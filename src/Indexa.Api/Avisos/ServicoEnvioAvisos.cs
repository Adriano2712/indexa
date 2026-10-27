using System.Net;
using Indexa.Api.Dados;
using Indexa.Api.Documentos;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;
using static Indexa.Api.Documentos.CartaReajustePdf;

namespace Indexa.Api.Avisos;

public enum ResultadoEnvio { Enviado, IgnoradoJaTratado, IgnoradoEmAndamento, Cancelado }

/// <summary>
/// Envio "pelo menos uma vez": a notificação é reservada (status Enviando + controle de versão) antes de falar com o SMTP.
/// Se o processo morrer entre o envio e a gravação, a reserva expira e o e-mail pode sair de novo; o Message-ID
/// fixo por notificação permite ao cliente de e-mail descartar a duplicata.
/// </summary>
public sealed class ServicoEnvioAvisos(
    IndexaDbContext db, INotifier notifier, ServicoDocumentos documentos, TimeProvider relogio, ILogger<ServicoEnvioAvisos> log)
{
    public async Task<ResultadoEnvio> EnviarAsync(Guid notificacaoId, CancellationToken ct)
    {
        var notificacao = await db.Notificacoes.SingleOrDefaultAsync(n => n.Id == notificacaoId, ct);
        if (notificacao is null || !notificacao.TentarIniciarEnvio(relogio.GetUtcNow()))
            return notificacao?.Status == StatusNotificacao.Enviando ? ResultadoEnvio.IgnoradoEmAndamento : ResultadoEnvio.IgnoradoJaTratado;

        var reajuste = await db.Reajustes.SingleAsync(r => r.Id == notificacao.ReajusteId, ct);
        if (reajuste.Status is StatusReajuste.Substituido or StatusReajuste.AguardandoIndice)
        {
            notificacao.Cancelar();
            await db.SaveChangesAsync(ct);
            return ResultadoEnvio.Cancelado;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            log.LogInformation("Notificação {Notificacao} reservada por outro worker.", notificacaoId);
            return ResultadoEnvio.IgnoradoEmAndamento;
        }

        string caminhoPdf;
        try
        {
            var pdf = await documentos.ObterOuGerarCartaAsync(reajuste, ct);
            caminhoPdf = reajuste.CaminhoPdf!;
            await notifier.EnviarAsync(await MontarMensagemAsync(notificacao, reajuste, pdf, ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Falha no envio da notificação {Notificacao} (tentativa {Tentativa}).", notificacaoId, notificacao.Tentativas);
            await RegistrarFalhaAsync(notificacaoId, ex, ct);
            throw;
        }

        // Fora do try: o e-mail já saiu; um conflito aqui não pode virar "falha de envio" e gerar reenvio.
        await ConfirmarEnvioAsync(notificacaoId, reajuste.Id, caminhoPdf, ct);
        return ResultadoEnvio.Enviado;
    }

    private async Task ConfirmarEnvioAsync(Guid notificacaoId, Guid reajusteId, string caminhoPdf, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                if (tentativa > 1) db.ChangeTracker.Clear();
                var notificacao = await db.Notificacoes.SingleAsync(n => n.Id == notificacaoId, ct);
                var reajuste = await db.Reajustes.SingleAsync(r => r.Id == reajusteId, ct);
                var agora = relogio.GetUtcNow();

                notificacao.RegistrarSucesso(agora);
                reajuste.RegistrarPdf(caminhoPdf);
                reajuste.MarcarNotificado(agora);
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 3)
            {
                // O processamento aplicou o reajuste ao mesmo tempo; recarrega e confirma de novo.
            }
        }
    }

    private async Task RegistrarFalhaAsync(Guid notificacaoId, Exception erro, CancellationToken ct)
    {
        // Contexto pode estar sujo (ex.: PDF registrado mas não salvo); grava a falha a partir de um estado limpo.
        db.ChangeTracker.Clear();
        var notificacao = await db.Notificacoes.SingleAsync(n => n.Id == notificacaoId, ct);
        notificacao.RegistrarFalha($"{erro.GetType().Name}: {erro.Message}");
        await db.SaveChangesAsync(ct);
    }

    private async Task<MensagemAviso> MontarMensagemAsync(Notificacao n, Reajuste r, byte[] pdf, CancellationToken ct)
    {
        var contrato = await db.Contratos.AsNoTracking().Include(c => c.Cliente).SingleAsync(c => c.Id == r.ContratoId, ct);
        var descricao = contrato.Descricao;
        var cliente = contrato.Cliente!.Nome;
        var valores = $"{Moeda(r.ValorAntigo)} → {Moeda(r.ValorNovo!.Value)}";
        var assunto = $"Reajuste em {Data(r.DataAniversario)}: {descricao} ({valores})";

        var texto =
            $"Contrato: {descricao}\nCliente: {cliente}\nAniversário: {Data(r.DataAniversario)}\n" +
            $"Índice: {NomeIndice(contrato.TipoIndice)}\nPercentual aplicado: {Percentual(r.PercentualAplicado!.Value, 4)}\n" +
            $"Regra: {DescreverRegra(r.RegraAplicada!.Value)}\nValor: {valores}\n\nA carta de reajuste segue em anexo.";

        Func<string?, string?> e = WebUtility.HtmlEncode;
        var html =
            $"<p>Reajuste do contrato <strong>{e(descricao)}</strong> ({e(cliente)}) em <strong>{Data(r.DataAniversario)}</strong>.</p>" +
            $"<p>{e(NomeIndice(contrato.TipoIndice))}: {Percentual(r.PercentualAplicado!.Value, 4)} &middot; {e(DescreverRegra(r.RegraAplicada!.Value))}</p>" +
            $"<p style=\"font-size:18px\"><strong>{e(valores)}</strong></p><p>A carta de reajuste segue em anexo.</p>";

        return new MensagemAviso(
            $"aviso-{n.Id:N}@indexa", n.Destinatario, assunto, texto, html,
            [new Anexo($"reajuste-{r.DataAniversario:yyyy-MM-dd}.pdf", "application/pdf", pdf)]);
    }
}

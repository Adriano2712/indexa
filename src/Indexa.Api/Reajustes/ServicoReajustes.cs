using System.Text.Json;
using Indexa.Api.Comum;
using Indexa.Api.Dados;
using Indexa.Api.Jobs;
using Indexa.Calculo;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Reajustes;

/// <summary>
/// Avança cada contrato pelos seus ciclos: cria o reajuste, calcula quando o índice existe, agenda o aviso
/// dentro da antecedência e aplica na data. Idempotente: pode rodar quantas vezes for preciso, inclusive em paralelo
/// (unicidade contrato + ciclo vigente e controle de versão no banco barram a segunda gravação).
/// </summary>
public sealed class ServicoReajustes(IndexaDbContext db, TimeProvider relogio, IFilaJobs fila, ILogger<ServicoReajustes> log)
{
    public const int LimiteCiclosPorExecucao = 600;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record ResumoProcessamento(int Contratos, int Falhas);

    public async Task<ResumoProcessamento> ProcessarTodosAsync(string origem, CancellationToken ct)
    {
        var ids = await db.Contratos.AsNoTracking().Where(c => c.Status == StatusContrato.Ativo).Select(c => c.Id).ToListAsync(ct);
        var falhas = new List<Exception>();

        foreach (var id in ids)
        {
            try
            {
                await ProcessarContratoAsync(id, origem, ct);
            }
            catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.EhViolacaoDeUnicidade())
            {
                // Outro worker gravou primeiro. A próxima execução continua de onde ele parou.
                log.LogInformation("Contrato {Contrato} processado em paralelo por outro worker; ignorando nesta execução.", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Falha ao processar contrato {Contrato}.", id);
                falhas.Add(ex);
            }
        }

        await ReenfileirarAvisosPendentesAsync(ct);

        // Um contrato com problema não impede os demais, mas a execução termina com falha visível no Hangfire.
        if (falhas.Count > 0) throw new AggregateException($"{falhas.Count} contrato(s) falharam no processamento.", falhas);
        return new ResumoProcessamento(ids.Count, 0);
    }

    public async Task ProcessarContratoAsync(Guid contratoId, string origem, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var contrato = await db.Contratos.Include(c => c.Cliente).SingleAsync(c => c.Id == contratoId, ct);
        if (contrato.Status != StatusContrato.Ativo) return;

        var usuario = await db.Usuarios.SingleAsync(u => u.Id == contrato.Cliente!.UsuarioId, ct);
        var hoje = relogio.HojeEmBrasilia();
        var dataCadastro = Relogio.DataEmBrasilia(contrato.CriadoEm);
        var clausulas = ClausulasReajuste.De(contrato);
        var variacoes = await CarregarVariacoesAsync(contrato, ct);
        var avisosNovos = new List<Guid>();

        for (var i = 0; i < LimiteCiclosPorExecucao; i++)
        {
            var agora = relogio.GetUtcNow();
            var ciclo = contrato.UltimoCicloAplicado + 1;
            var aniversario = Calendario.Aniversario(contrato.DataBase, ciclo, contrato.PeriodicidadeMeses);
            var retroativo = aniversario < dataCadastro;
            if (!retroativo && aniversario > hoje.AddDays(usuario.AntecedenciaAvisoDias)) break;

            var reajuste = await ObterOuCriarAsync(contrato, ciclo, aniversario, retroativo, origem, agora, ct);
            if (reajuste.Status == StatusReajuste.Aplicado)
                throw new InvalidOperationException(
                    $"Contrato {contrato.Id}: ciclo {ciclo} já aplicado, mas o contrato registra o ciclo {contrato.UltimoCicloAplicado}.");

            if (reajuste.Status == StatusReajuste.AguardandoIndice)
            {
                var resultado = MotorReajuste.Calcular(reajuste.ValorAntigo, aniversario, clausulas, variacoes);
                if (resultado is not ReajusteCalculado calculado)
                {
                    // Armadilha 1: sem índice, nada de cálculo parcial; a próxima execução tenta de novo.
                    await db.SaveChangesAsync(ct);
                    break;
                }
                RegistrarCalculo(reajuste, calculado, clausulas, agora);
            }

            if (!reajuste.Retroativo && usuario.AvisoEmailAtivo && reajuste.NotificadoEm is null &&
                !await db.Notificacoes.AnyAsync(n => n.ReajusteId == reajuste.Id && n.Canal == CanalNotificacao.Email, ct))
            {
                var aviso = Notificacao.Pendente(reajuste.Id, CanalNotificacao.Email, usuario.DestinatarioAviso, agora);
                db.Notificacoes.Add(aviso);
                avisosNovos.Add(aviso.Id);
            }

            var vencido = reajuste.Retroativo || aniversario <= hoje;
            if (vencido && reajuste.Status is StatusReajuste.Calculado or StatusReajuste.Notificado)
            {
                reajuste.Aplicar(agora);
                contrato.AplicarReajuste(ciclo, reajuste.ValorNovo!.Value, agora);
            }

            await db.SaveChangesAsync(ct);
            if (reajuste.Status != StatusReajuste.Aplicado) break;
        }

        foreach (var id in avisosNovos) fila.EnfileirarEnvioAviso(id);
    }

    /// <summary>
    /// Contrato com histórico nunca comunicado teve a base corrigida: marca o histórico como substituído.
    /// Não salva: o chamador grava junto com o contrato, no mesmo SaveChanges (uma transação), e depois reprocessa.
    /// </summary>
    public async Task SubstituirHistoricoAsync(Guid contratoId, CancellationToken ct)
    {
        var vigentes = await db.Reajustes.Where(r => r.ContratoId == contratoId && r.Status != StatusReajuste.Substituido).ToListAsync(ct);
        foreach (var r in vigentes) r.Substituir();

        var ids = vigentes.Select(r => r.Id).ToList();
        var avisos = await db.Notificacoes
            .Where(n => ids.Contains(n.ReajusteId) && (n.Status == StatusNotificacao.Pendente || n.Status == StatusNotificacao.Falhou))
            .ToListAsync(ct);
        foreach (var a in avisos) a.Cancelar();
    }

    public static Task<bool> PossuiReajustesComunicadosAsync(IndexaDbContext db, Guid contratoId, CancellationToken ct) =>
        db.Reajustes.AnyAsync(r => r.ContratoId == contratoId && r.Status != StatusReajuste.Substituido &&
                                   (r.NotificadoEm != null || r.Status == StatusReajuste.Notificado ||
                                    (r.Status == StatusReajuste.Aplicado && !r.Retroativo)), ct);

    /// <summary>
    /// Armadilha 6: a fonte revisou um mês. Reajustes ainda não emitidos que usaram esse mês são recalculados
    /// (nova revisão); os já notificados/aplicados nunca são reescritos, só sinalizados na divergência.
    /// </summary>
    public async Task TratarRevisaoDeIndiceAsync(IReadOnlyList<DivergenciaIndice> divergencias, CancellationToken ct)
    {
        var contratosAfetados = new HashSet<Guid>();
        foreach (var divergencia in divergencias)
        {
            var mes = divergencia.MesReferencia;
            var afetados = await db.Reajustes
                .Where(r => r.Status != StatusReajuste.Substituido && r.Status != StatusReajuste.AguardandoIndice &&
                            r.JanelaInicio <= mes && r.JanelaFim >= mes &&
                            db.Contratos.Any(c => c.Id == r.ContratoId && c.TipoIndice == divergencia.TipoIndice))
                .ToListAsync(ct);

            var emitido = false;
            var recalculados = 0;
            foreach (var reajuste in afetados)
            {
                if (reajuste.Status != StatusReajuste.Calculado || reajuste.NotificadoEm is not null)
                {
                    emitido = true;
                    log.LogWarning("Reajuste {Reajuste} já emitido usou {Tipo} {Mes:yyyy-MM} revisado; requer revisão manual.",
                        reajuste.Id, divergencia.TipoIndice, mes);
                    continue;
                }

                db.Reajustes.Add(reajuste.NovaRevisao("job:revisao-indice", relogio.GetUtcNow()));
                var avisos = await db.Notificacoes.Where(n => n.ReajusteId == reajuste.Id && n.Status == StatusNotificacao.Pendente).ToListAsync(ct);
                foreach (var a in avisos) a.Cancelar();
                contratosAfetados.Add(reajuste.ContratoId);
                recalculados++;
            }

            divergencia.RegistrarImpacto(emitido, recalculados);
        }

        await db.SaveChangesAsync(ct);
        foreach (var contratoId in contratosAfetados) await ProcessarContratoAsync(contratoId, "job:revisao-indice", ct);
    }

    private async Task ReenfileirarAvisosPendentesAsync(CancellationToken ct)
    {
        var limite = relogio.GetUtcNow() - Notificacao.ExpiracaoEnvio;
        var ids = await db.Notificacoes.AsNoTracking()
            .Where(n => (n.Status == StatusNotificacao.Pendente && n.CriadoEm < limite) ||
                        (n.Status == StatusNotificacao.Enviando && n.EnviandoDesde < limite))
            .Select(n => n.Id)
            .ToListAsync(ct);
        foreach (var id in ids) fila.EnfileirarEnvioAviso(id);
    }

    private async Task<Reajuste> ObterOuCriarAsync(
        Contrato contrato, int ciclo, DateOnly aniversario, bool retroativo, string origem, DateTimeOffset agora, CancellationToken ct)
    {
        var existente = await db.Reajustes.SingleOrDefaultAsync(
            r => r.ContratoId == contrato.Id && r.Ciclo == ciclo && r.Status != StatusReajuste.Substituido, ct);
        if (existente is not null) return existente;

        var revisao = await db.Reajustes.Where(r => r.ContratoId == contrato.Id && r.Ciclo == ciclo).MaxAsync(r => (int?)r.Revisao, ct);
        var novo = Reajuste.NovoAguardandoIndice(
            contrato.Id, ciclo, aniversario, contrato.ValorAtual, retroativo, origem, agora, revisao is null ? 0 : revisao.Value + 1);
        db.Reajustes.Add(novo);
        return novo;
    }

    private static void RegistrarCalculo(Reajuste reajuste, ReajusteCalculado c, ClausulasReajuste clausulas, DateTimeOffset agora) =>
        reajuste.RegistrarCalculo(
            c.ValorNovo, c.PercentualAcumulado, c.PercentualAplicado, c.Janela?.Inicio, c.Janela?.Fim, c.Regra,
            JsonSerializer.Serialize(c.IndicesUsados.Select(u => new VariacaoMensal(u.Mes, u.Variacao)), Json),
            JsonSerializer.Serialize(clausulas, Json),
            agora);

    private async Task<Dictionary<DateOnly, decimal>> CarregarVariacoesAsync(Contrato contrato, CancellationToken ct)
    {
        if (contrato.TipoIndice == TipoIndice.PercentualFixo) return [];
        var desde = contrato.DataBase.AddMonths(-(contrato.DefasagemMeses + 2));
        return await db.ValoresIndiceMensal.AsNoTracking()
            .Where(v => v.TipoIndice == contrato.TipoIndice && v.MesReferencia >= desde)
            .ToDictionaryAsync(v => v.MesReferencia, v => v.Valor, ct);
    }

    public static IReadOnlyList<VariacaoMensal> LerVariacoes(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<VariacaoMensal>>(json, Json) ?? [];
}

using Indexa.Api.Dados;
using Indexa.Api.Reajustes;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Documentos;

public sealed class ServicoDocumentos(IndexaDbContext db, IPdfGenerator pdf, IArmazenamentoDocumentos armazenamento, TimeProvider relogio)
{
    /// <summary>
    /// Gera a carta uma única vez e guarda o arquivo: o documento enviado ao responsável é o mesmo que fica na auditoria.
    /// Não chama SaveChanges; quem chama decide a transação.
    /// </summary>
    public async Task<byte[]> ObterOuGerarCartaAsync(Reajuste reajuste, CancellationToken ct)
    {
        if (reajuste.CaminhoPdf is { } caminho) return await armazenamento.LerAsync(caminho, ct);
        if (reajuste.ValorNovo is null) throw new RegraDeNegocioException("Reajuste ainda sem cálculo não tem carta.");

        var dados = await db.Contratos.AsNoTracking()
            .Where(c => c.Id == reajuste.ContratoId)
            .Select(c => new
            {
                c.Descricao,
                c.TipoIndice,
                ClienteNome = c.Cliente!.Nome,
                ClienteDocumento = c.Cliente.Documento,
                Responsavel = db.Usuarios.Where(u => u.Id == c.Cliente.UsuarioId).Select(u => u.Nome).Single(),
            })
            .SingleAsync(ct);

        var conteudo = pdf.GerarCartaReajuste(new DadosCartaReajuste(
            reajuste.Id, dados.Responsavel, dados.ClienteNome, dados.ClienteDocumento, dados.Descricao, reajuste.Ciclo,
            reajuste.DataAniversario, dados.TipoIndice, dados.TipoIndice == TipoIndice.PercentualFixo ? "contrato" : "Banco Central (SGS)",
            reajuste.ValorAntigo, reajuste.ValorNovo.Value, reajuste.PercentualAcumulado!.Value, reajuste.PercentualAplicado!.Value,
            reajuste.RegraAplicada!.Value, reajuste.JanelaInicio, reajuste.JanelaFim, ServicoReajustes.LerVariacoes(reajuste.IndicesUsadosJson),
            relogio.GetUtcNow()));

        var nome = $"reajustes/{reajuste.ContratoId:N}/ciclo-{reajuste.Ciclo}-rev-{reajuste.Revisao}-{reajuste.Id:N}.pdf";
        reajuste.RegistrarPdf(await armazenamento.SalvarAsync(nome, conteudo, ct));
        return conteudo;
    }
}

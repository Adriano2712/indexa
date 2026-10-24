using Indexa.Api.Comum;
using Indexa.Api.Dados;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Indexa.Api.Indices;

public sealed record ResultadoColeta(
    TipoIndice Tipo, bool Sucesso, int Inseridos, int Revisados, DateOnly? UltimoMesDisponivel, string? Erro);

public sealed class ServicoColetaIndices(
    IIndexProvider fonte, IndexaDbContext db, TimeProvider relogio, ILogger<ServicoColetaIndices> log)
{
    public static readonly TipoIndice[] IndicesColetados = [TipoIndice.Ipca, TipoIndice.IgpM];
    public static readonly DateOnly InicioCargaHistorica = new(1994, 1, 1);
    /// <summary>Meses re-baixados a cada coleta para detectar revisão da fonte.</summary>
    public const int MesesConferidos = 24;

    public async Task<IReadOnlyList<ResultadoColeta>> ColetarTodosAsync(CancellationToken ct)
    {
        var resultados = new List<ResultadoColeta>();
        foreach (var tipo in IndicesColetados) resultados.Add(await ColetarAsync(tipo, ct));
        return resultados;
    }

    public async Task<ResultadoColeta> ColetarAsync(TipoIndice tipo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var mesAtual = Calculo.Calendario.PrimeiroDiaDoMes(relogio.HojeEmBrasilia());
        var possuiDados = await db.ValoresIndiceMensal.AnyAsync(v => v.TipoIndice == tipo, ct);
        var desde = possuiDados ? mesAtual.AddMonths(-MesesConferidos) : InicioCargaHistorica;

        IReadOnlyList<ValorMensalPublicado> publicados;
        try
        {
            publicados = await fonte.ObterAsync(tipo, desde, mesAtual, ct);
        }
        catch (Exception ex) when (FonteIndisponivel(ex, ct))
        {
            // Armadilha 8: falha da fonte não derruba nada; seguimos com o que já está no banco e sinalizamos.
            var ultimo = await UltimoMesAsync(tipo, ct);
            log.LogWarning(ex, "Fonte {Fonte} indisponível para {Tipo}; dados locais vão até {Ultimo}.", fonte.Fonte, tipo, ultimo);
            return new ResultadoColeta(tipo, false, 0, 0, ultimo, $"Fonte indisponível ({ex.GetType().Name}).");
        }

        var existentes = await db.ValoresIndiceMensal
            .Where(v => v.TipoIndice == tipo && v.MesReferencia >= desde)
            .ToDictionaryAsync(v => v.MesReferencia, ct);

        int inseridos = 0, revisados = 0;
        foreach (var publicado in publicados.Where(p => p.MesReferencia <= mesAtual))
        {
            if (!existentes.TryGetValue(publicado.MesReferencia, out var guardado))
            {
                db.ValoresIndiceMensal.Add(ValorIndiceMensal.Criar(tipo, publicado.MesReferencia, publicado.Variacao, fonte.Fonte, agora));
                inseridos++;
            }
            else if (guardado.Valor != publicado.Variacao)
            {
                log.LogWarning("Índice {Tipo} {Mes:yyyy-MM} revisado pela fonte: {Anterior} → {Novo}.",
                    tipo, publicado.MesReferencia, guardado.Valor, publicado.Variacao);
                db.DivergenciasIndice.Add(guardado.Revisar(publicado.Variacao, fonte.Fonte, agora));
                revisados++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new ResultadoColeta(tipo, true, inseridos, revisados, await UltimoMesAsync(tipo, ct), null);
    }

    private Task<DateOnly?> UltimoMesAsync(TipoIndice tipo, CancellationToken ct) =>
        db.ValoresIndiceMensal.Where(v => v.TipoIndice == tipo).MaxAsync(v => (DateOnly?)v.MesReferencia, ct);

    private static bool FonteIndisponivel(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException or TimeoutRejectedException or BrokenCircuitException or FonteIndiceInvalidaException => true,
        TaskCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };
}

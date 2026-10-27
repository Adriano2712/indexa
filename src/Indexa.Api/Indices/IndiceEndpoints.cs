using Indexa.Api.Dados;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Indices;

public static class IndiceEndpoints
{
    public sealed record ValorIndiceResponse(DateOnly MesReferencia, decimal Variacao, string Fonte, DateTimeOffset ColetadoEm);

    public sealed record DivergenciaResponse(
        TipoIndice TipoIndice, DateOnly MesReferencia, decimal ValorAnterior, decimal ValorNovo, string Fonte, DateTimeOffset DetectadaEm,
        bool AfetaReajusteEmitido, int ReajustesRecalculados);

    // A coleta roda como job (Hangfire); disparo manual pelo painel /hangfire.
    public static void MapIndices(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/indices");
        g.MapGet("/divergencias", ListarDivergencias);
        g.MapGet("/{tipo}", Listar);
    }

    private static async Task<IResult> Listar(TipoIndice tipo, DateOnly? desde, DateOnly? ate, IndexaDbContext db, CancellationToken ct)
    {
        if (tipo == TipoIndice.PercentualFixo) return Results.NotFound();

        var q = db.ValoresIndiceMensal.AsNoTracking().Where(v => v.TipoIndice == tipo);
        if (desde is { } d) q = q.Where(v => v.MesReferencia >= d);
        if (ate is { } a) q = q.Where(v => v.MesReferencia <= a);

        var valores = await q.OrderBy(v => v.MesReferencia)
            .Select(v => new ValorIndiceResponse(v.MesReferencia, v.Valor, v.Fonte, v.ColetadoEm))
            .ToListAsync(ct);
        return Results.Ok(valores);
    }

    private static async Task<IResult> ListarDivergencias(IndexaDbContext db, CancellationToken ct) =>
        Results.Ok(await db.DivergenciasIndice.AsNoTracking()
            .OrderByDescending(d => d.DetectadaEm)
            .Take(200)
            .Select(d => new DivergenciaResponse(
                d.TipoIndice, d.MesReferencia, d.ValorAnterior, d.ValorNovo, d.Fonte, d.DetectadaEm, d.AfetaReajusteEmitido, d.ReajustesRecalculados))
            .ToListAsync(ct));
}

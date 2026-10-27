using System.Security.Claims;
using Indexa.Api.Auth;
using Indexa.Api.Comum;
using Indexa.Api.Dados;
using Indexa.Calculo;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Painel;

public static class PainelEndpoints
{
    public sealed record ProximoReajusteResponse(
        Guid ContratoId, string Descricao, string Cliente, int Ciclo, DateOnly Aniversario, int DiasRestantes, TipoIndice TipoIndice,
        decimal ValorAtual, decimal? ValorNovo, StatusReajuste? Status);

    public sealed record HistoricoReajusteResponse(
        Guid ReajusteId, Guid ContratoId, string Descricao, int Ciclo, DateOnly Aniversario, decimal ValorAntigo, decimal ValorNovo,
        decimal PercentualAplicado, RegraAplicada Regra, bool Retroativo, DateTimeOffset AplicadoEm);

    public sealed record PainelResponse(
        DateOnly Hoje, int ContratosAtivos, decimal ValorMensalAtivo, int ReajustesProximos30Dias, int AguardandoIndice,
        int AvisosComFalha, DateOnly? UltimoIpca, DateOnly? UltimoIgpM,
        IReadOnlyList<ProximoReajusteResponse> Proximos, IReadOnlyList<HistoricoReajusteResponse> Historico);

    public static void MapPainel(this IEndpointRouteBuilder api) => api.MapGet("/painel", Obter);

    private static async Task<IResult> Obter(ClaimsPrincipal user, IndexaDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var hoje = relogio.HojeEmBrasilia();
        var ativos = await db.Contratos.AsNoTracking()
            .Where(c => c.Cliente!.UsuarioId == usuarioId && c.Status == StatusContrato.Ativo)
            .Select(c => new { c.Id, c.Descricao, Cliente = c.Cliente!.Nome, c.DataBase, c.PeriodicidadeMeses, c.UltimoCicloAplicado, c.TipoIndice, c.ValorAtual })
            .ToListAsync(ct);
        var ids = ativos.Select(c => c.Id).ToList();

        var vigentes = await db.Reajustes.AsNoTracking()
            .Where(r => ids.Contains(r.ContratoId) && r.Status != StatusReajuste.Substituido && r.Status != StatusReajuste.Aplicado)
            .Select(r => new { r.ContratoId, r.Ciclo, r.ValorNovo, r.Status })
            .ToListAsync(ct);

        var proximos = ativos
            .Select(c =>
            {
                var ciclo = c.UltimoCicloAplicado + 1;
                var aniversario = Calendario.Aniversario(c.DataBase, ciclo, c.PeriodicidadeMeses);
                var reajuste = vigentes.FirstOrDefault(r => r.ContratoId == c.Id && r.Ciclo == ciclo);
                return new ProximoReajusteResponse(
                    c.Id, c.Descricao, c.Cliente, ciclo, aniversario, aniversario.DayNumber - hoje.DayNumber, c.TipoIndice, c.ValorAtual,
                    reajuste?.ValorNovo, reajuste?.Status);
            })
            .OrderBy(p => p.Aniversario)
            .ToList();

        var historico = await db.Reajustes.AsNoTracking()
            .Where(r => r.Status == StatusReajuste.Aplicado && db.Contratos.Any(c => c.Id == r.ContratoId && c.Cliente!.UsuarioId == usuarioId))
            .OrderByDescending(r => r.DataAniversario)
            .Take(10)
            .Select(r => new HistoricoReajusteResponse(
                r.Id, r.ContratoId, db.Contratos.Where(c => c.Id == r.ContratoId).Select(c => c.Descricao).Single(), r.Ciclo, r.DataAniversario,
                r.ValorAntigo, r.ValorNovo!.Value, r.PercentualAplicado!.Value, r.RegraAplicada!.Value, r.Retroativo, r.AplicadoEm!.Value))
            .ToListAsync(ct);

        var avisosComFalha = await db.Notificacoes.CountAsync(n => n.Status == StatusNotificacao.Falhou &&
            db.Reajustes.Any(r => r.Id == n.ReajusteId && ids.Contains(r.ContratoId)), ct);
        var ultimos = await db.ValoresIndiceMensal.AsNoTracking()
            .GroupBy(v => v.TipoIndice)
            .Select(g => new { Tipo = g.Key, Ultimo = g.Max(v => v.MesReferencia) })
            .ToListAsync(ct);

        return Results.Ok(new PainelResponse(
            hoje,
            ativos.Count,
            ativos.Sum(c => c.ValorAtual),
            proximos.Count(p => p.DiasRestantes is >= 0 and <= 30),
            vigentes.Count(r => r.Status == StatusReajuste.AguardandoIndice),
            avisosComFalha,
            ultimos.FirstOrDefault(u => u.Tipo == TipoIndice.Ipca)?.Ultimo,
            ultimos.FirstOrDefault(u => u.Tipo == TipoIndice.IgpM)?.Ultimo,
            proximos.Take(10).ToList(),
            historico));
    }
}

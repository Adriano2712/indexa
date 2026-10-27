using System.Security.Claims;
using Indexa.Api.Auth;
using Indexa.Api.Dados;
using Indexa.Api.Documentos;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Reajustes;

public static class ReajusteEndpoints
{
    public sealed record ConfiguracoesResponse(int AntecedenciaAvisoDias, string? EmailAviso, string DestinatarioEfetivo, bool AvisoEmailAtivo);
    public sealed record ConfiguracoesRequest(int AntecedenciaAvisoDias, string? EmailAviso, bool AvisoEmailAtivo);

    public static void MapReajustes(this IEndpointRouteBuilder api)
    {
        api.MapGet("/reajustes/{id:guid}/carta.pdf", BaixarCarta);
        api.MapGet("/configuracoes", ObterConfiguracoes);
        api.MapPut("/configuracoes", AtualizarConfiguracoes);
    }

    private static async Task<IResult> BaixarCarta(
        Guid id, ClaimsPrincipal user, IndexaDbContext db, ServicoDocumentos documentos, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var reajuste = await db.Reajustes.SingleOrDefaultAsync(
            r => r.Id == id && db.Contratos.Any(c => c.Id == r.ContratoId && c.Cliente!.UsuarioId == usuarioId), ct);
        if (reajuste is null) return Results.NotFound();
        if (reajuste.ValorNovo is null)
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Reajuste aguardando índice; carta ainda não disponível.");

        var pdf = await documentos.ObterOuGerarCartaAsync(reajuste, ct);
        await db.SaveChangesAsync(ct);
        return Results.File(pdf, "application/pdf", $"reajuste-ciclo-{reajuste.Ciclo}-{reajuste.DataAniversario:yyyy-MM-dd}.pdf");
    }

    private static async Task<IResult> ObterConfiguracoes(ClaimsPrincipal user, IndexaDbContext db, CancellationToken ct)
    {
        var u = await db.Usuarios.AsNoTracking().SingleAsync(x => x.Id == user.UsuarioId(), ct);
        return Results.Ok(Mapear(u));
    }

    private static async Task<IResult> AtualizarConfiguracoes(ConfiguracoesRequest req, ClaimsPrincipal user, IndexaDbContext db, CancellationToken ct)
    {
        var u = await db.Usuarios.SingleAsync(x => x.Id == user.UsuarioId(), ct);
        u.AtualizarPreferencias(req.AntecedenciaAvisoDias, req.EmailAviso, req.AvisoEmailAtivo);
        await db.SaveChangesAsync(ct);
        return Results.Ok(Mapear(u));
    }

    private static ConfiguracoesResponse Mapear(Usuario u) =>
        new(u.AntecedenciaAvisoDias, u.EmailAviso, u.DestinatarioAviso, u.AvisoEmailAtivo);
}

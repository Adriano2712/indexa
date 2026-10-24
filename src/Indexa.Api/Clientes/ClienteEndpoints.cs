using System.Linq.Expressions;
using System.Security.Claims;
using Indexa.Api.Auth;
using Indexa.Api.Comum;
using Indexa.Api.Dados;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Clientes;

public static class ClienteEndpoints
{
    public sealed record ClienteRequest(string? Nome, string? Documento, string? Email);

    public sealed record ClienteResponse(
        Guid Id, string Nome, string? Documento, string? Email, int QuantidadeContratos, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm);

    public static void MapClientes(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/clientes");
        g.MapGet("/", Listar);
        g.MapGet("/{id:guid}", Obter);
        g.MapPost("/", Criar);
        g.MapPut("/{id:guid}", Atualizar);
        g.MapDelete("/{id:guid}", Excluir);
    }

    private static Expression<Func<Cliente, ClienteResponse>> Projecao(IndexaDbContext db) => c =>
        new ClienteResponse(c.Id, c.Nome, c.Documento, c.Email, db.Contratos.Count(x => x.ClienteId == c.Id), c.CriadoEm, c.AtualizadoEm);

    private static async Task<IResult> Listar(
        ClaimsPrincipal user, IndexaDbContext db, string? busca, int? pagina, int? tamanhoPagina, CancellationToken ct)
    {
        var (numero, tamanho) = Paginacao.Normalizar(pagina, tamanhoPagina);
        var q = db.Clientes.AsNoTracking().Where(c => c.UsuarioId == user.UsuarioId());
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = Paginacao.PadraoContem(busca);
            q = q.Where(c => EF.Functions.ILike(c.Nome, padrao, "\\"));
        }

        var total = await q.CountAsync(ct);
        var itens = await q.OrderBy(c => c.Nome).ThenBy(c => c.Id)
            .Skip((numero - 1) * tamanho).Take(tamanho)
            .Select(Projecao(db)).ToListAsync(ct);
        return Results.Ok(new Pagina<ClienteResponse>(itens, total, numero, tamanho));
    }

    private static async Task<IResult> Obter(Guid id, ClaimsPrincipal user, IndexaDbContext db, CancellationToken ct)
    {
        var cliente = await ProjetarAsync(db, id, user.UsuarioId(), ct);
        return cliente is null ? Results.NotFound() : Results.Ok(cliente);
    }

    private static async Task<IResult> Criar(
        ClienteRequest req, ClaimsPrincipal user, IndexaDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var cliente = Cliente.Criar(user.UsuarioId(), req.Nome, req.Documento, req.Email, relogio.GetUtcNow());
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/clientes/{cliente.Id}", await ProjetarAsync(db, cliente.Id, cliente.UsuarioId, ct));
    }

    private static async Task<IResult> Atualizar(
        Guid id, ClienteRequest req, ClaimsPrincipal user, IndexaDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == usuarioId, ct);
        if (cliente is null) return Results.NotFound();

        cliente.Atualizar(req.Nome, req.Documento, req.Email, relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ProjetarAsync(db, id, usuarioId, ct));
    }

    private static async Task<IResult> Excluir(Guid id, ClaimsPrincipal user, IndexaDbContext db, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == usuarioId, ct);
        if (cliente is null) return Results.NotFound();
        if (await db.Contratos.AnyAsync(c => c.ClienteId == id, ct))
            throw new RegraDeNegocioException("Cliente possui contratos; encerre-os em vez de excluir o cliente.");

        db.Clientes.Remove(cliente);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Task<ClienteResponse?> ProjetarAsync(IndexaDbContext db, Guid id, Guid usuarioId, CancellationToken ct) =>
        db.Clientes.AsNoTracking().Where(c => c.Id == id && c.UsuarioId == usuarioId).Select(Projecao(db)).SingleOrDefaultAsync(ct);
}

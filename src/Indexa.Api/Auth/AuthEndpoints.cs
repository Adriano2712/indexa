using Indexa.Api.Dados;
using Indexa.Dominio;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.Auth;

public static class AuthEndpoints
{
    public sealed record RegistrarRequest(string? Nome, string? Email, string? Senha);
    public sealed record LoginRequest(string? Email, string? Senha);
    public sealed record UsuarioResponse(Guid Id, string Nome, string Email);
    public sealed record TokenResponse(string Token, DateTimeOffset ExpiraEm);

    public const string CookieSessao = "indexa_sessao";

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").AllowAnonymous();
        grupo.MapPost("/registrar", Registrar);
        grupo.MapPost("/login", Login).RequireRateLimiting("login");
        grupo.MapPost("/sair", Sair);
        app.MapGet("/api/auth/eu", Eu).RequireAuthorization();
    }

    // SameSite=Strict: o navegador não envia o cookie em requisições originadas de outro site (proteção contra CSRF).
    private static CookieOptions OpcoesCookie(IHostEnvironment ambiente, DateTimeOffset? expira) => new()
    {
        HttpOnly = true,
        Secure = !ambiente.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expira,
        IsEssential = true,
    };

    private static IResult Sair(HttpContext ctx, IHostEnvironment ambiente)
    {
        ctx.Response.Cookies.Delete(CookieSessao, OpcoesCookie(ambiente, null));
        return Results.NoContent();
    }

    private static async Task<IResult> Eu(System.Security.Claims.ClaimsPrincipal user, IndexaDbContext db, CancellationToken ct)
    {
        var id = user.UsuarioId();
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return usuario is null ? Results.Unauthorized() : Results.Ok(new UsuarioResponse(usuario.Id, usuario.Nome, usuario.Email));
    }

    private static async Task<IResult> Registrar(
        RegistrarRequest req, IndexaDbContext db, IPasswordHasher<Usuario> hasher, IConfiguration config, TimeProvider relogio,
        CancellationToken ct)
    {
        if (!config.GetValue<bool>("Auth:PermitirRegistro"))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Cadastro de novos usuários desabilitado.");

        var usuario = Usuario.Criar(req.Nome, req.Email, req.Senha, relogio.GetUtcNow());
        usuario.DefinirSenhaHash(hasher.HashPassword(usuario, req.Senha!));

        if (await db.Usuarios.AnyAsync(u => u.Email == usuario.Email, ct))
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "E-mail já cadastrado.");

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/usuarios/{usuario.Id}", new UsuarioResponse(usuario.Id, usuario.Nome, usuario.Email));
    }

    private static async Task<IResult> Login(
        LoginRequest req, IndexaDbContext db, IPasswordHasher<Usuario> hasher, EmissorToken emissor, HttpContext ctx,
        IHostEnvironment ambiente, CancellationToken ct)
    {
        var email = Usuario.NormalizarEmail(req.Email);
        var usuario = email is null ? null : await db.Usuarios.SingleOrDefaultAsync(u => u.Email == email, ct);
        var senha = req.Senha ?? "";

        var resultado = usuario is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senha);

        if (usuario is null || resultado == PasswordVerificationResult.Failed)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Credenciais inválidas.");

        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
        {
            usuario.DefinirSenhaHash(hasher.HashPassword(usuario, senha));
            await db.SaveChangesAsync(ct);
        }

        var (token, expiraEm) = emissor.Emitir(usuario);
        ctx.Response.Cookies.Append(CookieSessao, token, OpcoesCookie(ambiente, expiraEm));
        return Results.Ok(new TokenResponse(token, expiraEm));
    }
}

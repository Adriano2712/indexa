using System.Security.Claims;
using System.Text;
using Indexa.Dominio;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Indexa.Api.Auth;

public sealed class JwtOptions
{
    public string Chave { get; set; } = "";
    public string Emissor { get; set; } = "indexa";
    public string Audiencia { get; set; } = "indexa-web";
    public int ExpiracaoMinutos { get; set; } = 60;

    public SymmetricSecurityKey ChaveDeAssinatura() => new(Encoding.UTF8.GetBytes(Chave));
}

public sealed class EmissorToken(IOptions<JwtOptions> opcoes, TimeProvider relogio)
{
    public (string Token, DateTimeOffset ExpiraEm) Emitir(Usuario usuario)
    {
        var o = opcoes.Value;
        var agora = relogio.GetUtcNow();
        var expiraEm = agora.AddMinutes(o.ExpiracaoMinutos);

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = o.Emissor,
            Audience = o.Audiencia,
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expiraEm.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
                new Claim("name", usuario.Nome),
            ]),
            SigningCredentials = new SigningCredentials(o.ChaveDeAssinatura(), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descritor), expiraEm);
    }
}

public static class UsuarioAtual
{
    public static Guid UsuarioId(this ClaimsPrincipal usuario) =>
        Guid.Parse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Token sem claim 'sub'."));
}

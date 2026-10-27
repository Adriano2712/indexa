using System.Net;
using System.Net.Http.Json;
using Indexa.Api.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Indexa.Api.IntegrationTests;

[Collection("api")]
public sealed class SessaoCookieTests(ApiFactory api)
{
    private HttpClient Navegador() => api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<string> RegistrarAsync(HttpClient http)
    {
        var email = Auxiliares.NovoEmail();
        (await http.PostAsJsonAsync("/api/auth/registrar", new { nome = "Cookie", email, senha = Auxiliares.Senha })).EnsureSuccessStatusCode();
        return email;
    }

    [Fact]
    public async Task Login_grava_cookie_httponly_samesite_strict_que_autentica_sem_header()
    {
        var navegador = Navegador();
        var email = await RegistrarAsync(navegador);

        var login = await navegador.PostAsJsonAsync("/api/auth/login", new { email, senha = Auxiliares.Senha });

        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthEndpoints.CookieSessao + "="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var eu = await navegador.GetFromJsonAsync<AuthEndpoints.UsuarioResponse>("/api/auth/eu", Auxiliares.Json);
        Assert.Equal(email, eu!.Email);
        Assert.Null(navegador.DefaultRequestHeaders.Authorization);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/contratos")).StatusCode);
    }

    [Fact]
    public async Task Sair_apaga_o_cookie_e_a_sessao_deixa_de_valer()
    {
        var navegador = Navegador();
        var email = await RegistrarAsync(navegador);
        (await navegador.PostAsJsonAsync("/api/auth/login", new { email, senha = Auxiliares.Senha })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await navegador.PostAsync("/api/auth/sair", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/auth/eu")).StatusCode);
    }

    [Fact]
    public async Task Cookie_adulterado_nao_autentica()
    {
        var http = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/auth/eu");
        requisicao.Headers.Add("Cookie", $"{AuthEndpoints.CookieSessao}=eyJhbGciOiJIUzI1NiJ9.e30.invalido");

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(requisicao)).StatusCode);
    }
}

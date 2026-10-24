using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Indexa.Api.IntegrationTests;

[Collection("api")]
public sealed class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Token_do_login_acessa_endpoint_protegido()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/contratos")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_protegido_sem_token_retorna_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/contratos")).StatusCode);

    [Fact]
    public async Task Token_adulterado_retorna_401()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var token = http.DefaultRequestHeaders.Authorization!.Parameter!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/contratos")).StatusCode);
    }

    [Fact]
    public async Task Login_com_senha_errada_ou_email_inexistente_retorna_401()
    {
        var http = api.CreateClient();
        var email = Auxiliares.NovoEmail();
        await http.PostAsJsonAsync("/api/auth/registrar", new { nome = "Teste", email, senha = Auxiliares.Senha });

        var senhaErrada = await http.PostAsJsonAsync("/api/auth/login", new { email, senha = "outra-senha-qualquer" });
        var inexistente = await http.PostAsJsonAsync("/api/auth/login", new { email = Auxiliares.NovoEmail(), senha = Auxiliares.Senha });

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
    }

    [Fact]
    public async Task Registro_com_email_duplicado_ignora_caixa_e_retorna_409()
    {
        var http = api.CreateClient();
        var email = Auxiliares.NovoEmail();
        (await http.PostAsJsonAsync("/api/auth/registrar", new { nome = "A", email, senha = Auxiliares.Senha })).EnsureSuccessStatusCode();

        var duplicado = await http.PostAsJsonAsync("/api/auth/registrar", new { nome = "B", email = email.ToUpperInvariant(), senha = Auxiliares.Senha });

        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Registro_com_dados_invalidos_retorna_400_com_campos()
    {
        var resposta = await api.CreateClient().PostAsJsonAsync("/api/auth/registrar", new { nome = "", email = "nao-e-email", senha = "curta" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equivalent(new[] { "nome", "email", "senha" }, await resposta.CamposComErroAsync());
    }
}

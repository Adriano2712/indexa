using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Indexa.Api.Auth;
using Indexa.Api.Contratos;
using Indexa.Api.Dados;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Indexa.Api.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16.4-alpine").Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Indexa", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Chave", "chave-somente-para-testes-com-mais-de-32-bytes");
        builder.UseSetting("Auth:PermitirRegistro", "true");
        builder.UseSetting("Auth:LimiteLoginPorMinuto", "10000");
        builder.UseSetting("Banco:AplicarMigracoesAoIniciar", "true");
    }

    public async Task ComBancoAsync(Func<IndexaDbContext, Task> acao)
    {
        using var scope = Services.CreateScope();
        await acao(scope.ServiceProvider.GetRequiredService<IndexaDbContext>());
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

internal static class Auxiliares
{
    public const string Senha = "senha-segura-123";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string NovoEmail() => $"usuario-{Guid.NewGuid():N}@teste.com";

    public static async Task<HttpClient> CriarClienteAutenticadoAsync(this WebApplicationFactory<Program> api)
    {
        var http = api.CreateClient();
        var email = NovoEmail();
        (await http.PostAsJsonAsync("/api/auth/registrar", new { nome = "Teste", email, senha = Senha })).EnsureSuccessStatusCode();

        var login = await http.PostAsJsonAsync("/api/auth/login", new { email, senha = Senha });
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<AuthEndpoints.TokenResponse>(Json);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return http;
    }

    public static async Task<Guid> CriarClienteAsync(this HttpClient http, string nome = "Imobiliária Teste")
    {
        var resposta = await http.PostAsJsonAsync("/api/clientes", new { nome });
        resposta.EnsureSuccessStatusCode();
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Json);
        return corpo.GetProperty("id").GetGuid();
    }

    public static Dictionary<string, object?> ContratoIpca(Guid clienteId, string descricao = "Aluguel sala 101") => new()
    {
        ["clienteId"] = clienteId,
        ["descricao"] = descricao,
        ["valorInicial"] = 2500.00m,
        ["dataBase"] = "2025-03-10",
        ["tipoIndice"] = "Ipca",
    };

    public static async Task<ContratoEndpoints.ContratoResponse> CriarContratoAsync(this HttpClient http, object corpo)
    {
        var resposta = await http.PostAsJsonAsync("/api/contratos", corpo);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<ContratoEndpoints.ContratoResponse>(Json))!;
    }

    public static async Task<string[]> CamposComErroAsync(this HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Json);
        return corpo.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToArray();
    }
}

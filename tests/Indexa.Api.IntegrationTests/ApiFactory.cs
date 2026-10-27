using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Indexa.Api.Auth;
using Indexa.Api.Contratos;
using Indexa.Api.Dados;
using Indexa.Api.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Indexa.Api.IntegrationTests;

/// <summary>API real + Postgres em container. Hangfire sem servidor: os testes disparam os serviços dos jobs diretamente.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16.4-alpine").Build();

    public FilaFalsa Fila { get; } = new();
    public string PastaDocumentos { get; } = Path.Combine(Path.GetTempPath(), "indexa-testes", Guid.NewGuid().ToString("N"));

    public virtual Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
        await DescartarExtrasAsync();
        if (Directory.Exists(PastaDocumentos)) Directory.Delete(PastaDocumentos, recursive: true);
    }

    protected virtual Task DescartarExtrasAsync() => Task.CompletedTask;
    protected virtual void ConfigurarHost(IWebHostBuilder builder) { }
    protected virtual void ConfigurarServicos(IServiceCollection servicos) { }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Indexa", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Chave", "chave-somente-para-testes-com-mais-de-32-bytes");
        builder.UseSetting("Auth:PermitirRegistro", "true");
        builder.UseSetting("Auth:LimiteLoginPorMinuto", "10000");
        builder.UseSetting("Banco:AplicarMigracoesAoIniciar", "true");
        builder.UseSetting("Hangfire:Servidor", "false");
        builder.UseSetting("Hangfire:Painel", "false");
        builder.UseSetting("Armazenamento:Pasta", PastaDocumentos);
        ConfigurarHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IFilaJobs>(Fila);
            ConfigurarServicos(s);
        });
    }

    public async Task ComBancoAsync(Func<IndexaDbContext, Task> acao)
    {
        using var scope = Services.CreateScope();
        await acao(scope.ServiceProvider.GetRequiredService<IndexaDbContext>());
    }

    public async Task<T> ComServicoAsync<TServico, T>(Func<TServico, Task<T>> acao) where TServico : notnull
    {
        using var scope = Services.CreateScope();
        return await acao(scope.ServiceProvider.GetRequiredService<TServico>());
    }
}

/// <summary>Registra o que seria enfileirado no Hangfire.</summary>
public sealed class FilaFalsa : IFilaJobs
{
    public ConcurrentQueue<Guid> EnviosAviso { get; } = new();
    public int Processamentos;

    public void EnfileirarEnvioAviso(Guid notificacaoId) => EnviosAviso.Enqueue(notificacaoId);
    public void EnfileirarProcessamento() => Interlocked.Increment(ref Processamentos);
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

internal static class Auxiliares
{
    public const string Senha = "senha-segura-123";

    /// <summary>Data-base recente: o primeiro aniversário fica a ~11 meses, fora da antecedência, e nada é gerado no cadastro.</summary>
    public static readonly string DataBaseRecente = DateOnly.FromDateTime(DateTime.Today).AddMonths(-1).ToString("yyyy-MM-dd");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string NovoEmail() => $"usuario-{Guid.NewGuid():N}@teste.com";

    public static Task<HttpClient> CriarClienteAutenticadoAsync(this WebApplicationFactory<Program> api) =>
        api.CriarClienteAutenticadoAsync(NovoEmail());

    public static async Task<HttpClient> CriarClienteAutenticadoAsync(this WebApplicationFactory<Program> api, string email)
    {
        var http = api.CreateClient();
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

    public static Dictionary<string, object?> ContratoIpca(Guid clienteId, string descricao = "Aluguel sala 101", string? dataBase = null) => new()
    {
        ["clienteId"] = clienteId,
        ["descricao"] = descricao,
        ["valorInicial"] = 2500.00m,
        ["dataBase"] = dataBase ?? DataBaseRecente,
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

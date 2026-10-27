using System.Globalization;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Indexa.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Indexa.Api.IntegrationTests;

/// <summary>
/// Fixture do ciclo de vida completo: relógio ajustável, fonte de índices com as séries oficiais do SGS
/// (arquivos baixados em 24/10/2026) e Mailpit como servidor SMTP real.
/// </summary>
public sealed class ApiReajustesFactory : ApiFactory
{
    public static readonly DateTimeOffset Hoje = new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero); // 12:00 em Brasília

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.31.6")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/api/v1/info")))
        .Build();

    public RelogioAjustavel Relogio { get; } = new(Hoje);
    public FonteIndicesOficial Fonte { get; } = new();

    public override async Task InitializeAsync() => await Task.WhenAll(base.InitializeAsync(), _mailpit.StartAsync());

    protected override async Task DescartarExtrasAsync() => await _mailpit.DisposeAsync();

    public HttpClient Mailpit() => new() { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}/") };

    protected override void ConfigurarHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Smtp:Host", _mailpit.Hostname);
        builder.UseSetting("Smtp:Porta", _mailpit.GetMappedPublicPort(1025).ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Smtp:Seguranca", "None");
    }

    protected override void ConfigurarServicos(IServiceCollection servicos)
    {
        servicos.AddSingleton<TimeProvider>(Relogio);
        servicos.AddSingleton<IIndexProvider>(Fonte);
    }
}

[CollectionDefinition("reajustes")]
public sealed class ReajustesCollection : ICollectionFixture<ApiReajustesFactory>;

public sealed class RelogioAjustavel(DateTimeOffset inicial) : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = inicial;
    public override DateTimeOffset GetUtcNow() => Agora;
}

/// <summary>Serve as respostas oficiais do SGS. Permite simular atraso de divulgação e revisão da fonte.</summary>
public sealed class FonteIndicesOficial : IIndexProvider
{
    private static readonly IReadOnlyDictionary<TipoIndice, Dictionary<DateOnly, decimal>> Oficiais = new Dictionary<TipoIndice, Dictionary<DateOnly, decimal>>
    {
        [TipoIndice.Ipca] = Carregar("sgs-433-ipca.json"),
        [TipoIndice.IgpM] = Carregar("sgs-189-igpm.json"),
    };

    private readonly Dictionary<TipoIndice, Dictionary<DateOnly, decimal>> _publicados = [];
    private readonly object _trava = new();

    public FonteIndicesOficial() => Restaurar();

    public string Fonte => "SGS-teste";

    public static decimal Oficial(TipoIndice tipo, DateOnly mes) => Oficiais[tipo][mes];

    public void Restaurar()
    {
        lock (_trava)
            foreach (var (tipo, serie) in Oficiais) _publicados[tipo] = new Dictionary<DateOnly, decimal>(serie);
    }

    public void Remover(TipoIndice tipo, DateOnly mes) { lock (_trava) _publicados[tipo].Remove(mes); }

    public void Publicar(TipoIndice tipo, DateOnly mes, decimal valor) { lock (_trava) _publicados[tipo][mes] = valor; }

    public Task<IReadOnlyList<ValorMensalPublicado>> ObterAsync(TipoIndice tipo, DateOnly mesInicial, DateOnly mesFinal, CancellationToken ct)
    {
        lock (_trava)
            return Task.FromResult<IReadOnlyList<ValorMensalPublicado>>(_publicados[tipo]
                .Where(kv => kv.Key >= mesInicial && kv.Key <= mesFinal)
                .OrderBy(kv => kv.Key)
                .Select(kv => new ValorMensalPublicado(kv.Key, kv.Value))
                .ToList());
    }

    private sealed record Item(string Data, string Valor);

    private static Dictionary<DateOnly, decimal> Carregar(string arquivo) =>
        JsonSerializer.Deserialize<List<Item>>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Dados", arquivo)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            .ToDictionary(i => DateOnly.ParseExact(i.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture), i => decimal.Parse(i.Valor, CultureInfo.InvariantCulture));
}

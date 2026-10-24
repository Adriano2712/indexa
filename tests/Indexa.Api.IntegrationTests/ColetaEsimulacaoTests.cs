using System.Net;
using System.Net.Http.Json;
using Indexa.Api.Contratos;
using Indexa.Api.Indices;
using Indexa.Dominio;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Indexa.Api.IntegrationTests;

/// <summary>Coleção própria (banco próprio): estes testes mexem na tabela global de índices.</summary>
[CollectionDefinition("indices")]
public sealed class IndicesCollection : ICollectionFixture<ApiFactory>;

[Collection("indices")]
public sealed class ColetaEsimulacaoTests(ApiFactory api) : IAsyncLifetime
{
    // 24/10/2026 12:00 em Brasília.
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);

    // IPCA mensal oficial (SGS 433) de 2024 e 2025.
    private static readonly decimal[] Ipca2024 = [0.42m, 0.83m, 0.16m, 0.38m, 0.46m, 0.21m, 0.38m, -0.02m, 0.44m, 0.56m, 0.39m, 0.52m];
    private static readonly decimal[] Ipca2025 = [0.16m, 1.31m, 0.56m, 0.43m, 0.26m, 0.24m, 0.26m, -0.11m, 0.48m, 0.09m, 0.18m, 0.33m];

    private sealed class FonteFalsa : IIndexProvider
    {
        public List<ValorMensalPublicado> Publicados { get; } = [];
        public Exception? Falha { get; set; }
        public string Fonte => "fake";

        public Task<IReadOnlyList<ValorMensalPublicado>> ObterAsync(TipoIndice tipo, DateOnly mesInicial, DateOnly mesFinal, CancellationToken ct) =>
            Falha is not null
                ? Task.FromException<IReadOnlyList<ValorMensalPublicado>>(Falha)
                : Task.FromResult<IReadOnlyList<ValorMensalPublicado>>(
                    tipo == TipoIndice.Ipca ? Publicados.Where(p => p.MesReferencia >= mesInicial && p.MesReferencia <= mesFinal).ToList() : []);
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    private readonly FonteFalsa _fonte = new();

    public async Task InitializeAsync()
    {
        await api.ComBancoAsync(async db =>
        {
            await db.DivergenciasIndice.ExecuteDeleteAsync();
            await db.ValoresIndiceMensal.ExecuteDeleteAsync();
        });
        _fonte.Publicados.AddRange(Meses(2024, Ipca2024).Concat(Meses(2025, Ipca2025)));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static IEnumerable<ValorMensalPublicado> Meses(int ano, decimal[] valores) =>
        valores.Select((v, i) => new ValorMensalPublicado(new DateOnly(ano, i + 1, 1), v));

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> ComFonteFalsa() =>
        api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IIndexProvider>(_fonte);
            s.AddSingleton<TimeProvider>(new RelogioFixo(Agora));
        }));

    // O token precisa sair do relógio real: a validação de expiração do JwtBearer usa o relógio do sistema, não o TimeProvider.
    private async Task<HttpClient> ClienteAutenticadoAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        var autenticado = await api.CriarClienteAutenticadoAsync();
        var http = app.CreateClient();
        http.DefaultRequestHeaders.Authorization = autenticado.DefaultRequestHeaders.Authorization;
        return http;
    }

    private async Task<IReadOnlyList<ResultadoColeta>> ColetarAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ServicoColetaIndices>().ColetarTodosAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Primeira_coleta_carrega_historico_e_segunda_e_idempotente()
    {
        await using var app = ComFonteFalsa();

        var primeira = (await ColetarAsync(app)).Single(r => r.Tipo == TipoIndice.Ipca);
        var segunda = (await ColetarAsync(app)).Single(r => r.Tipo == TipoIndice.Ipca);

        Assert.True(primeira.Sucesso);
        Assert.Equal(24, primeira.Inseridos);
        Assert.Equal(new DateOnly(2025, 12, 1), primeira.UltimoMesDisponivel);
        Assert.Equal((true, 0, 0), (segunda.Sucesso, segunda.Inseridos, segunda.Revisados));
        await api.ComBancoAsync(async db => Assert.Equal(24, await db.ValoresIndiceMensal.CountAsync()));
    }

    [Fact]
    public async Task Revisao_da_fonte_gera_divergencia_e_atualiza_valor_vigente()
    {
        await using var app = ComFonteFalsa();
        await ColetarAsync(app);

        var mes = new DateOnly(2025, 3, 1);
        _fonte.Publicados.RemoveAll(p => p.MesReferencia == mes);
        _fonte.Publicados.Add(new ValorMensalPublicado(mes, 0.60m));
        var resultado = (await ColetarAsync(app)).Single(r => r.Tipo == TipoIndice.Ipca);

        Assert.Equal(1, resultado.Revisados);
        await api.ComBancoAsync(async db =>
        {
            var divergencia = await db.DivergenciasIndice.SingleAsync();
            Assert.Equal((mes, 0.56m, 0.60m), (divergencia.MesReferencia, divergencia.ValorAnterior, divergencia.ValorNovo));
            Assert.Equal(0.60m, (await db.ValoresIndiceMensal.SingleAsync(v => v.TipoIndice == TipoIndice.Ipca && v.MesReferencia == mes)).Valor);
        });
    }

    [Fact]
    public async Task Fonte_fora_do_ar_nao_derruba_e_informa_ultimo_mes_local()
    {
        await using var app = ComFonteFalsa();
        await ColetarAsync(app);

        _fonte.Falha = new HttpRequestException("BC fora do ar");
        var resultado = (await ColetarAsync(app)).Single(r => r.Tipo == TipoIndice.Ipca);

        Assert.False(resultado.Sucesso);
        Assert.Equal(new DateOnly(2025, 12, 1), resultado.UltimoMesDisponivel);
        Assert.Contains("indisponível", resultado.Erro);
        await api.ComBancoAsync(async db => Assert.Equal(24, await db.ValoresIndiceMensal.CountAsync()));
    }

    [Fact]
    public async Task Erro_de_programacao_na_fonte_nao_e_mascarado_como_indisponibilidade()
    {
        await using var app = ComFonteFalsa();
        _fonte.Falha = new InvalidOperationException("bug");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ColetarAsync(app));
    }

    [Fact]
    public async Task Simulacao_calcula_retroativos_com_indices_oficiais_e_deixa_proximo_aguardando()
    {
        await using var app = ComFonteFalsa();
        await ColetarAsync(app);
        var http = await ClienteAutenticadoAsync(app);
        var corpo = Auxiliares.ContratoIpca(await http.CriarClienteAsync());
        corpo["dataBase"] = "2024-02-10";
        var contrato = await http.CriarContratoAsync(corpo);

        var simulacao = await http.GetFromJsonAsync<ContratoEndpoints.SimulacaoResponse>(
            $"/api/contratos/{contrato.Id}/simulacao", Auxiliares.Json);

        Assert.Equal(new DateOnly(2026, 10, 24), simulacao!.Hoje);
        Assert.Collection(simulacao.Ciclos,
            c => Assert.Equal((1, true, StatusReajuste.Calculado, 2620.78m), (c.Ciclo, c.Retroativo, c.Status, c.ValorNovo)),
            c => Assert.Equal((2, true, StatusReajuste.Calculado, 2732.54m), (c.Ciclo, c.Retroativo, c.Status, c.ValorNovo)),
            c =>
            {
                // Próximo aniversário: 10/02/2027, janela jan–dez/2026; a fonte falsa só tem até dez/2025.
                Assert.Equal((3, false, StatusReajuste.AguardandoIndice), (c.Ciclo, c.Retroativo, c.Status));
                Assert.Equal(12, c.MesesFaltantes.Count);
                Assert.Null(c.ValorNovo);
            });
    }

    [Fact]
    public async Task Endpoints_de_indices_listam_valores_e_divergencias()
    {
        await using var app = ComFonteFalsa();
        var http = await ClienteAutenticadoAsync(app);

        var coleta = await http.PostAsync("/api/indices/coletar", null);
        var valores = await http.GetFromJsonAsync<List<IndiceEndpoints.ValorIndiceResponse>>(
            "/api/indices/Ipca?desde=2025-01-01&ate=2025-03-01", Auxiliares.Json);
        var divergencias = await http.GetAsync("/api/indices/divergencias");

        Assert.Equal(HttpStatusCode.OK, coleta.StatusCode);
        Assert.Equal([0.16m, 1.31m, 0.56m], valores!.Select(v => v.Variacao));
        Assert.Equal(HttpStatusCode.OK, divergencias.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().PostAsync("/api/indices/coletar", null)).StatusCode);
    }
}

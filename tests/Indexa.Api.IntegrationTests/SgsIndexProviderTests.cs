using System.Net;
using System.Text;
using Indexa.Api.Indices;
using Indexa.Dominio;

namespace Indexa.Api.IntegrationTests;

public sealed class SgsIndexProviderTests
{
    private sealed class HandlerFixo(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        public Uri? UltimaUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            UltimaUrl = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
        }
    }

    private static (SgsIndexProvider, HandlerFixo) Criar(HttpStatusCode status, string corpo)
    {
        var handler = new HandlerFixo(status, corpo);
        return (new SgsIndexProvider(new HttpClient(handler) { BaseAddress = new Uri("https://api.bcb.gov.br/") }), handler);
    }

    [Fact]
    public async Task Converte_resposta_real_do_sgs_e_monta_url_com_datas_invariantes()
    {
        var (sgs, handler) = Criar(HttpStatusCode.OK, """[{"data":"01/08/2026","valor":"-0.32"},{"data":"01/09/2026","valor":"0.82"}]""");

        var valores = await sgs.ObterAsync(TipoIndice.Ipca, new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 1), CancellationToken.None);

        Assert.Equal([new ValorMensalPublicado(new(2026, 8, 1), -0.32m), new ValorMensalPublicado(new(2026, 9, 1), 0.82m)], valores);
        Assert.Equal("/dados/serie/bcdata.sgs.433/dados?formato=json&dataInicial=01/08/2026&dataFinal=30/09/2026",
            handler.UltimaUrl!.PathAndQuery);
    }

    [Fact]
    public async Task Igpm_usa_serie_189() =>
        Assert.Contains("bcdata.sgs.189", await UrlDe(TipoIndice.IgpM));

    private static async Task<string> UrlDe(TipoIndice tipo)
    {
        var (sgs, handler) = Criar(HttpStatusCode.OK, "[]");
        await sgs.ObterAsync(tipo, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), CancellationToken.None);
        return handler.UltimaUrl!.ToString();
    }

    [Fact]
    public async Task Resposta_404_value_not_found_significa_mes_ainda_nao_divulgado()
    {
        var (sgs, _) = Criar(HttpStatusCode.NotFound,
            """{"erro":{"statusCode":404,"detail":"br.gov.bcb.pec.sgs.comum.excecoes.SGSNegocioException: Value(s) not found"}}""");

        Assert.Empty(await sgs.ObterAsync(TipoIndice.Ipca, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Outro_404_e_erro_e_nao_lista_vazia()
    {
        var (sgs, _) = Criar(HttpStatusCode.NotFound, "<html>pagina nao existe</html>");
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sgs.ObterAsync(TipoIndice.Ipca, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Erro_500_propaga_para_a_politica_de_resiliencia()
    {
        var (sgs, _) = Criar(HttpStatusCode.InternalServerError, "falha");
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sgs.ObterAsync(TipoIndice.Ipca, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"inesperado":true}""")]
    [InlineData("""[{"data":"2026-09-01","valor":"0.82"}]""")]
    [InlineData("""[{"data":"01/09/2026","valor":"0,82"}]""")]
    [InlineData("""[{"data":"01/09/2026","valor":""}]""")]
    public async Task Formato_inesperado_vira_fonte_invalida(string corpo)
    {
        var (sgs, _) = Criar(HttpStatusCode.OK, corpo);
        await Assert.ThrowsAsync<FonteIndiceInvalidaException>(() =>
            sgs.ObterAsync(TipoIndice.Ipca, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), CancellationToken.None));
    }

    /// <summary>Chama a API real. Rode com INDEXA_TESTES_EXTERNOS=1.</summary>
    [FactExterno]
    public async Task Api_real_do_sgs_responde_ipca_de_2025_conferido()
    {
        var sgs = new SgsIndexProvider(new HttpClient { BaseAddress = new Uri("https://api.bcb.gov.br/"), Timeout = TimeSpan.FromSeconds(30) });

        var valores = await sgs.ObterAsync(TipoIndice.Ipca, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 1), CancellationToken.None);

        Assert.Equal(12, valores.Count);
        Assert.Equal(1.31m, valores.Single(v => v.MesReferencia == new DateOnly(2025, 2, 1)).Variacao);
    }
}

public sealed class FactExternoAttribute : FactAttribute
{
    public FactExternoAttribute()
    {
        if (Environment.GetEnvironmentVariable("INDEXA_TESTES_EXTERNOS") != "1")
            Skip = "Teste contra API externa: defina INDEXA_TESTES_EXTERNOS=1.";
    }
}

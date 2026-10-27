using Indexa.Api.Documentos;
using Indexa.Api.Jobs;
using Indexa.Dominio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Indexa.Api.IntegrationTests;

public sealed class AlertasECartaTests
{
    private sealed class NotifierGravador : INotifier
    {
        public List<MensagemAviso> Enviadas { get; } = [];
        public Task EnviarAsync(MensagemAviso mensagem, CancellationToken ct) { Enviadas.Add(mensagem); return Task.CompletedTask; }
    }

    private static ServicoAlertas Alertas(NotifierGravador notifier, string? email) =>
        new(notifier, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Alertas:Email"] = email }).Build(),
            NullLogger<ServicoAlertas>.Instance);

    [Fact]
    public async Task Job_esgotado_gera_email_de_alerta_com_html_escapado()
    {
        var notifier = new NotifierGravador();

        var enviado = await Alertas(notifier, "admin@exemplo.com")
            .NotificarFalhaAsync("42", "EnvioAvisoJob.ExecutarAsync", "IOException: <smtp> recusou", CancellationToken.None);

        Assert.True(enviado);
        var m = Assert.Single(notifier.Enviadas);
        Assert.Equal("admin@exemplo.com", m.Para);
        Assert.Contains("EnvioAvisoJob", m.Assunto);
        Assert.Contains("&lt;smtp&gt;", m.CorpoHtml);
        Assert.DoesNotContain("<smtp>", m.CorpoHtml);
    }

    [Fact]
    public async Task Sem_destinatario_configurado_nao_envia()
    {
        var notifier = new NotifierGravador();
        Assert.False(await Alertas(notifier, null).NotificarFalhaAsync("1", "X", "erro", CancellationToken.None));
        Assert.Empty(notifier.Enviadas);
    }

    private static DadosCartaReajuste Carta(DateOnly aniversario, DateTimeOffset geradoEm) => new(
        Guid.NewGuid(), "Ana", "Cliente", null, "Sala", 1, aniversario, TipoIndice.Ipca, "SGS", 2500m, 2606.61m, 4.26m, 4.26m,
        RegraAplicada.Indice, null, null, [], geradoEm);

    [Theory]
    [InlineData(2026, 11, 5, "será reajustado")]
    [InlineData(2026, 10, 24, "foi reajustado")]   // vigência no próprio dia da emissão
    [InlineData(2025, 2, 10, "foi reajustado")]    // retroativo
    public void Texto_da_carta_acompanha_a_data_de_vigencia(int ano, int mes, int dia, string esperado)
    {
        var emitidaEm = new DateTimeOffset(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);
        Assert.Contains(esperado, CartaReajustePdf.Comunicado(Carta(new DateOnly(ano, mes, dia), emitidaEm)));
    }

    [Fact]
    public void Carta_gera_pdf_valido()
    {
        var pdf = new CartaReajustePdf().GerarCartaReajuste(Carta(new DateOnly(2026, 11, 5), DateTimeOffset.UtcNow));
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }
}

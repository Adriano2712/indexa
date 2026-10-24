using System.Globalization;
using System.Net;
using System.Text.Json;
using Indexa.Dominio;

namespace Indexa.Api.Indices;

/// <summary>
/// API de dados abertos do Banco Central (SGS). Comportamento verificado em 24/10/2026:
/// JSON [{"data":"dd/MM/aaaa","valor":"0.82"}]; intervalo sem dados responde 404 "Value(s) not found".
/// </summary>
public sealed class SgsIndexProvider(HttpClient http) : IIndexProvider
{
    public string Fonte => "BCB-SGS";

    public static int CodigoSerie(TipoIndice tipo) => tipo switch
    {
        TipoIndice.Ipca => 433,
        TipoIndice.IgpM => 189,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Índice sem série no SGS."),
    };

    private sealed record ItemSgs(string? Data, string? Valor);

    public async Task<IReadOnlyList<ValorMensalPublicado>> ObterAsync(
        TipoIndice tipo, DateOnly mesInicial, DateOnly mesFinal, CancellationToken ct)
    {
        var inicio = Formatar(new DateOnly(mesInicial.Year, mesInicial.Month, 1));
        var fim = Formatar(new DateOnly(mesFinal.Year, mesFinal.Month, 1).AddMonths(1).AddDays(-1));
        var url = $"dados/serie/bcdata.sgs.{CodigoSerie(tipo)}/dados?formato=json&dataInicial={inicio}&dataFinal={fim}";

        using var resposta = await http.GetAsync(url, ct);
        var corpo = await resposta.Content.ReadAsStringAsync(ct);

        if (resposta.StatusCode == HttpStatusCode.NotFound && corpo.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return [];
        resposta.EnsureSuccessStatusCode();

        List<ItemSgs>? itens;
        try
        {
            itens = JsonSerializer.Deserialize<List<ItemSgs>>(corpo, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            throw new FonteIndiceInvalidaException($"SGS série {CodigoSerie(tipo)}: JSON inesperado.", ex);
        }

        return (itens ?? throw new FonteIndiceInvalidaException("SGS retornou corpo vazio.")).Select(Converter).ToList();
    }

    private static string Formatar(DateOnly data) => data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static ValorMensalPublicado Converter(ItemSgs item)
    {
        if (!DateOnly.TryParseExact(item.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) || data.Day != 1)
            throw new FonteIndiceInvalidaException($"Data inesperada no SGS: '{item.Data}'.");
        if (!decimal.TryParse(item.Valor, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
            throw new FonteIndiceInvalidaException($"Valor inesperado no SGS para {item.Data}: '{item.Valor}'.");
        return new ValorMensalPublicado(data, valor);
    }
}

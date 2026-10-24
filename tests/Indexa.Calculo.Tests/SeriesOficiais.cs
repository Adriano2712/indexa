using System.Globalization;
using System.Text.Json;

namespace Indexa.Calculo.Tests;

/// <summary>
/// Respostas brutas da API SGS do Banco Central, baixadas em 24/10/2026 (conferíveis em
/// https://api.bcb.gov.br/dados/serie/bcdata.sgs.{codigo}/dados?formato=json&amp;dataInicial=..&amp;dataFinal=..).
/// 433 = IPCA variação mensal, 13522 = IPCA acumulado 12 meses, 189 = IGP-M variação mensal.
/// </summary>
internal static class SeriesOficiais
{
    public static readonly IReadOnlyDictionary<DateOnly, decimal> Ipca = Carregar("sgs-433-ipca.json");
    public static readonly IReadOnlyDictionary<DateOnly, decimal> IpcaAcumulado12Meses = Carregar("sgs-13522-ipca-12m.json");
    public static readonly IReadOnlyDictionary<DateOnly, decimal> IgpM = Carregar("sgs-189-igpm.json");

    private sealed record Item(string Data, string Valor);

    private static Dictionary<DateOnly, decimal> Carregar(string arquivo)
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "Dados", arquivo);
        var itens = JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(caminho), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return itens.ToDictionary(
            i => DateOnly.ParseExact(i.Data, "dd/MM/yyyy", CultureInfo.InvariantCulture),
            i => decimal.Parse(i.Valor, CultureInfo.InvariantCulture));
    }
}

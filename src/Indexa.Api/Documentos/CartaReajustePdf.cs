using System.Globalization;
using Indexa.Dominio;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Indexa.Api.Documentos;

/// <summary>Carta de reajuste em PDF (QuestPDF, licença Community: receita bruta anual abaixo de US$ 1 mi).</summary>
public sealed class CartaReajustePdf : IPdfGenerator
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    static CartaReajustePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Só fontes embutidas no QuestPDF: evita varrer as fontes do sistema e o PDF sai igual em Windows e Linux.
        QuestPDF.Settings.UseSystemFonts = false;
    }

    public static string Moeda(decimal valor) => valor.ToString("C", PtBr);
    public static string Percentual(decimal valor, int casas) => valor.ToString("N" + casas, PtBr) + "%";
    public static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", PtBr);
    public static string Mes(DateOnly mes) => mes.ToString("MM/yyyy", PtBr);

    public static string NomeIndice(TipoIndice tipo) => tipo switch
    {
        TipoIndice.Ipca => "IPCA (IBGE)",
        TipoIndice.IgpM => "IGP-M (FGV)",
        _ => "Percentual fixo contratual",
    };

    public static string DescreverRegra(RegraAplicada regra) => regra switch
    {
        RegraAplicada.Indice => "Variação integral do índice",
        RegraAplicada.PercentualFixo => "Percentual fixo previsto em contrato",
        RegraAplicada.Teto => "Limitado pelo teto contratual",
        RegraAplicada.Piso => "Limitado pelo piso contratual",
        RegraAplicada.NaoReducao => "Cláusula de não redução (índice negativo no período)",
        _ => regra.ToString(),
    };

    public byte[] GerarCartaReajuste(DadosCartaReajuste d) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(t => t.FontSize(10.5f));

        page.Header().Column(c =>
        {
            c.Item().Text("Comunicado de reajuste contratual").SemiBold().FontSize(17);
            c.Item().Text($"{d.ContratoDescricao} · ciclo {d.Ciclo}").FontColor(Colors.Grey.Darken1);
        });

        page.Content().PaddingVertical(16).Column(c =>
        {
            c.Spacing(10);
            c.Item().Text(t =>
            {
                t.Span("Cliente: ").SemiBold();
                t.Span(d.ClienteDocumento is null ? d.ClienteNome : $"{d.ClienteNome} ({d.ClienteDocumento})");
            });
            c.Item().Text(Comunicado(d));

            c.Item().Table(t =>
            {
                t.ColumnsDefinition(cols => { cols.RelativeColumn(2); cols.RelativeColumn(3); });
                Linha(t, "Índice", NomeIndice(d.TipoIndice));
                if (d.JanelaInicio is { } inicio && d.JanelaFim is { } fim)
                    Linha(t, "Período apurado", $"{Mes(inicio)} a {Mes(fim)}");
                Linha(t, "Variação acumulada", Percentual(d.PercentualAcumulado, 4));
                Linha(t, "Percentual aplicado", Percentual(d.PercentualAplicado, 4));
                Linha(t, "Regra aplicada", DescreverRegra(d.Regra));
                Linha(t, "Valor anterior", Moeda(d.ValorAntigo));
                Linha(t, "Novo valor", Moeda(d.ValorNovo));
                Linha(t, "Vigência", $"a partir de {Data(d.Aniversario)}");
            });

            if (d.Variacoes.Count > 0)
            {
                c.Item().PaddingTop(6).Text($"Variações mensais utilizadas (fonte: {d.FonteIndice})").SemiBold();
                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.RelativeColumn(); });
                    t.Header(h =>
                    {
                        h.Cell().BorderBottom(1).Padding(3).Text("Mês").SemiBold();
                        h.Cell().BorderBottom(1).Padding(3).AlignRight().Text("Variação").SemiBold();
                    });
                    foreach (var v in d.Variacoes)
                    {
                        t.Cell().Padding(3).Text(Mes(v.Mes));
                        t.Cell().Padding(3).AlignRight().Text(Percentual(v.Variacao, 2));
                    }
                });
            }

            c.Item().PaddingTop(10).Text(
                "Acumulado = produto de (1 + variação mensal/100) no período, menos 1. " +
                "O novo valor é arredondado uma única vez, a duas casas decimais.").FontSize(9).FontColor(Colors.Grey.Darken1);
        });

        page.Footer().Column(c =>
        {
            c.Item().Text($"Responsável: {d.ResponsavelNome}").FontSize(9);
            c.Item().Text(
                $"Documento informativo gerado pelo Indexa em {TimeZoneInfo.ConvertTime(d.GeradoEm, Comum.Relogio.Brasilia):dd/MM/yyyy HH:mm} (Brasília). " +
                $"Não substitui aditivo assinado pelas partes. Ref. {d.ReajusteId}").FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    })).GeneratePdf();

    /// <summary>Vigência já passada na data de emissão (ex.: reajuste retroativo) é descrita no passado.</summary>
    public static string Comunicado(DadosCartaReajuste d)
    {
        var emitidaEm = Comum.Relogio.DataEmBrasilia(d.GeradoEm);
        var valores = $"passando de {Moeda(d.ValorAntigo)} para {Moeda(d.ValorNovo)}";
        return d.Aniversario <= emitidaEm
            ? $"Conforme as cláusulas do contrato, o valor foi reajustado a partir de {Data(d.Aniversario)}, {valores}."
            : $"Conforme as cláusulas do contrato, o valor será reajustado a partir de {Data(d.Aniversario)}, {valores}.";
    }

    private static void Linha(TableDescriptor t, string rotulo, string valor)
    {
        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(rotulo).FontColor(Colors.Grey.Darken2);
        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(valor).SemiBold();
    }
}

namespace Indexa.Calculo.Tests;

/// <summary>
/// Confere o acumulado do motor contra a série oficial 13522 (IPCA acumulado em 12 meses, publicada com 2 casas).
/// </summary>
public sealed class OraculoIpcaTests
{
    // O IBGE acumula a partir do número-índice; as variações mensais vêm arredondadas a 2 casas.
    // Por isso o produto das mensais pode diferir em 0,01 p.p. do número oficial (ADR-07). Estes são os casos conhecidos.
    private static readonly DateOnly[] DivergenciasConhecidas = [new(2018, 5, 1), new(2020, 2, 1), new(2022, 12, 1)];

    public static TheoryData<DateOnly> MesesDoOraculo()
    {
        var dados = new TheoryData<DateOnly>();
        foreach (var mes in SeriesOficiais.IpcaAcumulado12Meses.Keys.Where(m => SeriesOficiais.Ipca.ContainsKey(m.AddMonths(-11))))
            dados.Add(mes);
        return dados;
    }

    private static decimal AcumuladoDoMotor(DateOnly fim)
    {
        var meses = Enumerable.Range(0, 12).Select(i => fim.AddMonths(-11 + i));
        return decimal.Round(MotorReajuste.Acumular(meses.Select(m => SeriesOficiais.Ipca[m])) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    [Theory]
    [MemberData(nameof(MesesDoOraculo))]
    public void Acumulado_12_meses_bate_com_a_serie_oficial(DateOnly fim)
    {
        var oficial = SeriesOficiais.IpcaAcumulado12Meses[fim];
        var motor = AcumuladoDoMotor(fim);

        if (DivergenciasConhecidas.Contains(fim))
            Assert.Equal(0.01m, Math.Abs(oficial - motor));
        else
            Assert.Equal(oficial, motor);
    }

    [Fact]
    public void Oraculo_cobre_mais_de_cem_janelas() => Assert.True(MesesDoOraculo().Count >= 120);
}

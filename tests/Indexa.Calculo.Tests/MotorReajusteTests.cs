using Indexa.Dominio;

namespace Indexa.Calculo.Tests;

/// <summary>
/// Valores esperados calculados de forma independente (Python decimal) a partir das séries oficiais do SGS.
/// Conferência rápida: IPCA 2024 = 4,83% e IPCA 2025 = 4,26% (série 13522); IGP-M 2024 = 6,54%.
/// </summary>
public sealed class MotorReajusteTests
{
    private static DateOnly D(int a, int m, int d) => new(a, m, d);

    // Aniversário em fev/2026 com defasagem 1 → janela jan–dez/2025 (o índice de jan/2026 fica de fora).
    private static readonly DateOnly AniversarioJanela2025 = D(2026, 2, 10);

    private static ClausulasReajuste Clausulas(
        TipoIndice tipo = TipoIndice.Ipca, decimal? fixo = null, decimal? teto = null, decimal? piso = null,
        UnidadeLimite? unidade = null, bool naoReduzir = false, int defasagem = 1) =>
        new(tipo, fixo, 12, defasagem, teto, piso, unidade ?? (teto ?? piso) switch { null => null, _ => UnidadeLimite.Percentual }, naoReduzir);

    private static ReajusteCalculado Calcular(ClausulasReajuste c, decimal valor = 2500.00m, IReadOnlyDictionary<DateOnly, decimal>? serie = null) =>
        Assert.IsType<ReajusteCalculado>(MotorReajuste.Calcular(
            valor, AniversarioJanela2025, c, serie ?? (c.TipoIndice == TipoIndice.IgpM ? SeriesOficiais.IgpM : SeriesOficiais.Ipca)));

    [Fact]
    public void Ipca_2025_real_sem_clausulas()
    {
        var r = Calcular(Clausulas());

        Assert.Equal(2606.61m, r.ValorNovo);
        Assert.Equal(4.2643849045689209876942781m, decimal.Round(r.PercentualAcumulado, 25));
        Assert.Equal(r.PercentualAcumulado, r.PercentualAplicado);
        Assert.Equal(RegraAplicada.Indice, r.Regra);
        Assert.Equal(new Janela(D(2025, 1, 1), D(2025, 12, 1)), r.Janela);
        Assert.Equal(12, r.IndicesUsados.Count);
        Assert.Equal(new IndiceUsado(D(2025, 8, 1), -0.11m), r.IndicesUsados[7]);
    }

    [Fact]
    public void Igpm_2025_negativo_sem_clausula_reduz_o_valor()
    {
        var r = Calcular(Clausulas(TipoIndice.IgpM));

        Assert.Equal(2473.95m, r.ValorNovo);
        Assert.True(r.PercentualAcumulado < 0);
        Assert.Equal(RegraAplicada.Indice, r.Regra);
    }

    [Fact]
    public void Igpm_2025_negativo_com_nao_reduzir_mantem_o_valor()
    {
        var r = Calcular(Clausulas(TipoIndice.IgpM, naoReduzir: true));

        Assert.Equal(2500.00m, r.ValorNovo);
        Assert.Equal(0m, r.PercentualAplicado);
        Assert.True(r.PercentualAcumulado < 0, "o acumulado bruto continua registrado para auditoria");
        Assert.Equal(RegraAplicada.NaoReducao, r.Regra);
    }

    [Fact]
    public void Teto_percentual_limita_o_acumulado()
    {
        var r = Calcular(Clausulas(teto: 4m));

        Assert.Equal(2600.00m, r.ValorNovo);
        Assert.Equal(4m, r.PercentualAplicado);
        Assert.Equal(RegraAplicada.Teto, r.Regra);
    }

    [Fact]
    public void Teto_acima_do_acumulado_nao_e_aplicado()
    {
        var r = Calcular(Clausulas(teto: 4.27m));
        Assert.Equal(RegraAplicada.Indice, r.Regra);
        Assert.Equal(2606.61m, r.ValorNovo);
    }

    [Fact]
    public void Piso_zero_impede_reajuste_negativo_e_registra_piso()
    {
        var r = Calcular(Clausulas(TipoIndice.IgpM, piso: 0m));

        Assert.Equal(2500.00m, r.ValorNovo);
        Assert.Equal(RegraAplicada.Piso, r.Regra);
    }

    [Fact]
    public void Piso_negativo_limita_a_reducao()
    {
        var r = Calcular(Clausulas(TipoIndice.IgpM, piso: -0.5m));

        Assert.Equal(2487.50m, r.ValorNovo);
        Assert.Equal(-0.5m, r.PercentualAplicado);
        Assert.Equal(RegraAplicada.Piso, r.Regra);
    }

    [Fact]
    public void Piso_negativo_com_nao_reduzir_termina_em_nao_reducao()
    {
        var r = Calcular(Clausulas(TipoIndice.IgpM, piso: -0.5m, naoReduzir: true));

        Assert.Equal(2500.00m, r.ValorNovo);
        Assert.Equal(RegraAplicada.NaoReducao, r.Regra);
    }

    [Fact]
    public void Teto_em_valor_limita_o_aumento_absoluto()
    {
        // Aumento bruto do IPCA 2025 sobre R$ 2.500,00 = R$ 106,6096...; teto de R$ 50,00.
        var r = Calcular(Clausulas(teto: 50m, unidade: UnidadeLimite.Valor));

        Assert.Equal(2550.00m, r.ValorNovo);
        Assert.Equal(2m, r.PercentualAplicado);
        Assert.Equal(RegraAplicada.Teto, r.Regra);
    }

    [Fact]
    public void Percentual_fixo_ignora_serie_e_janela()
    {
        var r = Assert.IsType<ReajusteCalculado>(MotorReajuste.Calcular(
            1234.56m, AniversarioJanela2025, Clausulas(TipoIndice.PercentualFixo, fixo: 5m), new Dictionary<DateOnly, decimal>()));

        Assert.Equal(1296.29m, r.ValorNovo);
        Assert.Equal(RegraAplicada.PercentualFixo, r.Regra);
        Assert.Null(r.Janela);
        Assert.Empty(r.IndicesUsados);
    }

    [Theory]
    [InlineData("1000.10", "1050.11")] // 1050,105 → meio exato: arredonda para longe do zero (bancário daria 1050,10)
    [InlineData("1000.30", "1050.32")] // 1050,315
    [InlineData("0.10", "0.11")]       // 0,105
    public void Arredondamento_meio_exato_vai_para_longe_do_zero(string valor, string esperado)
    {
        var r = Assert.IsType<ReajusteCalculado>(MotorReajuste.Calcular(
            decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture), AniversarioJanela2025,
            Clausulas(TipoIndice.PercentualFixo, fixo: 5m), new Dictionary<DateOnly, decimal>()));

        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), r.ValorNovo);
    }

    [Fact]
    public void Acumulado_nao_e_arredondado_antes_de_multiplicar()
    {
        // Com o acumulado arredondado para 4,26% antes, o resultado seria R$ 2.606,50; o correto (sem arredondar) é R$ 2.606,61.
        Assert.Equal(2606.50m, Arredondamento.Monetario(2500.00m * 1.0426m));
        Assert.Equal(2606.61m, Calcular(Clausulas()).ValorNovo);
    }

    [Fact]
    public void Indice_do_ultimo_mes_ainda_nao_divulgado_deixa_aguardando_e_nunca_calcula_parcial()
    {
        // Hoje (24/10/2026) o SGS tem até set/2026. Aniversário em nov/2026 com defasagem 0 precisa de out/2026.
        var resultado = MotorReajuste.Calcular(2500.00m, D(2026, 11, 10), Clausulas(defasagem: 0), SeriesOficiais.Ipca);

        var aguardando = Assert.IsType<ReajusteAguardandoIndice>(resultado);
        Assert.Equal([D(2026, 10, 1)], aguardando.MesesFaltantes);
        Assert.Equal(new Janela(D(2025, 11, 1), D(2026, 10, 1)), aguardando.Janela);
    }

    [Fact]
    public void Mesmo_aniversario_com_defasagem_1_ja_pode_ser_calculado()
    {
        var r = Assert.IsType<ReajusteCalculado>(MotorReajuste.Calcular(2500.00m, D(2026, 11, 10), Clausulas(), SeriesOficiais.Ipca));

        Assert.Equal(new Janela(D(2025, 10, 1), D(2026, 9, 1)), r.Janela);
        Assert.Equal(2614.40m, r.ValorNovo);
    }

    [Fact]
    public void Historico_retroativo_encadeia_valores_e_para_no_futuro()
    {
        var ciclos = MotorReajuste.CalcularHistorico(2500.00m, D(2024, 2, 10), Clausulas(), SeriesOficiais.Ipca, D(2026, 10, 24));

        Assert.Equal(2, ciclos.Count);
        var primeiro = Assert.IsType<ReajusteCalculado>(ciclos[0].Resultado);
        var segundo = Assert.IsType<ReajusteCalculado>(ciclos[1].Resultado);
        Assert.Equal((1, D(2025, 2, 10)), (ciclos[0].Ciclo, ciclos[0].Aniversario));
        Assert.Equal(2620.78m, primeiro.ValorNovo);   // IPCA 2024
        Assert.Equal(2620.78m, segundo.ValorAntigo);
        Assert.Equal(2732.54m, segundo.ValorNovo);    // IPCA 2025 sobre o valor já reajustado
    }

    [Fact]
    public void Historico_igpm_retroativo_com_ano_negativo()
    {
        var ciclos = MotorReajuste.CalcularHistorico(2500.00m, D(2024, 2, 10), Clausulas(TipoIndice.IgpM), SeriesOficiais.IgpM, D(2026, 10, 24));

        Assert.Equal(2663.40m, Assert.IsType<ReajusteCalculado>(ciclos[0].Resultado).ValorNovo);
        Assert.Equal(2635.64m, Assert.IsType<ReajusteCalculado>(ciclos[1].Resultado).ValorNovo);
    }

    [Fact]
    public void Historico_para_no_primeiro_ciclo_sem_indice()
    {
        var serieIncompleta = SeriesOficiais.Ipca.Where(kv => kv.Key != D(2024, 6, 1)).ToDictionary();

        var ciclos = MotorReajuste.CalcularHistorico(2500.00m, D(2024, 2, 10), Clausulas(), serieIncompleta, D(2026, 10, 24));

        var unico = Assert.Single(ciclos);
        Assert.Equal([D(2024, 6, 1)], Assert.IsType<ReajusteAguardandoIndice>(unico.Resultado).MesesFaltantes);
    }

    [Fact]
    public void Valor_atual_nao_positivo_e_erro_de_programacao() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MotorReajuste.Calcular(0m, AniversarioJanela2025, Clausulas(), SeriesOficiais.Ipca));
}

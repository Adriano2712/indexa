using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Indexa.Dominio;

namespace Indexa.Calculo.Tests;

/// <summary>Invariantes do motor para entradas arbitrárias (valores, variações mensais e cláusulas).</summary>
public sealed class PropriedadesTests
{
    private static readonly DateOnly Aniversario = new(2026, 2, 10);
    private static readonly DateOnly[] MesesDaJanela = Calendario.JanelaDoIndice(Aniversario, 12, 1).Meses().ToArray();

    public sealed record Cenario(decimal Valor, decimal[] Variacoes, decimal? Teto, decimal? Piso, bool NaoReduzir)
    {
        public ClausulasReajuste Clausulas => new(
            TipoIndice.Ipca, null, 12, 1, Teto, Piso, Teto is null && Piso is null ? null : UnidadeLimite.Percentual, NaoReduzir);

        public Dictionary<DateOnly, decimal> Serie => MesesDaJanela.Zip(Variacoes).ToDictionary(p => p.First, p => p.Second);

        public ReajusteCalculado Calcular() => (ReajusteCalculado)MotorReajuste.Calcular(Valor, Aniversario, Clausulas, Serie);
    }

    public static class Geradores
    {
        private static Gen<decimal> Decimal(int min, int max, int casas) =>
            Gen.Choose(min, max).Select(i => i / (decimal)Math.Pow(10, casas));

        public static Arbitrary<Cenario> Cenario()
        {
            var gen =
                from valor in Decimal(1, 1_000_000_000, 2)                      // R$ 0,01 a R$ 10 milhões
                from variacoes in Gen.ArrayOf(Decimal(-300, 300, 2), 12)       // −3,00% a +3,00% ao mês
                from teto in Gen.OneOf(Gen.Constant<decimal?>(null), Decimal(0, 2000, 2).Select(v => (decimal?)v))
                from piso in Gen.OneOf(Gen.Constant<decimal?>(null), Decimal(-1000, 0, 2).Select(v => (decimal?)v))
                from naoReduzir in Gen.Elements(true, false)
                select new Cenario(valor, variacoes, teto, piso, naoReduzir);
            return gen.ToArbitrary();
        }
    }

    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Valor_novo_sempre_tem_no_maximo_duas_casas(Cenario c) =>
        decimal.Round(c.Calcular().ValorNovo, 2) == c.Calcular().ValorNovo;

    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Com_nao_reduzir_o_valor_nunca_cai(Cenario c) =>
        !c.NaoReduzir || c.Calcular().ValorNovo >= c.Valor;

    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Teto_percentual_nunca_e_ultrapassado(Cenario c) =>
        c.Teto is not { } teto || c.Calcular().ValorNovo <= Arredondamento.Monetario(c.Valor * (1 + teto / 100m));

    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Piso_percentual_sempre_e_respeitado(Cenario c) =>
        c.Piso is not { } piso || c.Calcular().ValorNovo >= Arredondamento.Monetario(c.Valor * (1 + piso / 100m));

    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Mesmo_cenario_sempre_da_o_mesmo_resultado(Cenario c) =>
        c.Calcular() is var a && c.Calcular() is var b && a.ValorNovo == b.ValorNovo && a.Regra == b.Regra;

    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Sem_clausulas_e_indice_zero_o_valor_nao_muda(Cenario c) =>
        (c with { Variacoes = new decimal[12], Teto = null, Piso = null }).Calcular().ValorNovo == c.Valor;

    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Indice_maior_nunca_gera_valor_menor(Cenario c)
    {
        var maior = c with { Variacoes = c.Variacoes.Select(v => v + 0.5m).ToArray() };
        return maior.Calcular().ValorNovo >= c.Calcular().ValorNovo;
    }

    // decimal guarda 28-29 dígitos; o produto exato de 12 fatores passa disso, então a ordem pode mexer só no fim da mantissa.
    // O motor sempre multiplica em ordem cronológica (determinístico); aqui garantimos que a diferença é irrelevante.
    [Property(Arbitrary = [typeof(Geradores)], MaxTest = 500)]
    public bool Ordem_dos_meses_so_afeta_digitos_irrelevantes(Cenario c) =>
        decimal.Round(MotorReajuste.Acumular(c.Variacoes), 20) == decimal.Round(MotorReajuste.Acumular(Enumerable.Reverse(c.Variacoes)), 20);
}

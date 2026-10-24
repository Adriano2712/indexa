namespace Indexa.Calculo.Tests;

public sealed class CalendarioTests
{
    private static DateOnly D(int a, int m, int d) => new(a, m, d);

    [Theory]
    [InlineData(1, 2025, 2, 28)]
    [InlineData(2, 2026, 2, 28)]
    [InlineData(4, 2028, 2, 29)]
    public void Data_base_29_de_fevereiro_cai_em_28_nos_anos_nao_bissextos(int ciclo, int ano, int mes, int dia) =>
        Assert.Equal(D(ano, mes, dia), Calendario.Aniversario(D(2024, 2, 29), ciclo, 12));

    [Theory]
    [InlineData(1, 2024, 2, 29)]
    [InlineData(2, 2024, 3, 31)]
    [InlineData(3, 2024, 4, 30)]
    [InlineData(13, 2025, 2, 28)]
    public void Fim_de_mes_e_calculado_a_partir_da_data_base_sem_escorregar(int ciclo, int ano, int mes, int dia) =>
        Assert.Equal(D(ano, mes, dia), Calendario.Aniversario(D(2024, 1, 31), ciclo, 1));

    [Theory]
    [InlineData(0, 2026, 10)]
    [InlineData(1, 2026, 9)]
    [InlineData(2, 2026, 8)]
    public void Janela_de_12_meses_respeita_a_defasagem(int defasagem, int anoFim, int mesFim)
    {
        var janela = Calendario.JanelaDoIndice(D(2026, 11, 10), 12, defasagem);

        Assert.Equal(D(anoFim, mesFim, 1), janela.Fim);
        Assert.Equal(D(anoFim, mesFim, 1).AddMonths(-11), janela.Inicio);
        Assert.Equal(12, janela.Meses().Count());
    }

    [Fact]
    public void Janela_acompanha_a_periodicidade()
    {
        var janela = Calendario.JanelaDoIndice(D(2026, 7, 1), 6, 1);
        Assert.Equal([D(2025, 12, 1), D(2026, 1, 1), D(2026, 2, 1), D(2026, 3, 1), D(2026, 4, 1), D(2026, 5, 1)], janela.Meses());
    }

    [Fact]
    public void Ciclos_ate_hoje_incluem_aniversario_do_proprio_dia()
    {
        var ciclos = Calendario.CiclosAte(D(2024, 10, 24), 12, D(2026, 10, 24));
        Assert.Equal([(1, D(2025, 10, 24)), (2, D(2026, 10, 24))], ciclos);
    }

    [Fact]
    public void Contrato_que_comeca_no_futuro_nao_tem_ciclo_vencido() =>
        Assert.Empty(Calendario.CiclosAte(D(2027, 1, 1), 12, D(2026, 10, 24)));

    [Fact]
    public void Proximo_ciclo_e_o_primeiro_aniversario_a_partir_de_hoje()
    {
        Assert.Equal((3, D(2027, 2, 10)), Calendario.ProximoCiclo(D(2024, 2, 10), 12, D(2026, 10, 24)));
        Assert.Equal((2, D(2026, 10, 24)), Calendario.ProximoCiclo(D(2024, 10, 24), 12, D(2026, 10, 24)));
    }
}

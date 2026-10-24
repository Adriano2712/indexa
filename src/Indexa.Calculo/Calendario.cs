namespace Indexa.Calculo;

public readonly record struct Janela(DateOnly Inicio, DateOnly Fim)
{
    public IEnumerable<DateOnly> Meses()
    {
        for (var mes = Inicio; mes <= Fim; mes = mes.AddMonths(1)) yield return mes;
    }
}

public static class Calendario
{
    public static DateOnly PrimeiroDiaDoMes(DateOnly data) => new(data.Year, data.Month, 1);

    /// <summary>
    /// Calculado sempre a partir da data-base (não encadeado), para que fim de mês não "escorregue":
    /// 31/01 + 1 mês = 29/02, + 2 meses = 31/03. 29/02 em ano não bissexto vira 28/02 (ADR-05).
    /// </summary>
    public static DateOnly Aniversario(DateOnly dataBase, int ciclo, int periodicidadeMeses)
    {
        if (ciclo < 1) throw new ArgumentOutOfRangeException(nameof(ciclo));
        if (periodicidadeMeses < 1) throw new ArgumentOutOfRangeException(nameof(periodicidadeMeses));
        return dataBase.AddMonths(ciclo * periodicidadeMeses);
    }

    /// <summary>Ciclos com aniversário até a data informada, inclusive.</summary>
    public static IReadOnlyList<(int Ciclo, DateOnly Aniversario)> CiclosAte(DateOnly dataBase, int periodicidadeMeses, DateOnly ate)
    {
        var ciclos = new List<(int, DateOnly)>();
        for (var ciclo = 1; ; ciclo++)
        {
            var aniversario = Aniversario(dataBase, ciclo, periodicidadeMeses);
            if (aniversario > ate) return ciclos;
            ciclos.Add((ciclo, aniversario));
        }
    }

    public static (int Ciclo, DateOnly Aniversario) ProximoCiclo(DateOnly dataBase, int periodicidadeMeses, DateOnly hoje)
    {
        for (var ciclo = 1; ; ciclo++)
        {
            var aniversario = Aniversario(dataBase, ciclo, periodicidadeMeses);
            if (aniversario >= hoje) return (ciclo, aniversario);
        }
    }

    /// <summary>
    /// ADR-01: a janela termina <paramref name="defasagemMeses"/> meses antes do mês anterior ao aniversário.
    /// Defasagem 1 e aniversário em novembro: janela termina em setembro (outubro fica de fora, porque o índice
    /// de outubro só sai em novembro). Defasagem 0: janela termina no mês imediatamente anterior ao aniversário.
    /// </summary>
    public static Janela JanelaDoIndice(DateOnly aniversario, int periodicidadeMeses, int defasagemMeses)
    {
        if (periodicidadeMeses < 1) throw new ArgumentOutOfRangeException(nameof(periodicidadeMeses));
        if (defasagemMeses < 0) throw new ArgumentOutOfRangeException(nameof(defasagemMeses));

        var fim = PrimeiroDiaDoMes(aniversario).AddMonths(-(defasagemMeses + 1));
        return new Janela(fim.AddMonths(-(periodicidadeMeses - 1)), fim);
    }
}

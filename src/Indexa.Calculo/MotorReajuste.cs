using Indexa.Dominio;

namespace Indexa.Calculo;

/// <summary>ADR-04: único ponto de arredondamento do sistema.</summary>
public static class Arredondamento
{
    public const int CasasMonetarias = 2;
    public const MidpointRounding Modo = MidpointRounding.AwayFromZero;

    public static decimal Monetario(decimal valor) => decimal.Round(valor, CasasMonetarias, Modo);
}

public sealed record ClausulasReajuste(
    TipoIndice TipoIndice,
    decimal? PercentualFixo,
    int PeriodicidadeMeses,
    int DefasagemMeses,
    decimal? Teto,
    decimal? Piso,
    UnidadeLimite? UnidadeLimite,
    bool NaoReduzir)
{
    public static ClausulasReajuste De(Contrato c) =>
        new(c.TipoIndice, c.PercentualFixo, c.PeriodicidadeMeses, c.DefasagemMeses, c.Teto, c.Piso, c.UnidadeLimite, c.NaoReduzir);
}

public sealed record IndiceUsado(DateOnly Mes, decimal Variacao);

public abstract record ResultadoReajuste;

/// <param name="PercentualAcumulado">Acumulado bruto da janela (ou percentual fixo), em pontos percentuais, sem arredondamento.</param>
/// <param name="PercentualAplicado">Percentual que de fato chegou ao valor, após teto/piso/não redução.</param>
public sealed record ReajusteCalculado(
    decimal ValorAntigo,
    decimal ValorNovo,
    decimal PercentualAcumulado,
    decimal PercentualAplicado,
    RegraAplicada Regra,
    Janela? Janela,
    IReadOnlyList<IndiceUsado> IndicesUsados) : ResultadoReajuste;

public sealed record ReajusteAguardandoIndice(decimal ValorAntigo, Janela Janela, IReadOnlyList<DateOnly> MesesFaltantes) : ResultadoReajuste;

public sealed record CicloCalculado(int Ciclo, DateOnly Aniversario, ResultadoReajuste Resultado);

/// <summary>
/// Motor puro: sem banco, rede ou relógio. Tudo que influencia o resultado chega por parâmetro.
/// Ordem: acumulado → teto/piso → não redução → arredondamento (uma vez, no valor final).
/// </summary>
public static class MotorReajuste
{
    public static ResultadoReajuste Calcular(
        decimal valorAtual, DateOnly aniversario, ClausulasReajuste clausulas, IReadOnlyDictionary<DateOnly, decimal> variacoesMensais)
    {
        if (valorAtual <= 0) throw new ArgumentOutOfRangeException(nameof(valorAtual), "Valor atual deve ser positivo.");

        if (clausulas.TipoIndice == TipoIndice.PercentualFixo)
        {
            var percentual = clausulas.PercentualFixo
                ?? throw new ArgumentException("Contrato de percentual fixo sem percentual.", nameof(clausulas));
            return AplicarClausulas(valorAtual, percentual / 100m, RegraAplicada.PercentualFixo, clausulas, null, []);
        }

        var janela = Calendario.JanelaDoIndice(aniversario, clausulas.PeriodicidadeMeses, clausulas.DefasagemMeses);
        var meses = janela.Meses().ToList();
        var faltantes = meses.Where(m => !variacoesMensais.ContainsKey(m)).ToList();
        if (faltantes.Count > 0) return new ReajusteAguardandoIndice(valorAtual, janela, faltantes);

        var usados = meses.Select(m => new IndiceUsado(m, variacoesMensais[m])).ToList();
        var taxa = Acumular(usados.Select(u => u.Variacao));
        return AplicarClausulas(valorAtual, taxa, RegraAplicada.Indice, clausulas, janela, usados);
    }

    /// <summary>Acumula variações mensais (em %) por composição: Π(1 + v/100) − 1. Resultado como fração, sem arredondar.</summary>
    public static decimal Acumular(IEnumerable<decimal> variacoesPercentuais)
    {
        var fator = 1m;
        foreach (var v in variacoesPercentuais) fator *= 1m + v / 100m;
        return fator - 1m;
    }

    /// <summary>
    /// Recalcula os ciclos vencidos até <paramref name="hoje"/> (inclusive), encadeando o valor de um ciclo no seguinte.
    /// Para no primeiro ciclo sem índice: os seguintes dependem do valor dele.
    /// </summary>
    public static IReadOnlyList<CicloCalculado> CalcularHistorico(
        decimal valorInicial, DateOnly dataBase, ClausulasReajuste clausulas, IReadOnlyDictionary<DateOnly, decimal> variacoesMensais,
        DateOnly hoje)
    {
        var ciclos = new List<CicloCalculado>();
        var valor = valorInicial;
        foreach (var (ciclo, aniversario) in Calendario.CiclosAte(dataBase, clausulas.PeriodicidadeMeses, hoje))
        {
            var resultado = Calcular(valor, aniversario, clausulas, variacoesMensais);
            ciclos.Add(new CicloCalculado(ciclo, aniversario, resultado));
            if (resultado is not ReajusteCalculado calculado) break;
            valor = calculado.ValorNovo;
        }
        return ciclos;
    }

    private static ReajusteCalculado AplicarClausulas(
        decimal valorAtual, decimal taxa, RegraAplicada regra, ClausulasReajuste c, Janela? janela, IReadOnlyList<IndiceUsado> usados)
    {
        decimal valorNovo;
        decimal percentualAplicado;

        if (c.UnidadeLimite == UnidadeLimite.Valor)
        {
            // ADR-02, variante em R$: teto/piso limitam o aumento absoluto.
            (var aumento, regra) = Limitar(valorAtual * taxa, c.Piso, c.Teto, regra);
            valorNovo = Arredondamento.Monetario(valorAtual + aumento);
            percentualAplicado = (valorNovo - valorAtual) / valorAtual * 100m;
        }
        else
        {
            // ADR-02, padrão: teto/piso limitam o percentual acumulado.
            (var taxaAplicada, regra) = Limitar(taxa, c.Piso / 100m, c.Teto / 100m, regra);
            valorNovo = Arredondamento.Monetario(valorAtual * (1m + taxaAplicada));
            percentualAplicado = taxaAplicada * 100m;
        }

        // ADR-03: com a cláusula, nunca reduz; sem ela, a redução do índice é aplicada.
        if (c.NaoReduzir && valorNovo < valorAtual)
        {
            valorNovo = valorAtual;
            percentualAplicado = 0m;
            regra = RegraAplicada.NaoReducao;
        }

        return new ReajusteCalculado(valorAtual, valorNovo, taxa * 100m, percentualAplicado, regra, janela, usados);
    }

    private static (decimal Valor, RegraAplicada Regra) Limitar(decimal valor, decimal? piso, decimal? teto, RegraAplicada regra)
    {
        if (teto is { } t && valor > t) return (t, RegraAplicada.Teto);
        if (piso is { } p && valor < p) return (p, RegraAplicada.Piso);
        return (valor, regra);
    }
}

namespace Indexa.Dominio;

public sealed class ValorIndiceMensal
{
    private ValorIndiceMensal() { }

    public Guid Id { get; private set; }
    public TipoIndice TipoIndice { get; private set; }
    /// <summary>Sempre o dia 1 do mês de referência.</summary>
    public DateOnly MesReferencia { get; private set; }
    /// <summary>Variação mensal em pontos percentuais, como publicada pela fonte (ex.: 0.82).</summary>
    public decimal Valor { get; private set; }
    public string Fonte { get; private set; } = "";
    public DateTimeOffset ColetadoEm { get; private set; }

    public static ValorIndiceMensal Criar(TipoIndice tipo, DateOnly mesReferencia, decimal valor, string fonte, DateTimeOffset agora)
    {
        if (tipo == TipoIndice.PercentualFixo) throw new ArgumentException("Percentual fixo não tem série mensal.", nameof(tipo));
        if (mesReferencia.Day != 1) throw new ArgumentException("Mês de referência deve ser o dia 1.", nameof(mesReferencia));
        if (valor <= -100) throw new ArgumentOutOfRangeException(nameof(valor));
        ArgumentException.ThrowIfNullOrWhiteSpace(fonte);

        return new ValorIndiceMensal
        {
            Id = Guid.NewGuid(),
            TipoIndice = tipo,
            MesReferencia = mesReferencia,
            Valor = valor,
            Fonte = fonte,
            ColetadoEm = agora,
        };
    }

    /// <summary>
    /// A fonte publicou valor diferente do guardado. O valor vigente passa a ser o novo; o anterior fica registrado na divergência.
    /// Reajustes já calculados mantêm sua cópia dos índices usados e nunca são reescritos aqui.
    /// </summary>
    public DivergenciaIndice Revisar(decimal novoValor, string fonte, DateTimeOffset agora)
    {
        if (novoValor == Valor) throw new InvalidOperationException("Revisão sem mudança de valor.");
        if (novoValor <= -100) throw new ArgumentOutOfRangeException(nameof(novoValor));

        var divergencia = DivergenciaIndice.Registrar(TipoIndice, MesReferencia, Valor, novoValor, fonte, agora);
        Valor = novoValor;
        Fonte = fonte;
        ColetadoEm = agora;
        return divergencia;
    }
}

public sealed class DivergenciaIndice
{
    private DivergenciaIndice() { }

    public Guid Id { get; private set; }
    public TipoIndice TipoIndice { get; private set; }
    public DateOnly MesReferencia { get; private set; }
    public decimal ValorAnterior { get; private set; }
    public decimal ValorNovo { get; private set; }
    public string Fonte { get; private set; } = "";
    public DateTimeOffset DetectadaEm { get; private set; }
    /// <summary>Algum reajuste já notificado/aplicado usou o valor antigo: exige revisão humana (não é reescrito).</summary>
    public bool AfetaReajusteEmitido { get; private set; }
    public int ReajustesRecalculados { get; private set; }

    public void RegistrarImpacto(bool afetaReajusteEmitido, int recalculados)
    {
        AfetaReajusteEmitido = afetaReajusteEmitido;
        ReajustesRecalculados = recalculados;
    }

    internal static DivergenciaIndice Registrar(
        TipoIndice tipo, DateOnly mes, decimal anterior, decimal novo, string fonte, DateTimeOffset agora) => new()
        {
            Id = Guid.NewGuid(),
            TipoIndice = tipo,
            MesReferencia = mes,
            ValorAnterior = anterior,
            ValorNovo = novo,
            Fonte = fonte,
            DetectadaEm = agora,
        };
}

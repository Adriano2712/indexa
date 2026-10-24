namespace Indexa.Dominio;

/// <summary>
/// Registro imutável de um ciclo de reajuste. Correção não sobrescreve: gera nova revisão e marca a anterior como Substituido.
/// Os campos de cálculo são preenchidos pelo motor (Etapa 2).
/// </summary>
public sealed class Reajuste
{
    private Reajuste() { }

    public Guid Id { get; private set; }
    public Guid ContratoId { get; private set; }
    public int Ciclo { get; private set; }
    public int Revisao { get; private set; }
    public DateOnly DataAniversario { get; private set; }
    public decimal ValorAntigo { get; private set; }
    public decimal? ValorNovo { get; private set; }
    /// <summary>Acumulado bruto do índice na janela, em pontos percentuais, antes de teto/piso.</summary>
    public decimal? PercentualAcumulado { get; private set; }
    /// <summary>Percentual efetivamente aplicado após teto/piso/não redução.</summary>
    public decimal? PercentualAplicado { get; private set; }
    public DateOnly? JanelaInicio { get; private set; }
    public DateOnly? JanelaFim { get; private set; }
    public RegraAplicada? RegraAplicada { get; private set; }
    public StatusReajuste Status { get; private set; }
    public bool Retroativo { get; private set; }
    public string GeradoPor { get; private set; } = "";
    public string ChaveIdempotencia { get; private set; } = "";
    public string? CaminhoPdf { get; private set; }
    public Guid? SubstituiReajusteId { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public static string GerarChave(Guid contratoId, int ciclo, int revisao) => $"reajuste:{contratoId:N}:{ciclo}:{revisao}";

    public static Reajuste NovoAguardandoIndice(
        Guid contratoId, int ciclo, DateOnly dataAniversario, decimal valorAntigo, bool retroativo, string geradoPor, DateTimeOffset agora)
    {
        if (ciclo < 1) throw new ArgumentOutOfRangeException(nameof(ciclo));
        if (valorAntigo <= 0) throw new ArgumentOutOfRangeException(nameof(valorAntigo));
        ArgumentException.ThrowIfNullOrWhiteSpace(geradoPor);

        return new Reajuste
        {
            Id = Guid.NewGuid(),
            ContratoId = contratoId,
            Ciclo = ciclo,
            Revisao = 0,
            DataAniversario = dataAniversario,
            ValorAntigo = valorAntigo,
            Status = StatusReajuste.AguardandoIndice,
            Retroativo = retroativo,
            GeradoPor = geradoPor,
            ChaveIdempotencia = GerarChave(contratoId, ciclo, 0),
            CriadoEm = agora,
        };
    }

    public Reajuste NovaRevisao(string geradoPor, DateTimeOffset agora)
    {
        if (Status is StatusReajuste.Notificado or StatusReajuste.Aplicado)
            throw new RegraDeNegocioException("Reajuste já notificado ou aplicado não pode ser substituído.");
        if (Status == StatusReajuste.Substituido)
            throw new RegraDeNegocioException("Reajuste já foi substituído.");
        ArgumentException.ThrowIfNullOrWhiteSpace(geradoPor);

        Status = StatusReajuste.Substituido;
        return new Reajuste
        {
            Id = Guid.NewGuid(),
            ContratoId = ContratoId,
            Ciclo = Ciclo,
            Revisao = Revisao + 1,
            DataAniversario = DataAniversario,
            ValorAntigo = ValorAntigo,
            Status = StatusReajuste.AguardandoIndice,
            Retroativo = Retroativo,
            GeradoPor = geradoPor,
            ChaveIdempotencia = GerarChave(ContratoId, Ciclo, Revisao + 1),
            SubstituiReajusteId = Id,
            CriadoEm = agora,
        };
    }
}

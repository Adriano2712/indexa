namespace Indexa.Dominio;

/// <summary>
/// Um ciclo de reajuste. Fluxo: AguardandoIndice → Calculado → (Notificado) → Aplicado.
/// Depois de notificado ou aplicado, os valores ficam imutáveis (garantido também por trigger no banco).
/// Correção nunca sobrescreve: o registro vira Substituido e uma nova revisão é criada.
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
    /// <summary>Cópia das variações mensais usadas, para auditoria mesmo se a fonte revisar o índice depois.</summary>
    public string? IndicesUsadosJson { get; private set; }
    /// <summary>Cópia das cláusulas vigentes no cálculo (aditivos posteriores não mudam o histórico).</summary>
    public string? ClausulasJson { get; private set; }
    public StatusReajuste Status { get; private set; }
    public bool Retroativo { get; private set; }
    public string GeradoPor { get; private set; } = "";
    public string ChaveIdempotencia { get; private set; } = "";
    public string? CaminhoPdf { get; private set; }
    public Guid? SubstituiReajusteId { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? CalculadoEm { get; private set; }
    public DateTimeOffset? NotificadoEm { get; private set; }
    public DateTimeOffset? AplicadoEm { get; private set; }
    public uint Versao { get; private set; }

    public static string GerarChave(Guid contratoId, int ciclo, int revisao) => $"reajuste:{contratoId:N}:{ciclo}:{revisao}";

    public static Reajuste NovoAguardandoIndice(
        Guid contratoId, int ciclo, DateOnly dataAniversario, decimal valorAntigo, bool retroativo, string geradoPor,
        DateTimeOffset agora, int revisao = 0)
    {
        if (ciclo < 1) throw new ArgumentOutOfRangeException(nameof(ciclo));
        if (revisao < 0) throw new ArgumentOutOfRangeException(nameof(revisao));
        if (valorAntigo <= 0) throw new ArgumentOutOfRangeException(nameof(valorAntigo));
        ArgumentException.ThrowIfNullOrWhiteSpace(geradoPor);

        return new Reajuste
        {
            Id = Guid.NewGuid(),
            ContratoId = contratoId,
            Ciclo = ciclo,
            Revisao = revisao,
            DataAniversario = dataAniversario,
            ValorAntigo = valorAntigo,
            Status = StatusReajuste.AguardandoIndice,
            Retroativo = retroativo,
            GeradoPor = geradoPor,
            ChaveIdempotencia = GerarChave(contratoId, ciclo, revisao),
            CriadoEm = agora,
        };
    }

    public void RegistrarCalculo(
        decimal valorNovo, decimal percentualAcumulado, decimal percentualAplicado, DateOnly? janelaInicio, DateOnly? janelaFim,
        RegraAplicada regra, string indicesUsadosJson, string clausulasJson, DateTimeOffset agora)
    {
        if (Status != StatusReajuste.AguardandoIndice)
            throw new RegraDeNegocioException($"Reajuste em {Status} não pode receber novo cálculo.");
        if (valorNovo <= 0) throw new ArgumentOutOfRangeException(nameof(valorNovo));

        ValorNovo = valorNovo;
        PercentualAcumulado = percentualAcumulado;
        PercentualAplicado = percentualAplicado;
        JanelaInicio = janelaInicio;
        JanelaFim = janelaFim;
        RegraAplicada = regra;
        IndicesUsadosJson = indicesUsadosJson;
        ClausulasJson = clausulasJson;
        Status = StatusReajuste.Calculado;
        CalculadoEm = agora;
    }

    public void RegistrarPdf(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        if (ValorNovo is null) throw new RegraDeNegocioException("Reajuste sem cálculo não tem documento.");
        if (CaminhoPdf is not null && CaminhoPdf != caminho) throw new RegraDeNegocioException("O documento do reajuste já foi gerado.");
        CaminhoPdf = caminho;
    }

    public void MarcarNotificado(DateTimeOffset agora)
    {
        if (Status is not (StatusReajuste.Calculado or StatusReajuste.Notificado or StatusReajuste.Aplicado))
            throw new RegraDeNegocioException($"Reajuste em {Status} não pode ser notificado.");
        NotificadoEm ??= agora;
        if (Status == StatusReajuste.Calculado) Status = StatusReajuste.Notificado;
    }

    public void Aplicar(DateTimeOffset agora)
    {
        if (Status is not (StatusReajuste.Calculado or StatusReajuste.Notificado))
            throw new RegraDeNegocioException($"Reajuste em {Status} não pode ser aplicado.");
        Status = StatusReajuste.Aplicado;
        AplicadoEm = agora;
    }

    /// <summary>Pode ser substituído se nunca saiu do sistema: não notificado e (não aplicado ou retroativo).</summary>
    public bool PodeSerSubstituido =>
        Status != StatusReajuste.Substituido && NotificadoEm is null &&
        (Status != StatusReajuste.Aplicado || Retroativo) && Status != StatusReajuste.Notificado;

    public void Substituir()
    {
        if (!PodeSerSubstituido)
            throw new RegraDeNegocioException("Reajuste já notificado ou aplicado não pode ser substituído.");
        Status = StatusReajuste.Substituido;
    }

    /// <summary>Substitui este registro por uma revisão nova, que será recalculada (ex.: índice revisado pela fonte).</summary>
    public Reajuste NovaRevisao(string geradoPor, DateTimeOffset agora)
    {
        if (Status == StatusReajuste.Aplicado)
            throw new RegraDeNegocioException("Reajuste aplicado não gera revisão; corrija pelo contrato.");
        Substituir();
        var revisao = NovoAguardandoIndice(ContratoId, Ciclo, DataAniversario, ValorAntigo, Retroativo, geradoPor, agora, Revisao + 1);
        revisao.SubstituiReajusteId = Id;
        return revisao;
    }
}

namespace Indexa.Dominio;

public sealed record DadosContrato(
    Guid ClienteId,
    string Descricao,
    decimal ValorInicial,
    DateOnly DataBase,
    TipoIndice TipoIndice,
    decimal? PercentualFixo,
    int PeriodicidadeMeses,
    int DefasagemMeses,
    decimal? Teto,
    decimal? Piso,
    UnidadeLimite? UnidadeLimite,
    bool NaoReduzir);

public sealed class Contrato
{
    public const int TamanhoMaximoDescricao = 200;
    public const decimal ValorMaximo = 999_999_999_999.99m;
    public const int PeriodicidadePadrao = 12;
    public const int DefasagemPadrao = 1;
    public static readonly DateOnly DataBaseMinima = new(1994, 7, 1);

    private Contrato() { }

    public Guid Id { get; private set; }
    public Guid ClienteId { get; private set; }
    public Cliente? Cliente { get; private set; }
    public string Descricao { get; private set; } = "";
    public decimal ValorInicial { get; private set; }
    public decimal ValorAtual { get; private set; }
    public DateOnly DataBase { get; private set; }
    public TipoIndice TipoIndice { get; private set; }
    /// <summary>Em pontos percentuais (10 = 10%).</summary>
    public decimal? PercentualFixo { get; private set; }
    public int PeriodicidadeMeses { get; private set; }
    public int DefasagemMeses { get; private set; }
    /// <summary>Pontos percentuais se UnidadeLimite = Percentual; R$ de aumento se UnidadeLimite = Valor (ADR-02).</summary>
    public decimal? Teto { get; private set; }
    public decimal? Piso { get; private set; }
    public UnidadeLimite? UnidadeLimite { get; private set; }
    public bool NaoReduzir { get; private set; }
    public StatusContrato Status { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    public static Contrato Criar(DadosContrato dados, DateTimeOffset agora)
    {
        Validar(dados);
        var contrato = new Contrato { Id = Guid.NewGuid(), Status = StatusContrato.Ativo, CriadoEm = agora };
        contrato.Aplicar(dados, agora);
        contrato.ValorAtual = dados.ValorInicial;
        return contrato;
    }

    // Campos que definem os ciclos e o valor de partida: mudá-los depois de existir reajuste invalidaria o histórico.
    public IReadOnlyList<string> CamposDeBaseAlterados(DadosContrato dados)
    {
        var campos = new List<string>();
        if (dados.DataBase != DataBase) campos.Add("dataBase");
        if (dados.ValorInicial != ValorInicial) campos.Add("valorInicial");
        if (dados.PeriodicidadeMeses != PeriodicidadeMeses) campos.Add("periodicidadeMeses");
        return campos;
    }

    public void Atualizar(DadosContrato dados, bool possuiReajustes, DateTimeOffset agora)
    {
        if (Status == StatusContrato.Encerrado)
            throw new RegraDeNegocioException("Contrato encerrado não pode ser alterado.");

        Validar(dados);

        if (possuiReajustes)
        {
            var bloqueados = CamposDeBaseAlterados(dados);
            if (bloqueados.Count > 0)
                throw new RegraDeNegocioException(
                    $"O contrato já possui reajustes; não é possível alterar: {string.Join(", ", bloqueados)}.");
        }
        else
        {
            ValorAtual = dados.ValorInicial;
        }

        Aplicar(dados, agora);
    }

    public void Encerrar(DateTimeOffset agora)
    {
        if (Status == StatusContrato.Encerrado) return;
        Status = StatusContrato.Encerrado;
        AtualizadoEm = agora;
    }

    private void Aplicar(DadosContrato d, DateTimeOffset agora)
    {
        ClienteId = d.ClienteId;
        Descricao = d.Descricao.Trim();
        ValorInicial = d.ValorInicial;
        DataBase = d.DataBase;
        TipoIndice = d.TipoIndice;
        PercentualFixo = d.PercentualFixo;
        PeriodicidadeMeses = d.PeriodicidadeMeses;
        DefasagemMeses = d.DefasagemMeses;
        Teto = d.Teto;
        Piso = d.Piso;
        UnidadeLimite = d.UnidadeLimite;
        NaoReduzir = d.NaoReduzir;
        AtualizadoEm = agora;
    }

    private static void Validar(DadosContrato d)
    {
        var e = new ColetorErros();
        var descricao = d.Descricao?.Trim();

        e.Se(d.ClienteId == Guid.Empty, "clienteId", "Cliente é obrigatório.");
        e.Se(string.IsNullOrEmpty(descricao) || descricao.Length > TamanhoMaximoDescricao,
            "descricao", $"Descrição é obrigatória (até {TamanhoMaximoDescricao} caracteres).");
        e.Se(d.ValorInicial <= 0 || d.ValorInicial > ValorMaximo, "valorInicial", "Valor deve ser maior que zero.");
        e.Se(!Validacao.TemNoMaximoCasas(d.ValorInicial, 2), "valorInicial", "Use no máximo 2 casas decimais.");
        e.Se(d.DataBase < DataBaseMinima, "dataBase", $"Data-base deve ser a partir de {DataBaseMinima:dd/MM/yyyy}.");
        e.Se(!Enum.IsDefined(d.TipoIndice), "tipoIndice", "Tipo de índice inválido.");

        if (d.TipoIndice == TipoIndice.PercentualFixo)
        {
            e.Se(d.PercentualFixo is null, "percentualFixo", "Obrigatório para contratos com percentual fixo.");
            e.Se(d.PercentualFixo is <= 0 or > 100, "percentualFixo", "Deve estar entre 0 (exclusivo) e 100.");
            e.Se(d.PercentualFixo is { } p && !Validacao.TemNoMaximoCasas(p, 4), "percentualFixo", "Use no máximo 4 casas decimais.");
        }
        else
        {
            e.Se(d.PercentualFixo is not null, "percentualFixo", "Só se aplica a contratos com percentual fixo.");
        }

        e.Se(d.PeriodicidadeMeses is < 1 or > 120, "periodicidadeMeses", "Periodicidade deve estar entre 1 e 120 meses.");
        e.Se(d.DefasagemMeses is < 0 or > 6, "defasagemMeses", "Defasagem deve estar entre 0 e 6 meses.");

        var temLimite = d.Teto is not null || d.Piso is not null;
        e.Se(temLimite && d.UnidadeLimite is null, "unidadeLimite", "Informe se teto/piso são percentuais ou em valor.");
        e.Se(!temLimite && d.UnidadeLimite is not null, "unidadeLimite", "Unidade informada sem teto nem piso.");
        e.Se(d.UnidadeLimite is { } u && !Enum.IsDefined(u), "unidadeLimite", "Unidade inválida.");
        ValidarLimite(e, "teto", d.Teto, d.UnidadeLimite);
        ValidarLimite(e, "piso", d.Piso, d.UnidadeLimite);
        e.Se(d.Teto is { } teto && d.Piso is { } piso && piso > teto, "piso", "Piso não pode ser maior que o teto.");

        e.LancarSeHouver();
    }

    private static void ValidarLimite(ColetorErros e, string campo, decimal? limite, UnidadeLimite? unidade)
    {
        if (limite is not { } v || unidade is null) return;

        if (unidade == Dominio.UnidadeLimite.Percentual)
        {
            e.Se(v <= -100 || v > 1000, campo, "Percentual deve estar entre -100 (exclusivo) e 1000.");
            e.Se(!Validacao.TemNoMaximoCasas(v, 4), campo, "Use no máximo 4 casas decimais.");
        }
        else
        {
            e.Se(v < 0 || v > ValorMaximo, campo, "Valor deve ser maior ou igual a zero.");
            e.Se(!Validacao.TemNoMaximoCasas(v, 2), campo, "Use no máximo 2 casas decimais.");
        }
    }
}

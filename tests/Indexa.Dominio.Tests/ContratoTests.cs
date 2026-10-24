using Indexa.Dominio;

namespace Indexa.Dominio.Tests;

public sealed class ContratoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static DadosContrato Valido() => new(
        ClienteId: Guid.NewGuid(), Descricao: "Aluguel sala 101", ValorInicial: 2500.00m, DataBase: new DateOnly(2025, 3, 10),
        TipoIndice: TipoIndice.Ipca, PercentualFixo: null, PeriodicidadeMeses: 12, DefasagemMeses: 1,
        Teto: null, Piso: null, UnidadeLimite: null, NaoReduzir: false);

    private static string[] CamposComErro(DadosContrato dados) =>
        Assert.Throws<DadosInvalidosException>(() => Contrato.Criar(dados, Agora)).Erros.Keys.Order().ToArray();

    [Fact]
    public void Criar_contrato_valido_inicia_ativo_com_valor_atual_igual_ao_inicial()
    {
        var contrato = Contrato.Criar(Valido(), Agora);

        Assert.Equal(StatusContrato.Ativo, contrato.Status);
        Assert.Equal(2500.00m, contrato.ValorAtual);
        Assert.Equal(Agora, contrato.CriadoEm);
    }

    public static TheoryData<string, DadosContrato> CasosInvalidos => new()
    {
        { "clienteId", Valido() with { ClienteId = Guid.Empty } },
        { "descricao", Valido() with { Descricao = "   " } },
        { "descricao", Valido() with { Descricao = new string('x', 201) } },
        { "valorInicial", Valido() with { ValorInicial = 0m } },
        { "valorInicial", Valido() with { ValorInicial = 100.001m } },
        { "dataBase", Valido() with { DataBase = new DateOnly(1994, 6, 30) } },
        { "tipoIndice", Valido() with { TipoIndice = (TipoIndice)99 } },
        { "percentualFixo", Valido() with { TipoIndice = TipoIndice.PercentualFixo } },
        { "percentualFixo", Valido() with { TipoIndice = TipoIndice.PercentualFixo, PercentualFixo = 0m } },
        { "percentualFixo", Valido() with { TipoIndice = TipoIndice.PercentualFixo, PercentualFixo = 5.12345m } },
        { "percentualFixo", Valido() with { PercentualFixo = 5m } },
        { "periodicidadeMeses", Valido() with { PeriodicidadeMeses = 0 } },
        { "defasagemMeses", Valido() with { DefasagemMeses = 7 } },
        { "unidadeLimite", Valido() with { Teto = 10m } },
        { "unidadeLimite", Valido() with { UnidadeLimite = UnidadeLimite.Percentual } },
        { "piso", Valido() with { Teto = 5m, Piso = 6m, UnidadeLimite = UnidadeLimite.Percentual } },
        { "teto", Valido() with { Teto = -100m, UnidadeLimite = UnidadeLimite.Percentual } },
        { "teto", Valido() with { Teto = 10.123m, UnidadeLimite = UnidadeLimite.Valor } },
    };

    [Theory]
    [MemberData(nameof(CasosInvalidos))]
    public void Criar_contrato_invalido_aponta_o_campo(string campoEsperado, DadosContrato dados) =>
        Assert.Contains(campoEsperado, CamposComErro(dados));

    [Fact]
    public void Piso_negativo_percentual_e_aceito()
    {
        var contrato = Contrato.Criar(Valido() with { Piso = -5m, Teto = 10m, UnidadeLimite = UnidadeLimite.Percentual }, Agora);
        Assert.Equal(-5m, contrato.Piso);
    }

    [Fact]
    public void Atualizar_sem_reajustes_permite_mudar_base_e_ressincroniza_valor_atual()
    {
        var contrato = Contrato.Criar(Valido(), Agora);

        contrato.Atualizar(Valido() with { ClienteId = contrato.ClienteId, ValorInicial = 3000m, DataBase = new DateOnly(2025, 4, 1) }, false, Agora);

        Assert.Equal(3000m, contrato.ValorAtual);
        Assert.Equal(new DateOnly(2025, 4, 1), contrato.DataBase);
    }

    [Fact]
    public void Atualizar_com_reajustes_bloqueia_campos_de_base()
    {
        var dados = Valido();
        var contrato = Contrato.Criar(dados, Agora);

        var ex = Assert.Throws<RegraDeNegocioException>(() =>
            contrato.Atualizar(dados with { DataBase = new DateOnly(2025, 4, 1), PeriodicidadeMeses = 6 }, true, Agora));

        Assert.Contains("dataBase", ex.Message);
        Assert.Contains("periodicidadeMeses", ex.Message);
    }

    [Fact]
    public void Atualizar_com_reajustes_permite_mudar_clausulas_futuras_sem_mexer_no_valor_atual()
    {
        var dados = Valido();
        var contrato = Contrato.Criar(dados, Agora);

        contrato.Atualizar(dados with { Teto = 8m, UnidadeLimite = UnidadeLimite.Percentual, NaoReduzir = true }, true, Agora);

        Assert.Equal(8m, contrato.Teto);
        Assert.True(contrato.NaoReduzir);
        Assert.Equal(2500.00m, contrato.ValorAtual);
    }

    [Fact]
    public void Contrato_encerrado_nao_pode_ser_alterado()
    {
        var dados = Valido();
        var contrato = Contrato.Criar(dados, Agora);
        contrato.Encerrar(Agora);

        Assert.Throws<RegraDeNegocioException>(() => contrato.Atualizar(dados, false, Agora));
    }
}

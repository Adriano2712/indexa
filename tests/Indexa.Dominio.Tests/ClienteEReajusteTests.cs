using Indexa.Dominio;

namespace Indexa.Dominio.Tests;

public sealed class ClienteTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("123.456.789-09", "12345678909")]
    [InlineData("12.345.678/0001-95", "12345678000195")]
    [InlineData("12.abc.345/01de-35", "12ABC34501DE35")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void Documento_e_normalizado(string? entrada, string? esperado) =>
        Assert.Equal(esperado, Cliente.Criar(Guid.NewGuid(), "Fulano", entrada, null, Agora).Documento);

    [Theory]
    [InlineData("123")]
    [InlineData("1234567890A")]
    [InlineData("12ABC34501DEXY")]
    public void Documento_invalido_e_rejeitado(string documento)
    {
        var ex = Assert.Throws<DadosInvalidosException>(() => Cliente.Criar(Guid.NewGuid(), "Fulano", documento, null, Agora));
        Assert.Contains("documento", ex.Erros.Keys);
    }

    [Fact]
    public void Email_e_normalizado_para_minusculas()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Fulano", null, "  Contato@Exemplo.COM ", Agora);
        Assert.Equal("contato@exemplo.com", cliente.Email);
    }
}

public sealed class ReajusteTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nova_revisao_substitui_a_anterior_sem_alterar_seus_valores()
    {
        var contratoId = Guid.NewGuid();
        var original = Reajuste.NovoAguardandoIndice(contratoId, 1, new DateOnly(2026, 3, 10), 2500m, false, "job", Agora);

        var revisao = original.NovaRevisao("usuario:teste", Agora);

        Assert.Equal(StatusReajuste.Substituido, original.Status);
        Assert.Equal(2500m, original.ValorAntigo);
        Assert.Equal(1, revisao.Revisao);
        Assert.Equal(original.Id, revisao.SubstituiReajusteId);
        Assert.NotEqual(original.ChaveIdempotencia, revisao.ChaveIdempotencia);
        Assert.Equal(StatusReajuste.AguardandoIndice, revisao.Status);
    }

    [Fact]
    public void Reajuste_ja_substituido_nao_gera_outra_revisao()
    {
        var original = Reajuste.NovoAguardandoIndice(Guid.NewGuid(), 1, new DateOnly(2026, 3, 10), 2500m, false, "job", Agora);
        original.NovaRevisao("job", Agora);

        Assert.Throws<RegraDeNegocioException>(() => original.NovaRevisao("job", Agora));
    }

    [Fact]
    public void Chave_de_idempotencia_e_deterministica()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("reajuste:11111111222233334444555555555555:3:0", Reajuste.GerarChave(id, 3, 0));
    }
}

using Indexa.Dominio;

namespace Indexa.Dominio.Tests;

public sealed class CicloDeVidaReajusteTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);

    private static Reajuste Novo(bool retroativo = false) =>
        Reajuste.NovoAguardandoIndice(Guid.NewGuid(), 1, new DateOnly(2026, 11, 5), 2500m, retroativo, "teste", Agora);

    private static Reajuste Calculado(bool retroativo = false)
    {
        var r = Novo(retroativo);
        r.RegistrarCalculo(2614.40m, 4.576m, 4.576m, new DateOnly(2025, 10, 1), new DateOnly(2026, 9, 1), RegraAplicada.Indice, "[]", "{}", Agora);
        return r;
    }

    [Fact]
    public void Calculo_so_e_registrado_uma_vez()
    {
        var r = Calculado();
        Assert.Equal(StatusReajuste.Calculado, r.Status);
        Assert.Throws<RegraDeNegocioException>(() =>
            r.RegistrarCalculo(1m, 0m, 0m, null, null, RegraAplicada.Indice, "[]", "{}", Agora));
    }

    [Fact]
    public void Nao_aplica_sem_calculo() => Assert.Throws<RegraDeNegocioException>(() => Novo().Aplicar(Agora));

    [Fact]
    public void Notificacao_atrasada_depois_de_aplicado_mantem_aplicado_e_registra_data()
    {
        var r = Calculado();
        r.Aplicar(Agora);
        r.MarcarNotificado(Agora.AddHours(1));

        Assert.Equal(StatusReajuste.Aplicado, r.Status);
        Assert.Equal(Agora.AddHours(1), r.NotificadoEm);
    }

    [Fact]
    public void Notificado_nao_pode_ser_substituido()
    {
        var r = Calculado();
        r.MarcarNotificado(Agora);
        Assert.False(r.PodeSerSubstituido);
        Assert.Throws<RegraDeNegocioException>(r.Substituir);
        Assert.Throws<RegraDeNegocioException>(() => r.NovaRevisao("x", Agora));
    }

    [Fact]
    public void Aplicado_so_e_substituido_se_retroativo()
    {
        var comum = Calculado();
        comum.Aplicar(Agora);
        var retro = Calculado(retroativo: true);
        retro.Aplicar(Agora);

        Assert.False(comum.PodeSerSubstituido);
        Assert.True(retro.PodeSerSubstituido);
        retro.Substituir();
        Assert.Equal(StatusReajuste.Substituido, retro.Status);
    }

    [Fact]
    public void Pdf_nao_troca_depois_de_gerado()
    {
        var r = Calculado();
        r.RegistrarPdf("a.pdf");
        r.RegistrarPdf("a.pdf");
        Assert.Throws<RegraDeNegocioException>(() => r.RegistrarPdf("b.pdf"));
        Assert.Throws<RegraDeNegocioException>(() => Novo().RegistrarPdf("a.pdf"));
    }

    [Fact]
    public void Revisao_nova_tem_chave_propria_e_aponta_a_anterior()
    {
        var r = Calculado();
        var revisao = r.NovaRevisao("job", Agora);

        Assert.Equal(StatusReajuste.Substituido, r.Status);
        Assert.Equal((1, r.Id, StatusReajuste.AguardandoIndice), (revisao.Revisao, revisao.SubstituiReajusteId, revisao.Status));
        Assert.NotEqual(r.ChaveIdempotencia, revisao.ChaveIdempotencia);
    }
}

public sealed class NotificacaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);

    private static Notificacao Nova() => Notificacao.Pendente(Guid.NewGuid(), CanalNotificacao.Email, "a@b.com", Agora);

    [Fact]
    public void Reserva_impede_segundo_envio_ate_expirar()
    {
        var n = Nova();

        Assert.True(n.TentarIniciarEnvio(Agora));
        Assert.False(n.TentarIniciarEnvio(Agora.AddMinutes(1)));
        Assert.True(n.TentarIniciarEnvio(Agora + Notificacao.ExpiracaoEnvio + TimeSpan.FromSeconds(1)));
        Assert.Equal(2, n.Tentativas);
    }

    [Fact]
    public void Enviada_nunca_volta_a_ser_enviada()
    {
        var n = Nova();
        n.TentarIniciarEnvio(Agora);
        n.RegistrarSucesso(Agora);

        Assert.False(n.TentarIniciarEnvio(Agora.AddDays(1)));
        Assert.Throws<RegraDeNegocioException>(n.Cancelar);
    }

    [Fact]
    public void Esgota_tentativas_e_termina_em_falhou()
    {
        var n = Nova();
        for (var i = 1; i <= Notificacao.MaximoTentativas; i++)
        {
            Assert.True(n.TentarIniciarEnvio(Agora));
            n.RegistrarFalha(new string('x', 3000));
            Assert.Equal(i < Notificacao.MaximoTentativas ? StatusNotificacao.Pendente : StatusNotificacao.Falhou, n.Status);
        }

        Assert.Equal(2000, n.UltimoErro!.Length);
        Assert.False(n.TentarIniciarEnvio(Agora));
    }
}

public sealed class PreferenciasEContratoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Aviso_vai_para_email_de_login_ate_ser_configurado()
    {
        var u = Usuario.Criar("Ana", "ana@exemplo.com", "senha-longa-123", Agora);
        Assert.Equal(("ana@exemplo.com", 15), (u.DestinatarioAviso, u.AntecedenciaAvisoDias));

        u.AtualizarPreferencias(30, " Financeiro@Exemplo.com ", true);
        Assert.Equal(("financeiro@exemplo.com", 30), (u.DestinatarioAviso, u.AntecedenciaAvisoDias));
    }

    [Theory]
    [InlineData(0, null, "antecedenciaAvisoDias")]
    [InlineData(91, null, "antecedenciaAvisoDias")]
    [InlineData(10, "nao-e-email", "emailAviso")]
    public void Preferencias_invalidas_sao_rejeitadas(int dias, string? email, string campo)
    {
        var u = Usuario.Criar("Ana", "ana@exemplo.com", "senha-longa-123", Agora);
        Assert.Contains(campo, Assert.Throws<DadosInvalidosException>(() => u.AtualizarPreferencias(dias, email, true)).Erros.Keys);
    }

    [Fact]
    public void Reajuste_so_e_aplicado_ao_contrato_em_ordem()
    {
        var c = Contrato.Criar(new DadosContrato(Guid.NewGuid(), "X", 1000m, new DateOnly(2024, 1, 1), TipoIndice.Ipca, null, 12, 1, null, null, null, false), Agora);

        Assert.Throws<RegraDeNegocioException>(() => c.AplicarReajuste(2, 1100m, Agora));
        c.AplicarReajuste(1, 1050m, Agora);
        Assert.Equal((1050m, 1), (c.ValorAtual, c.UltimoCicloAplicado));
    }

    [Fact]
    public void Corrigir_base_zera_o_historico_do_contrato()
    {
        var dados = new DadosContrato(Guid.NewGuid(), "X", 1000m, new DateOnly(2024, 1, 1), TipoIndice.Ipca, null, 12, 1, null, null, null, false);
        var c = Contrato.Criar(dados, Agora);
        c.AplicarReajuste(1, 1050m, Agora);

        Assert.True(c.Atualizar(dados with { DataBase = new DateOnly(2024, 2, 1) }, possuiReajustesComunicados: false, Agora));
        Assert.Equal((1000m, 0), (c.ValorAtual, c.UltimoCicloAplicado));
        Assert.False(c.Atualizar(dados with { DataBase = new DateOnly(2024, 2, 1), NaoReduzir = true }, false, Agora));
    }
}

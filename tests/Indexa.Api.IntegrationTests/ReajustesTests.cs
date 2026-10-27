using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Indexa.Api.Avisos;
using Indexa.Api.Contratos;
using Indexa.Api.Indices;
using Indexa.Api.Reajustes;
using Indexa.Calculo;
using Indexa.Dominio;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Indexa.Api.IntegrationTests;

/// <summary>
/// Ciclo de vida completo com índices oficiais. Relógio: 24/10/2026, 12h em Brasília; SGS publicado até set/2026.
/// Valores esperados conferidos no Python (decimal) a partir das séries 433/189.
/// </summary>
[Collection("reajustes")]
public sealed class ReajustesTests(ApiReajustesFactory api) : IAsyncLifetime
{
    private static DateOnly D(int a, int m, int d) => new(a, m, d);

    public async Task InitializeAsync()
    {
        api.Relogio.Agora = ApiReajustesFactory.Hoje;
        api.Fonte.Restaurar();
        await ColetarAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- auxiliares ----------

    private Task ColetarAsync() =>
        api.ComServicoAsync<ServicoColetaIndices, IReadOnlyList<ResultadoColeta>>(s => s.ColetarTodosAsync(CancellationToken.None));

    private Task ProcessarAsync(Guid contratoId) =>
        api.ComServicoAsync<ServicoReajustes, bool>(async s => { await s.ProcessarContratoAsync(contratoId, "teste", CancellationToken.None); return true; });

    private Task<ResultadoEnvio> EnviarAsync(Guid notificacaoId) =>
        api.ComServicoAsync<ServicoEnvioAvisos, ResultadoEnvio>(s => s.EnviarAsync(notificacaoId, CancellationToken.None));

    private async Task<(HttpClient Http, Guid ClienteId, string Email)> NovoUsuarioAsync()
    {
        var email = Auxiliares.NovoEmail();
        var http = await api.CriarClienteAutenticadoAsync(email);
        return (http, await http.CriarClienteAsync("Condomínio Ipê"), email);
    }

    private static Task<ContratoEndpoints.ContratoResponse> CriarAsync(HttpClient http, Guid clienteId, string dataBase, Action<Dictionary<string, object?>>? ajuste = null)
    {
        var corpo = Auxiliares.ContratoIpca(clienteId, "Sala 12", dataBase);
        ajuste?.Invoke(corpo);
        return http.CriarContratoAsync(corpo);
    }

    private async Task<List<Reajuste>> ReajustesAsync(Guid contratoId)
    {
        List<Reajuste> lista = [];
        await api.ComBancoAsync(async db => lista = await db.Reajustes.AsNoTracking()
            .Where(r => r.ContratoId == contratoId).OrderBy(r => r.Ciclo).ThenBy(r => r.Revisao).ToListAsync());
        return lista;
    }

    private async Task<List<Notificacao>> NotificacoesAsync(Guid contratoId)
    {
        List<Notificacao> lista = [];
        await api.ComBancoAsync(async db => lista = await db.Notificacoes.AsNoTracking()
            .Where(n => db.Reajustes.Any(r => r.Id == n.ReajusteId && r.ContratoId == contratoId)).OrderBy(n => n.CriadoEm).ToListAsync());
        return lista;
    }

    private async Task<Contrato> ContratoAsync(Guid id)
    {
        Contrato? c = null;
        await api.ComBancoAsync(async db => c = await db.Contratos.AsNoTracking().SingleAsync(x => x.Id == id));
        return c!;
    }

    private async Task<JsonElement[]> EmailsParaAsync(string email)
    {
        using var mailpit = api.Mailpit();
        var busca = await mailpit.GetFromJsonAsync<JsonElement>($"api/v1/search?query={Uri.EscapeDataString($"to:{email}")}");
        return busca.GetProperty("messages").EnumerateArray().ToArray();
    }

    // ---------- cenários ----------

    [Fact]
    public async Task Cadastro_de_contrato_antigo_gera_retroativos_aplicados_sem_aviso()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();

        var contrato = await CriarAsync(http, cliente, "2024-02-10");

        Assert.Equal(2732.54m, contrato.ValorAtual);   // IPCA 2024 e depois IPCA 2025, encadeados
        Assert.Equal(2, contrato.UltimoCicloAplicado);
        var reajustes = await ReajustesAsync(contrato.Id);
        Assert.Equal([2620.78m, 2732.54m], reajustes.Select(r => r.ValorNovo!.Value));
        Assert.All(reajustes, r =>
        {
            Assert.Equal(StatusReajuste.Aplicado, r.Status);
            Assert.True(r.Retroativo);
            Assert.Equal(12, ServicoReajustes.LerVariacoes(r.IndicesUsadosJson).Count);
            Assert.NotNull(r.ClausulasJson);
        });
        Assert.Empty(await NotificacoesAsync(contrato.Id));
    }

    [Fact]
    public async Task Fluxo_completo_aviso_com_pdf_e_aplicacao_na_data()
    {
        var (http, cliente, email) = await NovoUsuarioAsync();

        // Aniversário em 05/11/2026 (12 dias): dentro da antecedência padrão de 15 dias. Janela out/2025–set/2026.
        var contrato = await CriarAsync(http, cliente, "2025-11-05");

        var reajuste = Assert.Single(await ReajustesAsync(contrato.Id));
        Assert.Equal((StatusReajuste.Calculado, false, 2614.40m), (reajuste.Status, reajuste.Retroativo, reajuste.ValorNovo));
        Assert.Equal((D(2025, 10, 1), D(2026, 9, 1)), (reajuste.JanelaInicio!.Value, reajuste.JanelaFim!.Value));
        Assert.Equal(2500.00m, contrato.ValorAtual);   // ainda não chegou a data
        var aviso = Assert.Single(await NotificacoesAsync(contrato.Id));
        Assert.Equal((StatusNotificacao.Pendente, email), (aviso.Status, aviso.Destinatario));
        Assert.Contains(aviso.Id, api.Fila.EnviosAviso);

        Assert.Equal(ResultadoEnvio.Enviado, await EnviarAsync(aviso.Id));
        Assert.Equal(ResultadoEnvio.IgnoradoJaTratado, await EnviarAsync(aviso.Id));

        var emails = await EmailsParaAsync(email);
        var mensagem = Assert.Single(emails);
        Assert.Contains("2.614,40", mensagem.GetProperty("Subject").GetString());
        Assert.Equal(1, mensagem.GetProperty("Attachments").GetInt32());

        reajuste = Assert.Single(await ReajustesAsync(contrato.Id));
        Assert.Equal(StatusReajuste.Notificado, reajuste.Status);
        var pdfGuardado = await File.ReadAllBytesAsync(Path.Combine(api.PastaDocumentos, reajuste.CaminhoPdf!));
        Assert.Equal("%PDF"u8.ToArray(), pdfGuardado[..4]);

        var download = await http.GetAsync($"/api/reajustes/{reajuste.Id}/carta.pdf");
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(pdfGuardado, await download.Content.ReadAsByteArrayAsync());

        // Chega o aniversário: aplica uma vez só, mesmo processando duas vezes.
        api.Relogio.Agora = new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.Zero);
        await ProcessarAsync(contrato.Id);
        await ProcessarAsync(contrato.Id);

        reajuste = Assert.Single(await ReajustesAsync(contrato.Id));
        Assert.Equal(StatusReajuste.Aplicado, reajuste.Status);
        Assert.NotNull(reajuste.NotificadoEm);
        var atualizado = await ContratoAsync(contrato.Id);
        Assert.Equal((2614.40m, 1), (atualizado.ValorAtual, atualizado.UltimoCicloAplicado));
        Assert.Single(await NotificacoesAsync(contrato.Id));
    }

    [Fact]
    public async Task Aniversario_alem_da_antecedencia_espera_e_antecedencia_maior_antecipa()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2025-12-20");   // aniversário em 20/12/2026: 57 dias

        Assert.Empty(await ReajustesAsync(contrato.Id));

        var config = await http.PutAsJsonAsync("/api/configuracoes", new { antecedenciaAvisoDias = 60, emailAviso = (string?)null, avisoEmailAtivo = true });
        config.EnsureSuccessStatusCode();
        await ProcessarAsync(contrato.Id);

        // Janela nov/2025–out/2026: outubro ainda não saiu. Nada de cálculo parcial, nada de aviso.
        var reajuste = Assert.Single(await ReajustesAsync(contrato.Id));
        Assert.Equal(StatusReajuste.AguardandoIndice, reajuste.Status);
        Assert.Null(reajuste.ValorNovo);
        Assert.Empty(await NotificacoesAsync(contrato.Id));
    }

    [Fact]
    public async Task Indice_atrasado_deixa_aguardando_e_calcula_quando_a_fonte_publica()
    {
        var setembro = D(2026, 9, 1);
        api.Fonte.Remover(TipoIndice.Ipca, setembro);
        await api.ComBancoAsync(db => db.ValoresIndiceMensal.Where(v => v.TipoIndice == TipoIndice.Ipca && v.MesReferencia == setembro).ExecuteDeleteAsync());
        var (http, cliente, _) = await NovoUsuarioAsync();

        var contrato = await CriarAsync(http, cliente, "2025-11-05");
        Assert.Equal(StatusReajuste.AguardandoIndice, Assert.Single(await ReajustesAsync(contrato.Id)).Status);
        Assert.Empty(await NotificacoesAsync(contrato.Id));

        api.Fonte.Restaurar();   // IBGE divulgou: setembro/2026 = 0,82 (valor oficial)
        await ColetarAsync();
        await ProcessarAsync(contrato.Id);

        var reajuste = Assert.Single(await ReajustesAsync(contrato.Id));
        Assert.Equal((StatusReajuste.Calculado, 2614.40m), (reajuste.Status, reajuste.ValorNovo));
        Assert.Single(await NotificacoesAsync(contrato.Id));
    }

    [Fact]
    public async Task Processamento_concorrente_do_mesmo_contrato_nao_duplica_reajuste_nem_aviso()
    {
        var (_, cliente, _) = await NovoUsuarioAsync();
        var contratoId = Guid.Empty;
        await api.ComBancoAsync(async db =>
        {
            // Criado direto no banco para que nenhum processamento rode antes da corrida.
            var c = Contrato.Criar(new DadosContrato(cliente, "Corrida", 2500m, D(2025, 11, 5), TipoIndice.Ipca, null, 12, 1, null, null, null, false),
                api.Relogio.GetUtcNow());
            db.Contratos.Add(c);
            await db.SaveChangesAsync();
            contratoId = c.Id;
        });

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await ProcessarAsync(contratoId); }
            catch (DbUpdateException) { /* perdeu a corrida: esperado */ }
        })));
        await ProcessarAsync(contratoId);

        var reajuste = Assert.Single(await ReajustesAsync(contratoId));
        Assert.Equal(2614.40m, reajuste.ValorNovo);
        Assert.Single(await NotificacoesAsync(contratoId));
    }

    [Fact]
    public async Task Envio_concorrente_da_mesma_notificacao_manda_um_email()
    {
        var (http, cliente, email) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2025-11-05");
        var aviso = Assert.Single(await NotificacoesAsync(contrato.Id));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => EnviarAsync(aviso.Id))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoEnvio.Enviado));
        Assert.Single(await EmailsParaAsync(email));
    }

    private sealed class NotifierQuebrado : INotifier
    {
        public Task EnviarAsync(MensagemAviso mensagem, CancellationToken ct) => throw new IOException("SMTP recusou a conexão");
    }

    [Fact]
    public async Task Falha_de_envio_conta_tentativas_e_termina_em_falhou()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2025-11-05");
        var aviso = Assert.Single(await NotificacoesAsync(contrato.Id));
        await using var quebrado = api.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<INotifier, NotifierQuebrado>()));

        async Task<ResultadoEnvio> Enviar()
        {
            using var scope = quebrado.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ServicoEnvioAvisos>().EnviarAsync(aviso.Id, CancellationToken.None);
        }

        for (var i = 1; i <= Notificacao.MaximoTentativas; i++)
            await Assert.ThrowsAsync<IOException>(Enviar);

        var final = Assert.Single(await NotificacoesAsync(contrato.Id));
        Assert.Equal((StatusNotificacao.Falhou, Notificacao.MaximoTentativas), (final.Status, final.Tentativas));
        Assert.Contains("SMTP recusou", final.UltimoErro);
        Assert.Equal(ResultadoEnvio.IgnoradoJaTratado, await Enviar());   // esgotado: não tenta de novo
        Assert.Equal(StatusReajuste.Calculado, Assert.Single(await ReajustesAsync(contrato.Id)).Status);
    }

    [Fact]
    public async Task Banco_recusa_alterar_ou_excluir_reajuste_emitido()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2024-02-10");
        var aplicado = (await ReajustesAsync(contrato.Id))[0];

        async Task<string> FalhaSql(FormattableString sql)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => api.ComBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql)));
            return ex.MessageText;
        }

        Assert.Contains("imutáveis", await FalhaSql($"""UPDATE reajustes SET "ValorNovo" = 1 WHERE "Id" = {aplicado.Id}"""));
        Assert.Contains("não permitida", await FalhaSql($"""UPDATE reajustes SET "Status" = 'Calculado' WHERE "Id" = {aplicado.Id}"""));
        Assert.Contains("excluídos", await FalhaSql($"""DELETE FROM reajustes WHERE "Id" = {aplicado.Id}"""));
        Assert.Equal(2620.78m, (await ReajustesAsync(contrato.Id))[0].ValorNovo);
    }

    [Fact]
    public async Task Corrigir_data_base_de_historico_so_retroativo_refaz_o_historico()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2024-02-10");

        var corpo = Auxiliares.ContratoIpca(cliente, "Sala 12", "2024-03-10");
        var resposta = await http.PutAsJsonAsync($"/api/contratos/{contrato.Id}", corpo);
        resposta.EnsureSuccessStatusCode();

        var reajustes = await ReajustesAsync(contrato.Id);
        Assert.Equal(4, reajustes.Count);
        Assert.All(reajustes.Where(r => r.Revisao == 0), r => Assert.Equal(StatusReajuste.Substituido, r.Status));
        var novos = reajustes.Where(r => r.Revisao == 1).ToList();
        Assert.Equal([D(2025, 3, 10), D(2026, 3, 10)], novos.Select(r => r.DataAniversario));
        Assert.All(novos, r => Assert.Equal(StatusReajuste.Aplicado, r.Status));

        // O motor (validado contra o SGS na Etapa 2) é o oráculo do histórico refeito.
        var variacoes = FonteIndicesOficialSerie(TipoIndice.Ipca);
        var esperado = MotorReajuste.CalcularHistorico(2500m, D(2024, 3, 10), new ClausulasReajuste(TipoIndice.Ipca, null, 12, 1, null, null, null, false),
            variacoes, D(2026, 10, 24));
        Assert.Equal(((ReajusteCalculado)esperado[^1].Resultado).ValorNovo, (await ContratoAsync(contrato.Id)).ValorAtual);
    }

    private static Dictionary<DateOnly, decimal> FonteIndicesOficialSerie(TipoIndice tipo)
    {
        var fonte = new FonteIndicesOficial();
        return fonte.ObterAsync(tipo, D(2015, 1, 1), D(2026, 9, 1), CancellationToken.None).Result.ToDictionary(v => v.MesReferencia, v => v.Variacao);
    }

    [Fact]
    public async Task Com_aviso_ja_enviado_data_base_fica_bloqueada()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2025-11-05");
        await EnviarAsync(Assert.Single(await NotificacoesAsync(contrato.Id)).Id);

        var resposta = await http.PutAsJsonAsync($"/api/contratos/{contrato.Id}", Auxiliares.ContratoIpca(cliente, "Sala 12", "2025-11-06"));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    }

    [Fact]
    public async Task Revisao_do_indice_recalcula_o_nao_emitido_e_so_sinaliza_o_emitido()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var naoEmitido = await CriarAsync(http, cliente, "2025-11-05");
        var emitido = await CriarAsync(http, cliente, "2025-11-06");
        await EnviarAsync(Assert.Single(await NotificacoesAsync(emitido.Id)).Id);
        var marco = D(2026, 3, 1);
        var original = FonteIndicesOficial.Oficial(TipoIndice.Ipca, marco);

        try
        {
            // Valor fictício: só exercita o mecanismo de revisão (não é dado do IBGE).
            api.Fonte.Publicar(TipoIndice.Ipca, marco, original + 0.50m);
            await ColetarAsync();

            var historico = await ReajustesAsync(naoEmitido.Id);
            Assert.Equal([StatusReajuste.Substituido, StatusReajuste.Calculado], historico.Select(r => r.Status));
            Assert.Equal(2614.40m, historico[0].ValorNovo);
            Assert.True(historico[1].ValorNovo > 2614.40m);
            Assert.Equal(historico[0].Id, historico[1].SubstituiReajusteId);
            // Relógio fixo: as duas notificações têm o mesmo CriadoEm, então a associação é pelo reajuste, não pela ordem.
            var avisoPorReajuste = (await NotificacoesAsync(naoEmitido.Id)).ToDictionary(n => n.ReajusteId, n => n.Status);
            Assert.Equal(StatusNotificacao.Cancelada, avisoPorReajuste[historico[0].Id]);
            Assert.Equal(StatusNotificacao.Pendente, avisoPorReajuste[historico[1].Id]);

            var intocado = Assert.Single(await ReajustesAsync(emitido.Id));
            Assert.Equal((StatusReajuste.Notificado, 2614.40m), (intocado.Status, intocado.ValorNovo));

            await api.ComBancoAsync(async db =>
            {
                var divergencia = await db.DivergenciasIndice.OrderByDescending(d => d.DetectadaEm).FirstAsync(d => d.MesReferencia == marco);
                Assert.True(divergencia.AfetaReajusteEmitido);
                Assert.True(divergencia.ReajustesRecalculados >= 1);
            });
        }
        finally
        {
            api.Fonte.Restaurar();
            await ColetarAsync();
        }
    }

    [Fact]
    public async Task Encerrar_contrato_cancela_aviso_pendente()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2025-11-05");

        (await http.PostAsync($"/api/contratos/{contrato.Id}/encerrar", null)).EnsureSuccessStatusCode();

        var aviso = Assert.Single(await NotificacoesAsync(contrato.Id));
        Assert.Equal(StatusNotificacao.Cancelada, aviso.Status);
        Assert.Equal(ResultadoEnvio.IgnoradoJaTratado, await EnviarAsync(aviso.Id));
    }

    [Fact]
    public async Task Configuracoes_validam_e_redirecionam_o_aviso()
    {
        var (http, cliente, email) = await NovoUsuarioAsync();

        var padrao = await http.GetFromJsonAsync<ReajusteEndpoints.ConfiguracoesResponse>("/api/configuracoes", Auxiliares.Json);
        Assert.Equal((15, email, true), (padrao!.AntecedenciaAvisoDias, padrao.DestinatarioEfetivo, padrao.AvisoEmailAtivo));

        var invalida = await http.PutAsJsonAsync("/api/configuracoes", new { antecedenciaAvisoDias = 0, emailAviso = "x", avisoEmailAtivo = true });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.Equivalent(new[] { "antecedenciaAvisoDias", "emailAviso" }, await invalida.CamposComErroAsync());

        var outro = Auxiliares.NovoEmail();
        (await http.PutAsJsonAsync("/api/configuracoes", new { antecedenciaAvisoDias = 15, emailAviso = outro, avisoEmailAtivo = true })).EnsureSuccessStatusCode();
        var contrato = await CriarAsync(http, cliente, "2025-11-05");

        Assert.Equal(outro, Assert.Single(await NotificacoesAsync(contrato.Id)).Destinatario);
    }

    [Fact]
    public async Task Carta_de_outro_usuario_nao_e_acessivel()
    {
        var (http, cliente, _) = await NovoUsuarioAsync();
        var (intruso, _, _) = await NovoUsuarioAsync();
        var contrato = await CriarAsync(http, cliente, "2024-02-10");
        var reajuste = (await ReajustesAsync(contrato.Id))[0];

        Assert.Equal(HttpStatusCode.NotFound, (await intruso.GetAsync($"/api/reajustes/{reajuste.Id}/carta.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/reajustes/{reajuste.Id}/carta.pdf")).StatusCode);
    }
}

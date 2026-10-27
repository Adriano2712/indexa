using System.Net;
using System.Net.Http.Json;
using Indexa.Api.Comum;
using Indexa.Api.Contratos;
using Indexa.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Indexa.Api.IntegrationTests;

[Collection("api")]
public sealed class ContratosTests(ApiFactory api)
{
    [Fact]
    public async Task Criar_contrato_ipca_aplica_padroes_e_retorna_201()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var clienteId = await http.CriarClienteAsync();

        var resposta = await http.PostAsJsonAsync("/api/contratos", Auxiliares.ContratoIpca(clienteId));

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var c = (await resposta.Content.ReadFromJsonAsync<ContratoEndpoints.ContratoResponse>(Auxiliares.Json))!;
        Assert.Equal(TipoIndice.Ipca, c.TipoIndice);
        Assert.Equal(12, c.PeriodicidadeMeses);
        Assert.Equal(1, c.DefasagemMeses);
        Assert.Equal(2500.00m, c.ValorAtual);
        Assert.Equal(StatusContrato.Ativo, c.Status);
        Assert.Equal("Imobiliária Teste", c.ClienteNome);
        Assert.Equal($"/api/contratos/{c.Id}", resposta.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Contrato_com_teto_piso_e_nao_reduzir_e_persistido()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var corpo = Auxiliares.ContratoIpca(await http.CriarClienteAsync());
        corpo["teto"] = 10m;
        corpo["piso"] = 0m;
        corpo["unidadeLimite"] = "Percentual";
        corpo["naoReduzir"] = true;

        var criado = await http.CriarContratoAsync(corpo);
        var detalhe = await http.GetFromJsonAsync<ContratoEndpoints.ContratoDetalheResponse>($"/api/contratos/{criado.Id}", Auxiliares.Json);

        Assert.Equal(10m, detalhe!.Contrato.Teto);
        Assert.Equal(0m, detalhe.Contrato.Piso);
        Assert.Equal(UnidadeLimite.Percentual, detalhe.Contrato.UnidadeLimite);
        Assert.True(detalhe.Contrato.NaoReduzir);
        Assert.Empty(detalhe.Reajustes);
    }

    public static TheoryData<string, string, object?> CasosInvalidos => new()
    {
        { "tipoIndice", "PercentualFixo", "percentualFixo" },
        { "valorInicial", "100.001", "valorInicial" },
        { "teto", "10", "unidadeLimite" },
        { "defasagemMeses", "9", "defasagemMeses" },
    };

    [Theory]
    [MemberData(nameof(CasosInvalidos))]
    public async Task Contrato_invalido_retorna_400_apontando_campo(string campo, string valor, object? campoEsperado)
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var corpo = Auxiliares.ContratoIpca(await http.CriarClienteAsync());
        corpo[campo] = decimal.TryParse(valor, System.Globalization.CultureInfo.InvariantCulture, out var numero) ? numero : valor;

        var resposta = await http.PostAsJsonAsync("/api/contratos", corpo);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains((string)campoEsperado!, await resposta.CamposComErroAsync());
    }

    [Fact]
    public async Task Piso_maior_que_teto_retorna_400()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var corpo = Auxiliares.ContratoIpca(await http.CriarClienteAsync());
        corpo["teto"] = 5m;
        corpo["piso"] = 6m;
        corpo["unidadeLimite"] = "Percentual";

        var resposta = await http.PostAsJsonAsync("/api/contratos", corpo);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("piso", await resposta.CamposComErroAsync());
    }

    [Fact]
    public async Task Tipo_de_indice_desconhecido_retorna_400()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var corpo = Auxiliares.ContratoIpca(await http.CriarClienteAsync());
        corpo["tipoIndice"] = "Selic";

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/contratos", corpo)).StatusCode);
    }

    [Fact]
    public async Task Nao_permite_usar_cliente_de_outro_usuario()
    {
        var dono = await api.CriarClienteAutenticadoAsync();
        var intruso = await api.CriarClienteAutenticadoAsync();
        var clienteDoDono = await dono.CriarClienteAsync();

        var resposta = await intruso.PostAsJsonAsync("/api/contratos", Auxiliares.ContratoIpca(clienteDoDono));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("clienteId", await resposta.CamposComErroAsync());
    }

    [Fact]
    public async Task Contrato_de_outro_usuario_retorna_404()
    {
        var dono = await api.CriarClienteAutenticadoAsync();
        var intruso = await api.CriarClienteAutenticadoAsync();
        var contrato = await dono.CriarContratoAsync(Auxiliares.ContratoIpca(await dono.CriarClienteAsync()));
        var corpoIntruso = Auxiliares.ContratoIpca(await intruso.CriarClienteAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await intruso.GetAsync($"/api/contratos/{contrato.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruso.PutAsJsonAsync($"/api/contratos/{contrato.Id}", corpoIntruso)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruso.PostAsync($"/api/contratos/{contrato.Id}/encerrar", null)).StatusCode);
    }

    [Fact]
    public async Task Listagem_filtra_por_busca_tipo_e_status_e_pagina()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var alfa = await http.CriarClienteAsync("Alfa Imóveis");
        var beta = await http.CriarClienteAsync("Beta Serviços");

        await http.CriarContratoAsync(Auxiliares.ContratoIpca(alfa, "Sala 101"));
        await http.CriarContratoAsync(Auxiliares.ContratoIpca(alfa, "Sala 102"));
        var igpm = Auxiliares.ContratoIpca(beta, "Manutenção elevador");
        igpm["tipoIndice"] = "IgpM";
        var contratoIgpm = await http.CriarContratoAsync(igpm);
        await http.PostAsync($"/api/contratos/{contratoIgpm.Id}/encerrar", null);

        async Task<Pagina<ContratoEndpoints.ContratoResponse>> Listar(string query) =>
            (await http.GetFromJsonAsync<Pagina<ContratoEndpoints.ContratoResponse>>($"/api/contratos?{query}", Auxiliares.Json))!;

        Assert.Equal(3, (await Listar("")).Total);
        Assert.Equal(2, (await Listar("busca=alfa")).Total);
        Assert.Equal(["Sala 102"], (await Listar("busca=102")).Itens.Select(c => c.Descricao));
        Assert.Equal(["Manutenção elevador"], (await Listar("tipoIndice=IgpM")).Itens.Select(c => c.Descricao));
        Assert.Equal(2, (await Listar("status=Ativo")).Total);

        var pagina2 = await Listar("tamanhoPagina=2&pagina=2");
        Assert.Equal(3, pagina2.Total);
        Assert.Single(pagina2.Itens);
    }

    [Fact]
    public async Task Com_reajuste_comunicado_bloqueia_data_base_mas_permite_mudar_clausulas()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var corpo = Auxiliares.ContratoIpca(await http.CriarClienteAsync());
        var contrato = await http.CriarContratoAsync(corpo);
        await api.ComBancoAsync(async db =>
        {
            // Reajuste não retroativo já aplicado = já produziu efeito para o cliente.
            var agora = DateTimeOffset.UtcNow;
            var c = await db.Contratos.SingleAsync(x => x.Id == contrato.Id);
            var r = Reajuste.NovoAguardandoIndice(c.Id, 1, c.DataBase.AddMonths(12), 2500m, false, "teste", agora);
            r.RegistrarCalculo(2600m, 4m, 4m, null, null, RegraAplicada.PercentualFixo, "[]", "{}", agora);
            r.Aplicar(agora);
            c.AplicarReajuste(1, 2600m, agora);
            db.Reajustes.Add(r);
            await db.SaveChangesAsync();
        });

        var dataBase = (string)corpo["dataBase"]!;
        corpo["dataBase"] = DateOnly.Parse(dataBase).AddMonths(1).ToString("yyyy-MM-dd");
        var bloqueado = await http.PutAsJsonAsync($"/api/contratos/{contrato.Id}", corpo);

        corpo["dataBase"] = dataBase;
        corpo["teto"] = 8m;
        corpo["unidadeLimite"] = "Percentual";
        var permitido = await http.PutAsJsonAsync($"/api/contratos/{contrato.Id}", corpo);

        Assert.Equal(HttpStatusCode.Conflict, bloqueado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, permitido.StatusCode);

        var detalhe = await http.GetFromJsonAsync<ContratoEndpoints.ContratoDetalheResponse>($"/api/contratos/{contrato.Id}", Auxiliares.Json);
        Assert.Equal(8m, detalhe!.Contrato.Teto);
        Assert.Equal(2600m, detalhe.Contrato.ValorAtual);
        var reajuste = Assert.Single(detalhe.Reajustes);
        Assert.Equal(StatusReajuste.Aplicado, reajuste.Status);
        Assert.False(reajuste.Retroativo);
    }

    [Fact]
    public async Task Contrato_encerrado_nao_aceita_edicao()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var corpo = Auxiliares.ContratoIpca(await http.CriarClienteAsync());
        var contrato = await http.CriarContratoAsync(corpo);

        (await http.PostAsync($"/api/contratos/{contrato.Id}/encerrar", null)).EnsureSuccessStatusCode();
        var edicao = await http.PutAsJsonAsync($"/api/contratos/{contrato.Id}", corpo);

        Assert.Equal(HttpStatusCode.Conflict, edicao.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Indexa.Api.Clientes;

namespace Indexa.Api.IntegrationTests;

[Collection("api")]
public sealed class ClientesTests(ApiFactory api)
{
    [Fact]
    public async Task Criar_obter_e_atualizar_cliente()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var criado = await http.PostAsJsonAsync("/api/clientes", new { nome = "Condomínio Alfa", documento = "12.ABC.345/01DE-35", email = "SINDICO@alfa.com" });
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var cliente = (await criado.Content.ReadFromJsonAsync<ClienteEndpoints.ClienteResponse>(Auxiliares.Json))!;
        Assert.Equal("12ABC34501DE35", cliente.Documento);
        Assert.Equal("sindico@alfa.com", cliente.Email);

        var atualizado = await http.PutAsJsonAsync($"/api/clientes/{cliente.Id}", new { nome = "Condomínio Alfa II" });
        atualizado.EnsureSuccessStatusCode();

        var obtido = await http.GetFromJsonAsync<ClienteEndpoints.ClienteResponse>($"/api/clientes/{cliente.Id}", Auxiliares.Json);
        Assert.Equal("Condomínio Alfa II", obtido!.Nome);
        Assert.Null(obtido.Documento);
    }

    [Fact]
    public async Task Cliente_de_outro_usuario_nao_e_visivel_nem_editavel()
    {
        var dono = await api.CriarClienteAutenticadoAsync();
        var intruso = await api.CriarClienteAutenticadoAsync();
        var id = await dono.CriarClienteAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await intruso.GetAsync($"/api/clientes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruso.PutAsJsonAsync($"/api/clientes/{id}", new { nome = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruso.DeleteAsync($"/api/clientes/{id}")).StatusCode);
        var lista = await intruso.GetFromJsonAsync<Comum.Pagina<ClienteEndpoints.ClienteResponse>>("/api/clientes", Auxiliares.Json);
        Assert.Equal(0, lista!.Total);
    }

    [Fact]
    public async Task Documento_invalido_retorna_400()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var resposta = await http.PostAsJsonAsync("/api/clientes", new { nome = "X", documento = "123" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("documento", await resposta.CamposComErroAsync());
    }

    [Fact]
    public async Task Excluir_cliente_sem_contrato_retorna_204_e_com_contrato_retorna_409()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        var semContrato = await http.CriarClienteAsync("Sem contrato");
        var comContrato = await http.CriarClienteAsync("Com contrato");
        await http.CriarContratoAsync(Auxiliares.ContratoIpca(comContrato));

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/clientes/{semContrato}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.DeleteAsync($"/api/clientes/{comContrato}")).StatusCode);
    }

    [Fact]
    public async Task Busca_por_nome_trata_curinga_como_literal()
    {
        var http = await api.CriarClienteAutenticadoAsync();
        await http.CriarClienteAsync("Alfa 100% Imóveis");
        await http.CriarClienteAsync("Beta Imóveis");

        var porcento = await http.GetFromJsonAsync<Comum.Pagina<ClienteEndpoints.ClienteResponse>>("/api/clientes?busca=%25", Auxiliares.Json);
        var imoveis = await http.GetFromJsonAsync<Comum.Pagina<ClienteEndpoints.ClienteResponse>>("/api/clientes?busca=im%C3%B3veis", Auxiliares.Json);

        Assert.Equal(["Alfa 100% Imóveis"], porcento!.Itens.Select(c => c.Nome));
        Assert.Equal(2, imoveis!.Total);
    }
}

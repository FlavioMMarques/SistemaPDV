using System.Net;
using System.Net.Http;
using System.Text;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

public class SoftcomApiClientTests
{
    private record ItemTeste(int Id, string Nome);

    private static HttpResponseMessage RespostaJson(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task UmaPaginaSoDevolveTodosOsItens()
    {
        var json = """
            { "current_page": 1, "data": [{"id":1,"nome":"A"},{"id":2,"nome":"B"}], "next_page_url": null, "total": 2, "date_sync": 1758000000 }
            """;

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(2, resultado.Itens.Count);
        Assert.Equal(1758000000, resultado.DateSync);
    }

    [Fact]
    public async Task DuasPaginasSeguePaginacaoEAcumulaItens()
    {
        var pagina1 = """
            { "current_page": 1, "data": [{"id":1,"nome":"A"}], "next_page_url": "https://exemplo.softcomshop.com.br/recurso?page=2", "total": 2 }
            """;
        var pagina2 = """
            { "current_page": 2, "data": [{"id":2,"nome":"B"}], "next_page_url": null, "total": 2 }
            """;

        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            chamadas++;
            // Cuidado: "per_page=200" contém a substring "page=2" por causa do "200" —
            // por isso a checagem precisa do "?" na frente, não um Contains solto.
            return RespostaJson(req.RequestUri!.Query.Contains("?page=2") ? pagina2 : pagina1);
        });
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(2, chamadas);
        Assert.Equal(2, resultado.Itens.Count);
        Assert.Contains(resultado.Itens, i => i.Id == 1);
        Assert.Contains(resultado.Itens, i => i.Id == 2);
    }

    [Fact]
    public async Task NextPageUrlDeDominioDiferenteEhRejeitado()
    {
        var pagina1 = """
            { "current_page": 1, "data": [{"id":1,"nome":"A"}], "next_page_url": "https://dominio-suspeito.com/recurso?page=2", "total": 2 }
            """;

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(pagina1));
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.Contains("domínio", resultado.Mensagem);
    }

    [Fact]
    public async Task RespostaComErroHttpDevolveFalhaComMensagem()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"message":"Access token expired."}""", Encoding.UTF8, "application/json"),
        });
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.Contains("401", resultado.Mensagem);
    }

    [Fact]
    public async Task RequisicaoEnviaHeadersCorretos()
    {
        HttpRequestMessage? requisicaoCapturada = null;
        var json = """{ "current_page": 1, "data": [], "next_page_url": null, "total": 0 }""";

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            requisicaoCapturada = req;
            return RespostaJson(json);
        });
        var cliente = new SoftcomApiClient(httpClient);

        await cliente.BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", 1758000000, "meu-token");

        Assert.NotNull(requisicaoCapturada);
        Assert.Equal("v2", requisicaoCapturada!.Headers.GetValues("Api-Version").Single());
        Assert.Equal("Bearer", requisicaoCapturada.Headers.Authorization?.Scheme);
        Assert.Equal("meu-token", requisicaoCapturada.Headers.Authorization?.Parameter);
        Assert.Contains("ultima_sincronizacao=1758000000", requisicaoCapturada.RequestUri!.Query);
    }
}

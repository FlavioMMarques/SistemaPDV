using System.Net;
using System.Net.Http;
using System.Text;
using SistemaPDV.Services;
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
    public async Task EnviarEmHttpNaoLoopbackNaoEnviaNadaEDevolveConexaoInsegura()
    {
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return RespostaJson("{}"); });
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.EnviarAsync(
            HttpMethod.Post, "http://exemplo.softcomshop.com.br/api/v2/vendas", new { a = 1 }, "token-fake");

        Assert.Equal(ResultadoEnvioTipo.ConexaoInsegura, resultado.Tipo);
        Assert.False(chamou);
        Assert.DoesNotContain("token-fake", resultado.Conteudo);
    }

    [Fact]
    public async Task BuscarTudoEmHttpNaoLoopbackNaoEnviaNada()
    {
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return RespostaJson("{}"); });
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.BuscarTudoAsync<ItemTeste>("http://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
    }

    [Fact]
    public async Task EnviarEmHttpLoopbackContinuaFuncionandoParaDesenvolvimento()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson("{}"));
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.EnviarAsync(HttpMethod.Post, "http://localhost:73/api/v2/vendas", new { a = 1 }, "token-fake");

        Assert.Equal(ResultadoEnvioTipo.Sucesso, resultado.Tipo);
    }

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
    public async Task VariasPaginasSaoTodasLidasNaOrdem()
    {
        // Catálogo maior que uma página (per_page=200): 5 páginas encadeadas por next_page_url.
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            chamadas++;
            var pagina = int.Parse(System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query)["page"] ?? "1");
            var proxima = pagina < 5 ? $"\"https://exemplo.softcomshop.com.br/recurso?page={pagina + 1}\"" : "null";
            return RespostaJson($$"""{ "current_page": {{pagina}}, "data": [{"id":{{pagina}},"nome":"P{{pagina}}"}], "next_page_url": {{proxima}}, "total": 5 }""");
        });

        var resultado = await new SoftcomApiClient(httpClient).BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(5, chamadas);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, resultado.Itens.Select(i => i.Id));
    }

    [Fact]
    public async Task ProximaPaginaQueRepeteUmaJaLidaInterrompeEmVezDeEntrarEmLaco()
    {
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            // Toda resposta aponta de volta pra mesma página.
            return RespostaJson("""{ "current_page": 1, "data": [{"id":1,"nome":"A"}], "next_page_url": "https://exemplo.softcomshop.com.br/recurso?page=1", "total": 9 }""");
        });

        var resultado = await new SoftcomApiClient(httpClient).BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.Contains("repetiu", resultado.Mensagem);
        Assert.True(chamadas <= 3, $"deveria parar logo, chamou {chamadas}x");
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
    public async Task NextPageUrlSemPerPageReforcaOParametroEmVezDeCairNoPadraoDoServidor()
    {
        // Reproduz o bug real observado: next_page_url não carrega "per_page" — sem reforçar,
        // a próxima chamada usaria o per_page PADRÃO do servidor (aqui simulado: cabe tudo numa
        // página só, e a "página 2" pedida volta vazia, com last_page=1, como se tivesse acabado).
        var pagina1 = """
            { "current_page": 1, "data": [{"id":1,"nome":"A"},{"id":2,"nome":"B"}],
              "next_page_url": "https://exemplo.softcomshop.com.br/recurso?page=2", "total": 3 }
            """;
        var pagina2ComPerPage = """{ "current_page": 2, "data": [{"id":3,"nome":"C"}], "next_page_url": null, "total": 3 }""";
        var pagina2SemPerPageFantasma = """{ "current_page": 2, "data": [], "last_page": 1, "next_page_url": null, "total": 3 }""";

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            var query = req.RequestUri!.Query;
            // "?page=2"/"&page=2" ancorado — "per_page=200" também contém a substring solta "page=2".
            if (!query.Contains("?page=2") && !query.Contains("&page=2"))
                return RespostaJson(pagina1);

            return RespostaJson(query.Contains("per_page=") ? pagina2ComPerPage : pagina2SemPerPageFantasma);
        });

        var resultado = await new SoftcomApiClient(httpClient).BuscarTudoAsync<ItemTeste>(
            "https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(3, resultado.Itens.Count);
        Assert.Contains(resultado.Itens, i => i.Id == 3);
    }

    [Fact]
    public async Task NextPageUrlNuloAntesDoTotalDeclaradoDevolveFalha()
    {
        // A API diz que tem 5 no total, mas devolve next_page_url nulo já na 1ª página com só 2 itens.
        var json = """
            { "current_page": 1, "data": [{"id":1,"nome":"A"},{"id":2,"nome":"B"}], "next_page_url": null, "total": 5 }
            """;

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var cliente = new SoftcomApiClient(httpClient);

        var resultado = await cliente.BuscarTudoAsync<ItemTeste>("https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.Contains("parou de paginar", resultado.Mensagem);
        Assert.Contains("2 de 5", resultado.Mensagem);
    }

    [Fact]
    public async Task BuscarTudoTentaDeNovoAposFalhaDeRedeTransitoriaEDevolveSucesso()
    {
        var chamadas = 0;
        var json = """{ "current_page": 1, "data": [{"id":1,"nome":"A"}], "next_page_url": null, "total": 1 }""";
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            if (chamadas <= 2)
                throw new HttpRequestException("Conexão recusada (simulada)");
            return RespostaJson(json);
        });

        var resultado = await new SoftcomApiClient(httpClient).BuscarTudoAsync<ItemTeste>(
            "https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(3, chamadas);
        Assert.Single(resultado.Itens);
    }

    [Fact]
    public async Task BuscarTudoEsgotaTentativasEPropagaFalhaDeRede()
    {
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            throw new HttpRequestException("Conexão recusada (simulada, sempre)");
        });

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new SoftcomApiClient(httpClient).BuscarTudoAsync<ItemTeste>(
                "https://exemplo.softcomshop.com.br", "recurso", null, "token-fake"));

        Assert.Equal(4, chamadas); // 1 tentativa original + 3 retries
    }

    [Fact]
    public async Task EnviarTentaDeNovoAposFalhaDeRedeTransitoriaEDevolveSucesso()
    {
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            if (chamadas == 1)
                throw new HttpRequestException("Conexão recusada (simulada)");
            return RespostaJson("{}");
        });

        var resultado = await new SoftcomApiClient(httpClient).EnviarAsync(
            HttpMethod.Post, "https://exemplo.softcomshop.com.br/api/v2/vendas", new { a = 1 }, "token-fake");

        Assert.Equal(ResultadoEnvioTipo.Sucesso, resultado.Tipo);
        Assert.Equal(2, chamadas);
    }

    [Fact]
    public async Task EnviarEsgotaTentativasEDevolveFalhaDeConexaoEmVezDeLancar()
    {
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            throw new HttpRequestException("Conexão recusada (simulada, sempre)");
        });

        var resultado = await new SoftcomApiClient(httpClient).EnviarAsync(
            HttpMethod.Post, "https://exemplo.softcomshop.com.br/api/v2/vendas", new { a = 1 }, "token-fake");

        Assert.Equal(ResultadoEnvioTipo.Falha, resultado.Tipo);
        Assert.Contains("Falha de conexão", resultado.Conteudo);
        Assert.Equal(4, chamadas);
    }

    [Fact]
    public async Task RespostaComStatusDeErroNaoAcionaRetry()
    {
        // 401/409/422 são classificação de negócio, não falha de rede — não deve haver retry.
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"message":"Access token expired."}""", Encoding.UTF8, "application/json"),
            };
        });

        await new SoftcomApiClient(httpClient).BuscarTudoAsync<ItemTeste>(
            "https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

        Assert.Equal(1, chamadas);
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

    // ---- log das requisições ----

    private static string LerLog(string pasta) =>
        string.Join(Environment.NewLine, Directory.GetFiles(pasta, "pdv-*.log").Select(File.ReadAllText));

    [Fact]
    public async Task EnviarAsyncRegistraMetodoUrlECorpoMascarandoCpfETokenNoCorpo()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "SistemaPDV.Tests.Log", Guid.NewGuid().ToString("N"));
        var anterior = Registro.Destino;
        try
        {
            Registro.Destino = new LogArquivo(pasta);
            var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson("{}"));
            var cliente = new SoftcomApiClient(httpClient);

            await cliente.EnviarAsync(HttpMethod.Post, "https://exemplo.softcomshop.com.br/api/v2/vendas",
                new { nome = "Cliente Teste", cpfCnpj = "529.982.247-25", token = "abc123XYZ" }, "token-fake");

            var texto = LerLog(pasta);
            Assert.Contains("POST https://exemplo.softcomshop.com.br/api/v2/vendas", texto);
            Assert.Contains("Cliente Teste", texto);            // dado não-sensível passa
            Assert.DoesNotContain("529.982.247-25", texto);     // CPF mascarado (regex de CPF/CNPJ)
            Assert.DoesNotContain("abc123XYZ", texto);          // chave "token" mascarada
        }
        finally
        {
            Registro.Destino = anterior;
            try { Directory.Delete(pasta, recursive: true); } catch (Exception) { /* limpeza de teste */ }
        }
    }

    [Fact]
    public async Task BuscarTudoAsyncRegistraCadaPaginaComoUmaRequisicaoGet()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "SistemaPDV.Tests.Log", Guid.NewGuid().ToString("N"));
        var anterior = Registro.Destino;
        try
        {
            Registro.Destino = new LogArquivo(pasta);
            var pagina1 = """
                { "current_page": 1, "data": [{"id":1,"nome":"A"}], "next_page_url": "https://exemplo.softcomshop.com.br/recurso?page=2", "total": 2 }
                """;
            var pagina2 = """{ "current_page": 2, "data": [{"id":2,"nome":"B"}], "next_page_url": null, "total": 2 }""";
            // "?page=2" ancorado — "per_page=200" também contém a substring solta "page=2".
            var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
                RespostaJson(req.RequestUri!.Query.Contains("?page=2") || req.RequestUri!.Query.Contains("&page=2") ? pagina2 : pagina1));

            await new SoftcomApiClient(httpClient).BuscarTudoAsync<ItemTeste>(
                "https://exemplo.softcomshop.com.br", "recurso", null, "token-fake");

            var texto = LerLog(pasta);
            Assert.Contains("GET https://exemplo.softcomshop.com.br/recurso?per_page=200", texto);
            Assert.Contains("GET https://exemplo.softcomshop.com.br/recurso?page=2&per_page=200", texto);
        }
        finally
        {
            Registro.Destino = anterior;
            try { Directory.Delete(pasta, recursive: true); } catch (Exception) { /* limpeza de teste */ }
        }
    }
}

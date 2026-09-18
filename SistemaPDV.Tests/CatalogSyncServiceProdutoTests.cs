using System.Net;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class CatalogSyncServiceProdutoTests
{
    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
        await context.SaveChangesAsync();
    }

    private static HttpResponseMessage RespostaJson(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task SincronizaProdutoComImagens()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """
            { "current_page": 1, "data": [
                {
                  "id": 10, "sku": "REF2L", "nome": "Refrigerante 2L", "estoque": 15,
                  "preco_venda": 9.90, "ncm": "22021000", "vender": 1,
                  "produto_imagem": [{"descricao": "Foto principal", "arquivo_original": "ref2l.jpg", "tipo": "PRINCIPAL"}]
                }
              ], "next_page_url": null, "total": 1, "date_sync": 1758000000 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarProdutosAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Include(p => p.Imagens).Single(p => p.IdExterno == 10);

        Assert.Equal("Refrigerante 2L", produto.Nome);
        Assert.True(produto.Vender);
        Assert.Single(produto.Imagens);
        Assert.Equal("ref2l.jpg", produto.Imagens.Single().ArquivoOriginal);
    }

    [Fact]
    public async Task VenderIgualA0ViraFalse()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """{ "current_page": 1, "data": [{"id": 11, "nome": "Item fora de venda", "estoque": 0, "preco_venda": 0, "vender": 0}], "next_page_url": null, "total": 1 }""";
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarProdutosAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.False(leitura.Produtos.Single().Vender);
    }

    [Fact]
    public async Task ResincronizarSubstituiAListaDeImagensEmVezDeAcumular()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var jsonComUmaImagem = """
            { "current_page": 1, "data": [{"id": 10, "nome": "Refrigerante 2L", "estoque": 15, "preco_venda": 9.90,
                "produto_imagem": [{"arquivo_original": "v1.jpg"}]}], "next_page_url": null, "total": 1 }
            """;
        var jsonComOutraImagem = """
            { "current_page": 1, "data": [{"id": 10, "nome": "Refrigerante 2L", "estoque": 15, "preco_venda": 9.90,
                "produto_imagem": [{"arquivo_original": "v2.jpg"}]}], "next_page_url": null, "total": 1 }
            """;

        var primeiraChamada = true;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            var resposta = RespostaJson(primeiraChamada ? jsonComUmaImagem : jsonComOutraImagem);
            primeiraChamada = false;
            return resposta;
        });
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarProdutosAsync("token-fake");
        await service.SincronizarProdutosAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Include(p => p.Imagens).Single();
        Assert.Single(produto.Imagens);
        Assert.Equal("v2.jpg", produto.Imagens.Single().ArquivoOriginal);
    }
}

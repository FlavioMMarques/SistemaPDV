using System.Net;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class CatalogSyncServiceClienteTests
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
    public async Task SincronizaClienteJuridicoComTabelaPrecoEEnderecoCompleto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """
            { "current_page": 1, "data": [
                {
                  "id": 2, "nome": "C. CRIATIVO", "razao_social": "C. CRIATIVO COMERCIO DE PRESENTES LTDA - ME",
                  "pessoa": "JURIDICA", "cpf_cnpj": "26849839000192", "bloqueado": "0",
                  "cidade": "CURITIBA", "uf": "PR",
                  "tabela_preco": { "id": null, "descricao": "PADRAO" }
                }
              ], "next_page_url": null, "total": 1, "date_sync": 1758000000 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarClientesAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single(c => c.IdExterno == 2);

        Assert.Equal(TipoPessoa.Juridica, cliente.Pessoa);
        Assert.False(cliente.Bloqueado);
        Assert.Equal("PADRAO", cliente.TabelaPreco?.Descricao);
        Assert.Equal("CURITIBA", cliente.Cidade);
    }

    [Fact]
    public async Task ClienteBloqueadoIgualA1ViraVerdadeiro()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """{ "current_page": 1, "data": [{"id": 1, "nome": "CONSUMIDOR", "pessoa": "FISICA", "bloqueado": "1"}], "next_page_url": null, "total": 1 }""";
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarClientesAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.True(leitura.Clientes.Single().Bloqueado);
    }

    [Fact]
    public async Task ClienteSemTabelaPrecoFicaComTabelaPrecoNula()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """{ "current_page": 1, "data": [{"id": 1, "nome": "CONSUMIDOR", "pessoa": "FISICA"}], "next_page_url": null, "total": 1 }""";
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarClientesAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.Clientes.Single().TabelaPreco);
    }
}

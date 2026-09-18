using System.Net;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// CatalogSyncService agora recebe SegredoProtector (Windows-only) no construtor.
[SupportedOSPlatform("windows")]
public class CatalogSyncServiceFormaPagamentoTests
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
    public async Task PrimeiraSincronizacaoCriaOsRegistros()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """
            { "current_page": 1, "data": [
                {"id": 5, "nome": "PIX", "tipo": "CARTEIRA_DIGITAL", "padrao": true, "carteira_digital": true, "ordem": 9},
                {"id": 6, "nome": "DINHEIRO", "tipo": "ESPECIE", "padrao": false, "ordem": 1}
              ], "next_page_url": null, "total": 2, "date_sync": 1758000000 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarFormasPagamentoAsync("token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(2, resultado.Quantidade);

        using var leitura = fixture.CriarContexto();
        Assert.Equal(2, leitura.FormasPagamento.Count());
        var pix = leitura.FormasPagamento.Single(f => f.IdExterno == 5);
        Assert.Equal("PIX", pix.Nome);
        Assert.Equal(SyncStatus.Sincronizado, pix.SyncStatus);

        var configuracao = leitura.ConfiguracoesSincronizacao.Single();
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1758000000), configuracao.UltimaSincronizacaoFormasPagamento);
    }

    [Fact]
    public async Task SegundaSincronizacaoComMesmoIdExternoAtualizaEmVezDeDuplicar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var jsonV1 = """{ "current_page": 1, "data": [{"id": 5, "nome": "PIX", "tipo": "CARTEIRA_DIGITAL", "ordem": 9}], "next_page_url": null, "total": 1 }""";
        var jsonV2 = """{ "current_page": 1, "data": [{"id": 5, "nome": "PIX ATUALIZADO", "tipo": "CARTEIRA_DIGITAL", "ordem": 9}], "next_page_url": null, "total": 1 }""";

        var primeiraChamada = true;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            var resposta = RespostaJson(primeiraChamada ? jsonV1 : jsonV2);
            primeiraChamada = false;
            return resposta;
        });
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarFormasPagamentoAsync("token-fake");
        await service.SincronizarFormasPagamentoAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.Single(leitura.FormasPagamento);
        Assert.Equal("PIX ATUALIZADO", leitura.FormasPagamento.Single().Nome);
    }

    [Fact]
    public async Task FalhaDaApiNaoLancaExcecao()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"message":"Access token expired."}""", Encoding.UTF8, "application/json"),
        });
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarFormasPagamentoAsync("token-fake");

        Assert.False(resultado.Sucesso);
        Assert.Contains("401", resultado.Mensagem);
    }
}

using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Services;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class ConfiguracaoServiceTests
{
    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static ConfiguracaoService CriarService(SqliteInMemoryFixture fixture, HttpClient httpClient)
    {
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        return new ConfiguracaoService(fixture.CriarContexto, authService, segredoProtector);
    }

    [Fact]
    public async Task ObterOuCriarCriaConfiguracaoNovaQuandoNaoExisteNenhuma()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var service = CriarService(fixture, httpClient);

        var configuracao = await service.ObterOuCriarAsync();

        Assert.True(configuracao.ExigirAberturaCaixa);
        using var leitura = fixture.CriarContexto();
        Assert.Single(leitura.ConfiguracoesSincronizacao);
    }

    [Fact]
    public async Task ObterOuCriarRetornaAExistenteSemDuplicar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new SistemaPDV.Models.ConfiguracaoSincronizacao { NomeDispositivo = "PDV-01" });
            await context.SaveChangesAsync();
        }
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var service = CriarService(fixture, httpClient);

        var configuracao = await service.ObterOuCriarAsync();

        Assert.Equal("PDV-01", configuracao.NomeDispositivo);
        using var leitura = fixture.CriarContexto();
        Assert.Single(leitura.ConfiguracoesSincronizacao);
    }

    [Fact]
    public async Task VincularDispositivoComSucessoProtegeSegredoEExtraiClienteIdDoLink()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "data": { "client_secret": "segredo-puro" } }"""));
        var service = CriarService(fixture, httpClient);
        await service.ObterOuCriarAsync();

        var (sucesso, _) = await service.VincularDispositivoAsync(
            "https://exemplo.softcomshop.com.br/registrar?client_id=42&empresa_cnpj=123", "PDV-01");

        Assert.True(sucesso);
        using var leitura = fixture.CriarContexto();
        var configuracao = leitura.ConfiguracoesSincronizacao.Single();
        Assert.Equal("42", configuracao.ApiClienteId);
        Assert.Equal("PDV-01", configuracao.NomeDispositivo);
        Assert.NotEqual("segredo-puro", configuracao.ApiClienteSecretProtegido);
        Assert.Equal("segredo-puro", new SegredoProtector().Desproteger(configuracao.ApiClienteSecretProtegido));
    }

    [Fact]
    public async Task VincularDispositivoComFalhaNaApiNaoAlteraConfiguracao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.BadRequest, """{ "message": "device_id invalido" }"""));
        var service = CriarService(fixture, httpClient);
        await service.ObterOuCriarAsync();

        var (sucesso, mensagem) = await service.VincularDispositivoAsync(
            "https://exemplo.softcomshop.com.br/registrar?client_id=42", "PDV-01");

        Assert.False(sucesso);
        Assert.Contains("400", mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.ConfiguracoesSincronizacao.Single().ApiClienteSecretProtegido);
    }

    [Fact]
    public async Task AtualizarAsyncAplicaMudancasArbitrarias()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var service = CriarService(fixture, httpClient);
        await service.ObterOuCriarAsync();

        await service.AtualizarAsync(c =>
        {
            c.ExigirAberturaCaixa = false;
            c.ClienteConsumidorFinalIdExterno = 1;
        });

        using var leitura = fixture.CriarContexto();
        var configuracao = leitura.ConfiguracoesSincronizacao.Single();
        Assert.False(configuracao.ExigirAberturaCaixa);
        Assert.Equal(1, configuracao.ClienteConsumidorFinalIdExterno);
    }
}

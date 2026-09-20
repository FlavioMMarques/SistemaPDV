using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class SoftcomAuthServiceTests
{
    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task ObterTokenEmHttpNaoLoopbackNaoEnviaOClientSecret()
    {
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return RespostaJson(HttpStatusCode.OK, "{}"); });
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());
        var configuracao = new ConfiguracaoSincronizacao
        {
            UrlApi = "http://exemplo.softcomshop.com.br/registrar?client_id=1",
            ApiClienteId = "1",
            ApiClienteSecretProtegido = "secret-de-teste",
        };

        var (sucesso, mensagem, token) = await service.ObterTokenAsync(configuracao);

        Assert.False(sucesso);
        Assert.Null(token);
        Assert.False(chamou);
        Assert.Contains("HTTPS", mensagem);
    }

    [Fact]
    public async Task ObterClienteSecretEmHttpNaoLoopbackNaoEnviaNada()
    {
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return RespostaJson(HttpStatusCode.OK, "{}"); });
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());

        var (sucesso, mensagem, clienteSecret) = await service.ObterClienteSecretAsync(
            "http://exemplo.softcomshop.com.br/registrar?client_id=1", "PDV-01");

        Assert.False(sucesso);
        Assert.Null(clienteSecret);
        Assert.False(chamou);
        Assert.Contains("HTTPS", mensagem);
    }

    [Fact]
    public async Task ObterClienteSecretComSucessoExtraiOValor()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "data": { "client_secret": "abc123" } }"""));
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());

        var (sucesso, _, clienteSecret) = await service.ObterClienteSecretAsync(
            "https://exemplo.softcomshop.com.br/registrar?client_id=1", "PDV-01");

        Assert.True(sucesso);
        Assert.Equal("abc123", clienteSecret);
    }

    [Fact]
    public async Task ObterClienteSecretSemientAoNaRespostaFalha()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "data": { } }"""));
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());

        var (sucesso, mensagem, clienteSecret) = await service.ObterClienteSecretAsync(
            "https://exemplo.softcomshop.com.br/registrar?client_id=1", "PDV-01");

        Assert.False(sucesso);
        Assert.Null(clienteSecret);
        Assert.Contains("client_secret", mensagem);
    }

    [Fact]
    public async Task ObterClienteSecretComErroHttpFalha()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.BadRequest, """{ "message": "device_id invalido" }"""));
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());

        var (sucesso, mensagem, _) = await service.ObterClienteSecretAsync(
            "https://exemplo.softcomshop.com.br/registrar?client_id=1", "PDV-01");

        Assert.False(sucesso);
        Assert.Contains("400", mensagem);
    }

    [Fact]
    public async Task ObterTokenComChaveDataTokenExtraiOValor()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "data": { "token": "meu-access-token" } }"""));
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());
        var configuracao = new ConfiguracaoSincronizacao
        {
            UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
            ApiClienteId = "1",
            ApiClienteSecretProtegido = "secret-de-teste",
        };

        var (sucesso, _, token) = await service.ObterTokenAsync(configuracao);

        Assert.True(sucesso);
        Assert.Equal("meu-access-token", token);
    }

    [Fact]
    public async Task ObterTokenComChaveAccessTokenNaRaizExtraiOValor()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "access_token": "outro-formato-de-token" }"""));
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());
        var configuracao = new ConfiguracaoSincronizacao
        {
            UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
            ApiClienteId = "1",
            ApiClienteSecretProtegido = "secret-de-teste",
        };

        var (sucesso, _, token) = await service.ObterTokenAsync(configuracao);

        Assert.True(sucesso);
        Assert.Equal("outro-formato-de-token", token);
    }

    [Fact]
    public async Task ObterTokenComErroHttpFalha()
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.Unauthorized, """{ "message": "invalid_client" }"""));
        var service = new SoftcomAuthService(httpClient, new SegredoProtector());
        var configuracao = new ConfiguracaoSincronizacao
        {
            UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
            ApiClienteId = "1",
            ApiClienteSecretProtegido = "secret-de-teste",
        };

        var (sucesso, mensagem, token) = await service.ObterTokenAsync(configuracao);

        Assert.False(sucesso);
        Assert.Null(token);
        Assert.Contains("401", mensagem);
    }

    [Fact]
    public void ExtrairDominioPegaSoOEsquemaEHost()
    {
        var dominio = SoftcomAuthService.ExtrairDominio("https://exemplo.softcomshop.com.br/registrar?client_id=1&empresa_cnpj=123");

        Assert.Equal("https://exemplo.softcomshop.com.br", dominio);
    }
}

using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Services;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class ConfiguracoesViewModelTests
{
    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static ConfiguracoesViewModel CriarViewModel(SqliteInMemoryFixture fixture, HttpClient httpClient)
    {
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        var configuracaoService = new ConfiguracaoService(fixture.CriarContexto, authService, segredoProtector);
        return new ConfiguracoesViewModel(configuracaoService);
    }

    [Fact]
    public async Task IniciarCarregaExigirAberturaCaixaComoTrueQuandoConfiguracaoNova()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var viewModel = CriarViewModel(fixture, httpClient);

        await viewModel.IniciarAsync();

        Assert.True(viewModel.ExigirAberturaCaixa);
    }

    [Fact]
    public void VincularCommandDesabilitadoSemLinkOuNomeDispositivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var viewModel = CriarViewModel(fixture, httpClient);

        var podeExecutar = false;
        viewModel.VincularCommand.CanExecute.Subscribe(v => podeExecutar = v);

        Assert.False(podeExecutar);

        viewModel.LinkCadastro = "https://exemplo.softcomshop.com.br/registrar?client_id=1";
        viewModel.NomeDispositivo = "PDV-01";

        Assert.True(podeExecutar);
    }

    [Fact]
    public async Task VincularComSucessoGravaConfiguracaoEMostraMensagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "data": { "client_secret": "abc" } }"""));
        var viewModel = CriarViewModel(fixture, httpClient);
        await viewModel.IniciarAsync();
        viewModel.LinkCadastro = "https://exemplo.softcomshop.com.br/registrar?client_id=7";
        viewModel.NomeDispositivo = "PDV-01";

        await viewModel.VincularCommand.Execute();

        Assert.False(string.IsNullOrEmpty(viewModel.Mensagem));
        using var leitura = fixture.CriarContexto();
        var configuracao = leitura.ConfiguracoesSincronizacao.Single();
        Assert.Equal("7", configuracao.ApiClienteId);
    }

    [Fact]
    public async Task SalvarCommandPersisteTogglesSemPrecisarVincularDeNovo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var viewModel = CriarViewModel(fixture, httpClient);
        await viewModel.IniciarAsync();
        viewModel.ExigirAberturaCaixa = false;
        viewModel.ClienteConsumidorFinalIdExterno = "1";

        await viewModel.SalvarCommand.Execute();

        using var leitura = fixture.CriarContexto();
        var configuracao = leitura.ConfiguracoesSincronizacao.Single();
        Assert.False(configuracao.ExigirAberturaCaixa);
        Assert.Equal(1, configuracao.ClienteConsumidorFinalIdExterno);
    }
}

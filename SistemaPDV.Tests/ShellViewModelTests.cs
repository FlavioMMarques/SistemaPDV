using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Versioning;
using System.Text;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class ShellViewModelTests
{
    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static (
        ConfiguracaoService Configuracao,
        LoginOperadorService Login,
        CaixaService Caixa,
        DashboardService Dashboard,
        VendaService Venda,
        CatalogoLocalService CatalogoLocal) CriarServicos(SqliteInMemoryFixture fixture)
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        return (
            new ConfiguracaoService(fixture.CriarContexto, authService, segredoProtector),
            new LoginOperadorService(fixture.CriarContexto),
            new CaixaService(fixture.CriarContexto),
            new DashboardService(fixture.CriarContexto),
            new VendaService(fixture.CriarContexto),
            new CatalogoLocalService(fixture.CriarContexto));
    }

    private static async Task<int> SemearFuncionarioAsync(SqliteInMemoryFixture fixture, string pdvKey)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos Silva", PdvKeyHash = PdvKeyHasher.Hash(pdvKey) };
        context.Funcionarios.Add(funcionario);
        await context.SaveChangesAsync();
        return funcionario.Id;
    }

    [Fact]
    public async Task SemConfiguracaoVaiDireitoPraConfiguracoes()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (configuracao, login, caixa, dashboard, venda, catalogoLocal) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa, dashboard, venda, catalogoLocal);

        await shell.IniciarAsync();

        Assert.Equal(Tela.Configuracoes, shell.TelaAtual);
        Assert.IsType<ConfiguracoesViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task ComUrlApiPreenchidaVaiDireitoPraLogin()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
            await context.SaveChangesAsync();
        }
        var (configuracao, login, caixa, dashboard, venda, catalogoLocal) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa, dashboard, venda, catalogoLocal);

        await shell.IniciarAsync();

        Assert.Equal(Tela.Login, shell.TelaAtual);
        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task LoginSemCaixaAbertoEComExigenciaVaiPraAbrirCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ExigirAberturaCaixa = true,
            });
            await context.SaveChangesAsync();
        }
        await SemearFuncionarioAsync(fixture, "1234");
        var (configuracao, login, caixa, dashboard, venda, catalogoLocal) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa, dashboard, venda, catalogoLocal);
        await shell.IniciarAsync();
        var loginViewModel = (LoginViewModel)shell.CurrentViewModel!;
        loginViewModel.PdvKeyDigitada = "1234";

        // AposLoginAsync roda desacoplado (Subscribe, não faz parte da pipeline que
        // Execute() devolve — ver docs/APRENDIZADOS.md) — o teste espera o sinal de
        // saída real (TelaAtual mudar), não o comando de login em si.
        var telaMudou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await loginViewModel.EntrarCommand.Execute();
        await telaMudou;

        Assert.Equal(Tela.AbrirCaixa, shell.TelaAtual);
        Assert.IsType<AbrirCaixaViewModel>(shell.CurrentViewModel);
        Assert.NotNull(shell.OperadorLogado);
        Assert.Null(shell.CaixaAberto);
    }

    [Fact]
    public async Task AbrirCaixaComSucessoLevaPraDashboard()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ExigirAberturaCaixa = true,
            });
            await context.SaveChangesAsync();
        }
        await SemearFuncionarioAsync(fixture, "1234");
        var (configuracao, login, caixa, dashboard, venda, catalogoLocal) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa, dashboard, venda, catalogoLocal);
        await shell.IniciarAsync();
        var loginViewModel = (LoginViewModel)shell.CurrentViewModel!;
        loginViewModel.PdvKeyDigitada = "1234";
        var chegouEmAbrirCaixa = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await loginViewModel.EntrarCommand.Execute();
        await chegouEmAbrirCaixa;
        var abrirCaixaViewModel = (AbrirCaixaViewModel)shell.CurrentViewModel!;
        abrirCaixaViewModel.TrocoInicial = "10.00";

        var chegouNoDashboard = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.AbrirCaixa).FirstAsync().ToTask();
        await abrirCaixaViewModel.AbrirCommand.Execute();
        await chegouNoDashboard;

        Assert.Equal(Tela.Dashboard, shell.TelaAtual);
        Assert.IsType<DashboardViewModel>(shell.CurrentViewModel);
        Assert.NotNull(shell.CaixaAberto);
    }

    [Fact]
    public async Task LoginSemCaixaAbertoESemExigenciaVaiPraDashboard()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ExigirAberturaCaixa = false,
            });
            await context.SaveChangesAsync();
        }
        await SemearFuncionarioAsync(fixture, "1234");
        var (configuracao, login, caixa, dashboard, venda, catalogoLocal) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa, dashboard, venda, catalogoLocal);
        await shell.IniciarAsync();
        var loginViewModel = (LoginViewModel)shell.CurrentViewModel!;
        loginViewModel.PdvKeyDigitada = "1234";

        var telaMudou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await loginViewModel.EntrarCommand.Execute();
        await telaMudou;

        Assert.Equal(Tela.Dashboard, shell.TelaAtual);
    }

    [Fact]
    public async Task LoginComCaixaJaAbertoVaiPraDashboardMesmoExigindo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ExigirAberturaCaixa = true,
            });
            await context.SaveChangesAsync();
        }
        var funcionarioId = await SemearFuncionarioAsync(fixture, "1234");
        var (configuracao, login, caixa, dashboard, venda, catalogoLocal) = CriarServicos(fixture);
        await caixa.AbrirCaixaLocalAsync(funcionarioId, DateOnly.FromDateTime(DateTime.Now), 1, 10m);
        var shell = new ShellViewModel(configuracao, login, caixa, dashboard, venda, catalogoLocal);
        await shell.IniciarAsync();
        var loginViewModel = (LoginViewModel)shell.CurrentViewModel!;
        loginViewModel.PdvKeyDigitada = "1234";

        var telaMudou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await loginViewModel.EntrarCommand.Execute();
        await telaMudou;

        Assert.Equal(Tela.Dashboard, shell.TelaAtual);
        Assert.NotNull(shell.CaixaAberto);
    }

    [Fact]
    public async Task JornadaCompletaLoginAbrirCaixaDashboardNovaVendaChegaNoPdv()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ExigirAberturaCaixa = true,
            });
            await context.SaveChangesAsync();
        }
        await SemearFuncionarioAsync(fixture, "1234");
        var (configuracao, login, caixa, dashboard, venda, catalogoLocal) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa, dashboard, venda, catalogoLocal);
        await shell.IniciarAsync();
        var loginViewModel = (LoginViewModel)shell.CurrentViewModel!;
        loginViewModel.PdvKeyDigitada = "1234";
        var chegouEmAbrirCaixa = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await loginViewModel.EntrarCommand.Execute();
        await chegouEmAbrirCaixa;
        var abrirCaixaViewModel = (AbrirCaixaViewModel)shell.CurrentViewModel!;
        abrirCaixaViewModel.TrocoInicial = "10.00";
        var chegouNoDashboard = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.AbrirCaixa).FirstAsync().ToTask();
        await abrirCaixaViewModel.AbrirCommand.Execute();
        await chegouNoDashboard;
        var dashboardViewModel = (DashboardViewModel)shell.CurrentViewModel!;

        var chegouNoPdv = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Dashboard).FirstAsync().ToTask();
        await dashboardViewModel.NovaVendaCommand.Execute();
        await chegouNoPdv;

        Assert.Equal(Tela.Pdv, shell.TelaAtual);
        Assert.IsType<PdvViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task FalhaInesperadaAposLoginNaoLancaESetaMensagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
            await context.SaveChangesAsync();
        }
        await SemearFuncionarioAsync(fixture, "1234");
        var (configuracao, login, _, dashboard, venda, catalogoLocal) = CriarServicos(fixture);

        // CaixaService com um contextFactory que sempre lança — simula uma falha
        // inesperada bem depois do login (ex: banco indisponível num instante),
        // exatamente onde AposLoginAsync roda desacoplado (Subscribe, fire-and-forget,
        // ver docs/APRENDIZADOS.md). Sem o try/catch do achado da revisão de código,
        // isso derrubaria a exceção como task nunca observada, travando a tela sem
        // nenhum aviso.
        var caixaQuebrado = new CaixaService(() => throw new InvalidOperationException("Falha simulada de banco."));
        var shell = new ShellViewModel(configuracao, login, caixaQuebrado, dashboard, venda, catalogoLocal);
        await shell.IniciarAsync();
        var loginViewModel = (LoginViewModel)shell.CurrentViewModel!;
        loginViewModel.PdvKeyDigitada = "1234";

        var mensagemMudou = shell.WhenAnyValue(s => s.Mensagem).Where(m => m is not null).FirstAsync().ToTask();
        var excecao = await Record.ExceptionAsync(() => loginViewModel.EntrarCommand.Execute().ToTask());
        await mensagemMudou;

        Assert.Null(excecao);
        Assert.False(string.IsNullOrEmpty(shell.Mensagem));
    }
}

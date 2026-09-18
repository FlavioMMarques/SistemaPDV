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

    private static (ConfiguracaoService Configuracao, LoginOperadorService Login, CaixaService Caixa) CriarServicos(SqliteInMemoryFixture fixture)
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, "{}"));
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        return (
            new ConfiguracaoService(fixture.CriarContexto, authService, segredoProtector),
            new LoginOperadorService(fixture.CriarContexto),
            new CaixaService(fixture.CriarContexto));
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
        var (configuracao, login, caixa) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa);

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
        var (configuracao, login, caixa) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa);

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
        var (configuracao, login, caixa) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa);
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
        Assert.NotNull(shell.OperadorLogado);
        Assert.Null(shell.CaixaAberto);
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
        var (configuracao, login, caixa) = CriarServicos(fixture);
        var shell = new ShellViewModel(configuracao, login, caixa);
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
        var (configuracao, login, caixa) = CriarServicos(fixture);
        await caixa.AbrirCaixaLocalAsync(funcionarioId, DateOnly.FromDateTime(DateTime.Now), 1, 10m);
        var shell = new ShellViewModel(configuracao, login, caixa);
        await shell.IniciarAsync();
        var loginViewModel = (LoginViewModel)shell.CurrentViewModel!;
        loginViewModel.PdvKeyDigitada = "1234";

        var telaMudou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await loginViewModel.EntrarCommand.Execute();
        await telaMudou;

        Assert.Equal(Tela.Dashboard, shell.TelaAtual);
        Assert.NotNull(shell.CaixaAberto);
    }
}

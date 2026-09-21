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

// Sair (logoff) no Shell: volta ao login sem fechar nada.
[SupportedOSPlatform("windows")]
public class ShellSairTests
{
    private static async Task<ShellViewModel> CriarShellNoLoginAsync(SqliteInMemoryFixture fixture, bool comCaixaAberto = true)
    {
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ExigirAberturaCaixa = false,
            });
            context.Funcionarios.Add(new Funcionario { Nome = "Carlos", PdvKeyHash = PdvKeyTeste.Hash("1234") });
            await context.SaveChangesAsync();
        }

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        var protetor = new SegredoProtector();
        var caixaService = new CaixaService(fixture.CriarContexto);
        if (comCaixaAberto)
            await caixaService.AbrirCaixaLocalAsync(1, DateOnly.FromDateTime(DateTime.Now), 1, 10m);

        var shell = new ShellViewModel(
            new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, protetor), protetor),
            new LoginOperadorService(fixture.CriarContexto),
            caixaService,
            new DashboardService(fixture.CriarContexto),
            new VendaService(fixture.CriarContexto),
            new CatalogoLocalService(fixture.CriarContexto),
            new VendaLocalService(fixture.CriarContexto),
            new CadastroLocalService(fixture.CriarContexto));
        await shell.IniciarAsync();
        return shell;
    }

    private static async Task LogarAsync(ShellViewModel shell)
    {
        var login = (LoginViewModel)shell.CurrentViewModel!;
        login.OperadorSelecionado = login.Operadores[0];
        login.PdvKeyDigitada = "1234";
        var chegou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await login.EntrarCommand.Execute();
        await chegou;
    }

    private static bool PodeSair(ShellViewModel shell)
    {
        var pode = false;
        shell.SairCommand.CanExecute.Subscribe(v => pode = v);
        return pode;
    }

    [Fact]
    public async Task SairFicaDesabilitadoAntesDoLogin()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellNoLoginAsync(fixture);

        Assert.Equal(Tela.Login, shell.TelaAtual);
        Assert.False(PodeSair(shell));
    }

    [Fact]
    public async Task SairVoltaAoLoginSemOperadorNemCaixaNaTela()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellNoLoginAsync(fixture);
        await LogarAsync(shell);
        Assert.NotNull(shell.OperadorLogado);
        Assert.NotNull(shell.CaixaAberto);
        Assert.True(PodeSair(shell));

        await shell.SairCommand.Execute();

        Assert.Equal(Tela.Login, shell.TelaAtual);
        var login = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.Null(shell.OperadorLogado);
        Assert.Null(shell.CaixaAberto);
        Assert.False(PodeSair(shell));                                   // sem operador, nada de Sair
        Assert.Equal(new[] { "Carlos" }, login.Operadores.Select(o => o.Nome));   // a lista já vem carregada
        Assert.Null(login.OperadorSelecionado);                          // e o próximo operador começa do zero
        Assert.Equal(string.Empty, login.PdvKeyDigitada);
    }

    [Fact]
    public async Task SairNaoFechaOCaixaEOOperadorOReencontraAoEntrarDeNovo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellNoLoginAsync(fixture);
        await LogarAsync(shell);
        var caixaId = shell.CaixaAberto!.Id;

        await shell.SairCommand.Execute();

        await using (var leitura = fixture.CriarContexto())
            Assert.Equal(StatusCaixa.Aberto, leitura.Caixas.Single().Status);   // nada foi fechado

        await LogarAsync(shell);
        Assert.Equal(caixaId, shell.CaixaAberto!.Id);
        Assert.Equal("Carlos", shell.OperadorLogado!.Nome);
    }

    [Fact]
    public async Task SairFechaOPainelDaFilaOutbox()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellNoLoginAsync(fixture);
        await LogarAsync(shell);
        await shell.AbrirPainelOutboxCommand.Execute();
        Assert.True(shell.PainelOutboxAberto);

        await shell.SairCommand.Execute();

        Assert.False(shell.PainelOutboxAberto);
    }

    [Fact]
    public async Task SairFicaBloqueadoComVendaEmAndamentoELiberaAoCancelar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellNoLoginAsync(fixture);
        await LogarAsync(shell);
        var chegouNoPdv = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.Pdv).FirstAsync().ToTask();
        await shell.IrParaPdvCommand.Execute();
        await chegouNoPdv;
        var pdv = (PdvViewModel)shell.CurrentViewModel!;

        var bloqueou = shell.SairCommand.CanExecute.Where(v => !v).FirstAsync().ToTask();
        pdv.AdicionarItem(new Produto { Nome = "Refrigerante", PrecoVenda = 9.90m });
        await bloqueou;
        Assert.False(PodeSair(shell));

        var liberou = shell.SairCommand.CanExecute.Where(v => v).FirstAsync().ToTask();
        await pdv.CancelarCommand.Execute();
        await liberou;
        Assert.True(PodeSair(shell));
    }
}

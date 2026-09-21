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

// Fechar caixa no Shell (Task 51): botão na barra, tela de conferência e o que vem depois de fechar.
[SupportedOSPlatform("windows")]
public class ShellFecharCaixaTests
{
    private static async Task<ShellViewModel> LogarComCaixaAbertoAsync(SqliteInMemoryFixture fixture, bool exigirAberturaCaixa)
    {
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ExigirAberturaCaixa = exigirAberturaCaixa,
            });
            var funcionario = new Funcionario { Nome = "Carlos", PdvKeyHash = PdvKeyTeste.Hash("1234") };
            context.Funcionarios.Add(funcionario);
            await context.SaveChangesAsync();
        }

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        var protetor = new SegredoProtector();
        var caixaService = new CaixaService(fixture.CriarContexto);
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
        var login = (LoginViewModel)shell.CurrentViewModel!;
        login.OperadorSelecionado = login.Operadores[0];
        login.PdvKeyDigitada = "1234";
        var chegou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await login.EntrarCommand.Execute();
        await chegou;
        return shell;
    }

    private static async Task IrParaFecharCaixaAsync(ShellViewModel shell)
    {
        var chegou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.FecharCaixa).FirstAsync().ToTask();
        await shell.IrParaFecharCaixaCommand.Execute();
        await chegou;
    }

    [Fact]
    public async Task BotaoFecharCaixaLevaATelaDeConferencia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await LogarComCaixaAbertoAsync(fixture, exigirAberturaCaixa: true);

        await IrParaFecharCaixaAsync(shell);

        Assert.IsType<FecharCaixaViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task FecharComExigenciaDeAberturaVoltaParaAbrirCaixaESemCaixaAberto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await LogarComCaixaAbertoAsync(fixture, exigirAberturaCaixa: true);
        await IrParaFecharCaixaAsync(shell);
        var fechar = (FecharCaixaViewModel)shell.CurrentViewModel!;

        var saiu = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.FecharCaixa).FirstAsync().ToTask();
        await fechar.ConfirmarCommand.Execute();
        await saiu;

        Assert.Null(shell.CaixaAberto);
        Assert.Equal(Tela.AbrirCaixa, shell.TelaAtual);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(StatusCaixa.Fechado, leitura.Caixas.Single().Status);
    }

    [Fact]
    public async Task FecharSemExigenciaDeAberturaVaiParaODashboardSemCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await LogarComCaixaAbertoAsync(fixture, exigirAberturaCaixa: false);
        await IrParaFecharCaixaAsync(shell);
        var fechar = (FecharCaixaViewModel)shell.CurrentViewModel!;

        var saiu = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.FecharCaixa).FirstAsync().ToTask();
        await fechar.ConfirmarCommand.Execute();
        await saiu;

        Assert.Null(shell.CaixaAberto);
        Assert.Equal(Tela.Dashboard, shell.TelaAtual);
    }

    [Fact]
    public async Task CancelarVoltaAoDashboardComOCaixaAindaAberto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await LogarComCaixaAbertoAsync(fixture, exigirAberturaCaixa: true);
        await IrParaFecharCaixaAsync(shell);
        var fechar = (FecharCaixaViewModel)shell.CurrentViewModel!;

        var saiu = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.FecharCaixa).FirstAsync().ToTask();
        await fechar.CancelarCommand.Execute();
        await saiu;

        Assert.NotNull(shell.CaixaAberto);
        Assert.Equal(Tela.Dashboard, shell.TelaAtual);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(StatusCaixa.Aberto, leitura.Caixas.Single().Status);
    }

    [Fact]
    public async Task FecharCaixaFicaDesabilitadoSemCaixaAbertoEDuranteUmaVenda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await LogarComCaixaAbertoAsync(fixture, exigirAberturaCaixa: false);
        var pode = false;
        using var inscricao = shell.IrParaFecharCaixaCommand.CanExecute.Subscribe(v => pode = v);
        Assert.True(pode);   // com caixa aberto e sem venda em andamento

        var bloqueou = shell.IrParaFecharCaixaCommand.CanExecute.Where(v => !v).FirstAsync().ToTask();
        var chegouNoPdv = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.Pdv).FirstAsync().ToTask();
        await shell.IrParaPdvCommand.Execute();
        await chegouNoPdv;
        ((PdvViewModel)shell.CurrentViewModel!).AdicionarItem(new Produto { Nome = "Refrigerante", PrecoVenda = 9.90m });
        await bloqueou;

        Assert.False(pode);   // fechar o caixa com carrinho cheio descartaria a venda
    }

    [Fact]
    public async Task DepoisDeFecharATelaDeAberturaJaSugereOProximoTurnoLivre()
    {
        // Reproduz o que o usuário viu: fechou o turno 1, voltou pra "Abrir caixa" e a tela sugeria "Turno 1" de novo.
        using var fixture = new SqliteInMemoryFixture();
        var shell = await LogarComCaixaAbertoAsync(fixture, exigirAberturaCaixa: true);
        await IrParaFecharCaixaAsync(shell);
        var fechar = (FecharCaixaViewModel)shell.CurrentViewModel!;
        var saiu = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.AbrirCaixa).FirstAsync().ToTask();
        await fechar.ConfirmarCommand.Execute();
        await saiu;
        var abrir = (AbrirCaixaViewModel)shell.CurrentViewModel!;

        Assert.Equal(2, abrir.Turno);   // já carregado quando a tela aparece: sem piscar "Turno 1"
    }
}

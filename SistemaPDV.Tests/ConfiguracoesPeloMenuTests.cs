using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Versioning;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Configurações depois do vínculo: antes só abria sozinha no 1º uso (dispositivo sem link), então "Código deste PDV",
// "Exigir abertura de caixa" e o Consumidor Final ficavam inalcançáveis. Agora há um botão na barra (qualquer tela
// logada, sem exigir caixa aberto) e a tela só libera o formulário com a chave de um supervisor.
[SupportedOSPlatform("windows")]
public class ConfiguracoesPeloMenuTests
{
    private const string ChaveSupervisor = "9999";
    private const string ChaveOperador = "1234";
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static int chamadasHttp;

    private static ConfiguracaoService CriarConfiguracaoService(SqliteInMemoryFixture fixture)
    {
        var segredoProtector = new SegredoProtector();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            Interlocked.Increment(ref chamadasHttp);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        return new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, segredoProtector), segredoProtector);
    }

    private static async Task SemearAsync(SqliteInMemoryFixture fixture, bool exigirAberturaCaixa = true)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi, ExigirAberturaCaixa = exigirAberturaCaixa });
        context.Funcionarios.Add(new Funcionario { Nome = "Carlos", IdExterno = 2, PdvKeyHash = PdvKeyTeste.Hash(ChaveOperador) });
        context.Funcionarios.Add(new Funcionario { Nome = "Chefe", IdExterno = 3, Supervisor = true, PdvKeyHash = PdvKeyTeste.Hash(ChaveSupervisor) });
        await context.SaveChangesAsync();
    }

    // ---- a tela (ViewModel) ----

    private static async Task<ConfiguracoesViewModel> AbrirBloqueadaAsync(SqliteInMemoryFixture fixture)
    {
        await SemearAsync(fixture);
        var viewModel = new ConfiguracoesViewModel(CriarConfiguracaoService(fixture), exigirSupervisor: true);
        await viewModel.IniciarAsync();
        return viewModel;
    }

    [Fact]
    public async Task AbertaPeloMenuComecaBloqueadaEComVoltar()
    {
        using var fixture = new SqliteInMemoryFixture();

        var viewModel = await AbrirBloqueadaAsync(fixture);

        Assert.True(viewModel.Bloqueada);
        Assert.False(viewModel.Liberada);
        Assert.True(viewModel.PodeVoltar);
    }

    [Fact]
    public async Task NaPrimeiraVinculacaoAbreDiretoSemChaveESemVoltar()
    {
        using var fixture = new SqliteInMemoryFixture();

        var viewModel = new ConfiguracoesViewModel(CriarConfiguracaoService(fixture));   // sem exigirSupervisor

        Assert.True(viewModel.Liberada);
        Assert.False(viewModel.PodeVoltar);   // não há para onde voltar: ainda nem existe login
    }

    [Fact]
    public async Task LiberarExigeDigitarAChave()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await AbrirBloqueadaAsync(fixture);
        var pode = true;
        using var inscricao = viewModel.DesbloquearCommand.CanExecute.Subscribe(v => pode = v);
        Assert.False(pode);

        viewModel.ChaveSupervisor = "9";

        Assert.True(pode);
    }

    [Fact]
    public async Task ChaveDeSupervisorLiberaOFormulario()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await AbrirBloqueadaAsync(fixture);
        viewModel.ChaveSupervisor = ChaveSupervisor;

        await viewModel.DesbloquearCommand.Execute();

        Assert.False(viewModel.Bloqueada);
        Assert.True(viewModel.Liberada);
        Assert.Null(viewModel.MensagemDesbloqueio);
        Assert.Equal(string.Empty, viewModel.ChaveSupervisor);   // a chave nunca fica no formulário
    }

    [Theory]
    [InlineData("0000")]          // chave que não é de ninguém
    [InlineData(ChaveOperador)]   // chave de um operador comum: não é supervisor
    public async Task ChaveErradaOuDeOperadorNaoLiberaEDaAMesmaMensagem(string chave)
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await AbrirBloqueadaAsync(fixture);
        viewModel.ChaveSupervisor = chave;

        await viewModel.DesbloquearCommand.Execute();

        Assert.True(viewModel.Bloqueada);
        Assert.Contains("supervisor", viewModel.MensagemDesbloqueio, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, viewModel.ChaveSupervisor);   // limpa mesmo quando erra
    }

    [Fact]
    public async Task SupervisorDesativadoNaoLibera()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await AbrirBloqueadaAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            context.Funcionarios.Single(f => f.Supervisor).Desativado = true;
            await context.SaveChangesAsync();
        }
        viewModel.ChaveSupervisor = ChaveSupervisor;

        await viewModel.DesbloquearCommand.Execute();

        Assert.True(viewModel.Bloqueada);
    }

    [Fact]
    public async Task BloqueadaNaoSalvaMesmoSeOComandoForAcionadoDiretamente()
    {
        // Execute() ignora o CanExecute e o botão escondido não é barreira: a regra vive no próprio ViewModel.
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await AbrirBloqueadaAsync(fixture);
        viewModel.CodigoPdv = "07";
        viewModel.ExigirAberturaCaixa = false;

        await viewModel.SalvarCommand.Execute();

        Assert.Contains("supervisor", viewModel.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        var salvo = leitura.ConfiguracoesSincronizacao.Single();
        Assert.Null(salvo.CodigoPdv);
        Assert.True(salvo.ExigirAberturaCaixa);
    }

    [Fact]
    public async Task BloqueadaNaoVinculaNemChamaARede()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await AbrirBloqueadaAsync(fixture);
        viewModel.LinkCadastro = "https://outra-empresa.softcomshop.com.br/registrar?client_id=9";
        viewModel.NomeDispositivo = "PDV 2";
        var antes = Volatile.Read(ref chamadasHttp);

        var vinculou = await viewModel.VincularCommand.Execute();

        Assert.False(vinculou);
        Assert.Equal(antes, Volatile.Read(ref chamadasHttp));   // nenhuma chamada à API
    }

    [Fact]
    public async Task DepoisDeLiberadaSalvaNormalmente()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await AbrirBloqueadaAsync(fixture);
        viewModel.ChaveSupervisor = ChaveSupervisor;
        await viewModel.DesbloquearCommand.Execute();
        viewModel.CodigoPdv = "07";

        await viewModel.SalvarCommand.Execute();

        using var leitura = fixture.CriarContexto();
        Assert.Equal("07", leitura.ConfiguracoesSincronizacao.Single().CodigoPdv);
    }

    // ---- o botão da barra (Shell) ----

    private static ShellViewModel CriarShell(SqliteInMemoryFixture fixture, ConfiguracaoService? configuracaoService = null) => new(
        configuracaoService ?? CriarConfiguracaoService(fixture),
        new LoginOperadorService(fixture.CriarContexto),
        new CaixaService(fixture.CriarContexto),
        new DashboardService(fixture.CriarContexto),
        new VendaService(fixture.CriarContexto),
        new CatalogoLocalService(fixture.CriarContexto),
        new VendaLocalService(fixture.CriarContexto),
        new CadastroLocalService(fixture.CriarContexto));

    private static async Task LogarComoOperadorAsync(ShellViewModel shell)
    {
        await shell.IniciarAsync();
        var login = (LoginViewModel)shell.CurrentViewModel!;
        login.PdvKeyDigitada = ChaveOperador;
        var saiuDoLogin = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await login.EntrarCommand.Execute();
        await saiuDoLogin;
    }

    [Fact]
    public async Task BotaoFicaDesabilitadoAntesDoLogin()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var shell = CriarShell(fixture);
        await shell.IniciarAsync();
        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);

        Assert.False(await shell.IrParaConfiguracoesCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task BotaoFicaHabilitadoDepoisDoLoginMesmoSemCaixaAberto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture, exigirAberturaCaixa: true);
        var shell = CriarShell(fixture);
        await LogarComoOperadorAsync(shell);

        Assert.Equal(Tela.AbrirCaixa, shell.TelaAtual);
        Assert.Null(shell.CaixaAberto);
        // O código do PDV precisa ser definido ANTES de vender: o botão não espera o caixa abrir (diferente de Cadastros).
        Assert.True(await shell.IrParaConfiguracoesCommand.CanExecute.FirstAsync());
        Assert.False(await shell.IrParaCadastrosCommand.CanExecute.FirstAsync());
    }

    [Fact]
    public async Task BotaoAbreAsConfiguracoesBloqueadas()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var shell = CriarShell(fixture);
        await LogarComoOperadorAsync(shell);

        await shell.IrParaConfiguracoesCommand.Execute();

        Assert.Equal(Tela.Configuracoes, shell.TelaAtual);
        var configuracoes = Assert.IsType<ConfiguracoesViewModel>(shell.CurrentViewModel);
        Assert.True(configuracoes.Bloqueada);
        Assert.True(configuracoes.PodeVoltar);
    }

    [Fact]
    public async Task VoltarSemCaixaAbertoLevaDeNovoParaAbrirCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture, exigirAberturaCaixa: true);
        var shell = CriarShell(fixture);
        await LogarComoOperadorAsync(shell);
        await shell.IrParaConfiguracoesCommand.Execute();
        var configuracoes = (ConfiguracoesViewModel)shell.CurrentViewModel!;

        var voltou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.AbrirCaixa).FirstAsync().ToTask();
        await configuracoes.VoltarCommand.Execute();
        await voltou;

        Assert.IsType<AbrirCaixaViewModel>(shell.CurrentViewModel);
        Assert.NotNull(shell.OperadorLogado);   // continua logado
    }

    [Fact]
    public async Task VoltarSemExigenciaDeCaixaLevaAoDashboard()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture, exigirAberturaCaixa: false);
        var shell = CriarShell(fixture);
        await LogarComoOperadorAsync(shell);
        await shell.IrParaConfiguracoesCommand.Execute();
        var configuracoes = (ConfiguracoesViewModel)shell.CurrentViewModel!;

        var voltou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.Dashboard).FirstAsync().ToTask();
        await configuracoes.VoltarCommand.Execute();
        await voltou;

        Assert.IsType<DashboardViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task ReVincularPeloMenuDerrubaOLoginEOCaixaEVoltaAoLogin()
    {
        // Um novo vínculo pode ser de OUTRA empresa: o operador que estava logado e o caixa aberto deixam de valer.
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture, exigirAberturaCaixa: false);
        int operadorId;
        await using (var context = fixture.CriarContexto())
            operadorId = context.Funcionarios.Single(f => !f.Supervisor).Id;
        await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 20), 1, 10m);

        var segredoProtector = new SegredoProtector();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": { "client_secret": "abc" } }""") });
        var configuracaoService = new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, segredoProtector), segredoProtector);
        var shell = CriarShell(fixture, configuracaoService);
        await LogarComoOperadorAsync(shell);
        Assert.NotNull(shell.CaixaAberto);   // entrou já com o caixa aberto
        await shell.IrParaConfiguracoesCommand.Execute();
        var configuracoes = (ConfiguracoesViewModel)shell.CurrentViewModel!;
        configuracoes.ChaveSupervisor = ChaveSupervisor;
        await configuracoes.DesbloquearCommand.Execute();
        configuracoes.LinkCadastro = "https://outra-empresa.softcomshop.com.br/registrar?client_id=9";
        configuracoes.NomeDispositivo = "PDV 2";

        var chegouNoLogin = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.Login).FirstAsync().ToTask();
        await configuracoes.VincularCommand.Execute();
        await chegouNoLogin;

        Assert.Null(shell.OperadorLogado);
        Assert.Null(shell.CaixaAberto);
        Assert.False(await shell.IrParaConfiguracoesCommand.CanExecute.FirstAsync());   // sem login de novo, sem o botão
    }

    // ---- re-vincular a OUTRA empresa com dados locais ----

    private const string LinkEmpresaA = "https://exemplo.softcomshop.com.br/registrar?client_id=1&empresa_cnpj=12345678000199";
    private const string LinkEmpresaB = "https://exemplo.softcomshop.com.br/registrar?client_id=2&empresa_cnpj=06220266000126";

    private static async Task<(ConfiguracaoService Servico, Func<int> Chamadas)> PrepararReVinculoAsync(SqliteInMemoryFixture fixture, bool comCaixaLocal)
    {
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = LinkEmpresaA });
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            context.Funcionarios.Add(operador);
            await context.SaveChangesAsync();
            if (comCaixaLocal)
                await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operador.Id, new DateOnly(2026, 9, 20), 1, 10m);
        }

        var chamadas = 0;
        var segredoProtector = new SegredoProtector();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": { "client_secret": "abc" } }""") };
        });
        return (new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, segredoProtector), segredoProtector), () => chamadas);
    }

    [Fact]
    public async Task ReVincularAOutraEmpresaComCaixaLocalEhRecusadoSemChamarARede()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (servico, chamadas) = await PrepararReVinculoAsync(fixture, comCaixaLocal: true);

        var (sucesso, mensagem) = await servico.VincularDispositivoAsync(LinkEmpresaB, "PDV-01");

        Assert.False(sucesso);
        Assert.Contains("outra empresa", mensagem);
        Assert.Equal(0, chamadas());
        using var leitura = fixture.CriarContexto();
        Assert.Equal(LinkEmpresaA, leitura.ConfiguracoesSincronizacao.Single().UrlApi);   // nada mudou
    }

    [Fact]
    public async Task ReVincularAOutraEmpresaSemNenhumDadoLocalEhPermitido()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (servico, _) = await PrepararReVinculoAsync(fixture, comCaixaLocal: false);

        var (sucesso, mensagem) = await servico.VincularDispositivoAsync(LinkEmpresaB, "PDV-01");

        Assert.True(sucesso, mensagem);
    }

    [Fact]
    public async Task ReVincularAMesmaEmpresaComCaixaLocalContinuaLivre()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (servico, _) = await PrepararReVinculoAsync(fixture, comCaixaLocal: true);

        var (sucesso, mensagem) = await servico.VincularDispositivoAsync(LinkEmpresaA.Replace("client_id=1", "client_id=5"), "PDV-01");

        Assert.True(sucesso, mensagem);   // mesmo CNPJ (ex: novo segredo): não mistura nada
    }

    [Fact]
    public async Task LinkSemCnpjNaoBloqueiaPorqueNaoDaParaProvarADiferenca()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (servico, _) = await PrepararReVinculoAsync(fixture, comCaixaLocal: true);

        var (sucesso, mensagem) = await servico.VincularDispositivoAsync("https://exemplo.softcomshop.com.br/registrar?client_id=9", "PDV-01");

        Assert.True(sucesso, mensagem);
    }
}

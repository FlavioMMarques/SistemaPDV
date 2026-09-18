using System;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.ViewModels;

// Casca de navegação: decide qual tela mostrar (Configurações -> Login ->
// AbrirCaixa/Dashboard) e guarda o estado que o header precisa (operador logado,
// caixa aberto). DashboardViewModel ainda não existe (Task 46) — a decisão de que
// é hora de mostrá-lo já está completa e testável via TelaAtual, só a instância
// real de CurrentViewModel fica pendente até lá.
public class ShellViewModel : ViewModelBase
{
    private readonly ConfiguracaoService configuracaoService;
    private readonly LoginOperadorService loginOperadorService;
    private readonly CaixaService caixaService;

    private Tela telaAtual;
    private ViewModelBase? currentViewModel;
    private Funcionario? operadorLogado;
    private Models.Caixa? caixaAberto;

    public ShellViewModel(ConfiguracaoService configuracaoService, LoginOperadorService loginOperadorService, CaixaService caixaService)
    {
        this.configuracaoService = configuracaoService;
        this.loginOperadorService = loginOperadorService;
        this.caixaService = caixaService;
    }

    public Tela TelaAtual
    {
        get => telaAtual;
        private set => this.RaiseAndSetIfChanged(ref telaAtual, value);
    }

    public ViewModelBase? CurrentViewModel
    {
        get => currentViewModel;
        private set => this.RaiseAndSetIfChanged(ref currentViewModel, value);
    }

    public Funcionario? OperadorLogado
    {
        get => operadorLogado;
        private set => this.RaiseAndSetIfChanged(ref operadorLogado, value);
    }

    public Models.Caixa? CaixaAberto
    {
        get => caixaAberto;
        private set => this.RaiseAndSetIfChanged(ref caixaAberto, value);
    }

    // Marcado aqui (não a classe inteira) porque IniciarAsync é o único caminho que
    // pode chegar em IrParaConfiguracoes, que constrói ConfiguracoesViewModel
    // (Windows-only por causa do vínculo de dispositivo via DPAPI).
    [SupportedOSPlatform("windows")]
    public async Task IniciarAsync()
    {
        var configuracao = await configuracaoService.ObterOuCriarAsync();

        // Sem UrlApi, o dispositivo nunca foi vinculado — não tem como logar nem
        // sincronizar nada ainda, então Configurações vem antes até do Login.
        if (string.IsNullOrWhiteSpace(configuracao.UrlApi))
        {
            IrParaConfiguracoes();
            return;
        }

        IrParaLogin();
    }

    [SupportedOSPlatform("windows")]
    private void IrParaConfiguracoes()
    {
        var viewModel = new ConfiguracoesViewModel(configuracaoService);
        TelaAtual = Tela.Configuracoes;
        CurrentViewModel = viewModel;
        _ = viewModel.IniciarAsync();
    }

    private void IrParaLogin()
    {
        var viewModel = new LoginViewModel(loginOperadorService);

        // EntrarCommand é o próprio IObservable<Funcionario?> do comando (ver
        // docs/APRENDIZADOS.md #26) — Shell escuta sem LoginViewModel precisar
        // conhecer Shell.
        viewModel.EntrarCommand
            .Where(funcionario => funcionario is not null)
            .Subscribe(funcionario => _ = AposLoginAsync(funcionario!));

        TelaAtual = Tela.Login;
        CurrentViewModel = viewModel;
    }

    private async Task AposLoginAsync(Funcionario funcionario)
    {
        OperadorLogado = funcionario;

        var configuracao = await configuracaoService.ObterOuCriarAsync();
        CaixaAberto = await caixaService.ObterCaixaAbertoAsync(funcionario.Id);

        if (CaixaAberto is not null || !configuracao.ExigirAberturaCaixa)
        {
            IrParaDashboard();
            return;
        }

        IrParaAbrirCaixa(funcionario.Id);
    }

    private void IrParaAbrirCaixa(int funcionarioId)
    {
        var viewModel = new AbrirCaixaViewModel(caixaService, funcionarioId);

        viewModel.AbrirCommand
            .Where(caixa => caixa is not null)
            .Subscribe(caixa =>
            {
                CaixaAberto = caixa;
                IrParaDashboard();
            });

        TelaAtual = Tela.AbrirCaixa;
        CurrentViewModel = viewModel;
    }

    private void IrParaDashboard()
    {
        // TODO (Task 46): trocar por um DashboardViewModel real quando ele existir.
        TelaAtual = Tela.Dashboard;
        CurrentViewModel = null;
    }
}

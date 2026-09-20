using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.ViewModels;

// Casca de navegação: decide qual tela mostrar (Configurações -> Login ->
// AbrirCaixa/Dashboard/Pdv) e guarda o estado que o header precisa (operador
// logado, caixa aberto).
public class ShellViewModel : ViewModelBase
{
    private readonly ConfiguracaoService configuracaoService;
    private readonly LoginOperadorService loginOperadorService;
    private readonly CaixaService caixaService;
    private readonly DashboardService dashboardService;
    private readonly VendaService vendaService;
    private readonly CatalogoLocalService catalogoLocalService;
    private readonly VendaLocalService vendaLocalService;
    private readonly CadastroLocalService cadastroLocalService;

    private Tela telaAtual;
    private ViewModelBase? currentViewModel;
    private Funcionario? operadorLogado;
    private Models.Caixa? caixaAberto;
    private string? mensagem;
    private bool vendaEmAndamento;
    private EstadoConexao conexao;
    private string? detalheConexao;
    private readonly Subject<Unit> dispositivoVinculado = new();

    public ShellViewModel(
        ConfiguracaoService configuracaoService,
        LoginOperadorService loginOperadorService,
        CaixaService caixaService,
        DashboardService dashboardService,
        VendaService vendaService,
        CatalogoLocalService catalogoLocalService,
        VendaLocalService vendaLocalService,
        CadastroLocalService cadastroLocalService)
    {
        this.configuracaoService = configuracaoService;
        this.loginOperadorService = loginOperadorService;
        this.caixaService = caixaService;
        this.dashboardService = dashboardService;
        this.vendaService = vendaService;
        this.catalogoLocalService = catalogoLocalService;
        this.vendaLocalService = vendaLocalService;
        this.cadastroLocalService = cadastroLocalService;

        // Navegação persistente entre as 3 telas pós-caixa-aberto (Dashboard/Pdv/
        // ListaPedidos) — só habilitada com CaixaAberto preenchido, já que nenhuma
        // delas faz sentido sem caixa. Resolve o que ficou pendente nas Tasks 42/43
        // ("navegação entre as telas sem menu"), pelo menos pra essas três (Cadastros,
        // abaixo, tem a própria regra).
        //
        // Também bloqueada enquanto há venda em andamento (decisão do usuário,
        // 2026-09-20): cada IrPara* cria um ViewModel novo, então sair da tela de
        // venda com o carrinho cheio — ou clicar em "Nova Venda" estando nela —
        // descartaria o que o operador já montou. Obriga a finalizar ou cancelar.
        var temCaixaAberto = this.WhenAnyValue(vm => vm.CaixaAberto).Select(caixa => caixa is not null);
        var semVendaEmAndamento = this.WhenAnyValue(vm => vm.VendaEmAndamento).Select(emAndamento => !emAndamento);
        var podeNavegar = temCaixaAberto.CombineLatest(semVendaEmAndamento, (temCaixa, semVenda) => temCaixa && semVenda);
        IrParaDashboardCommand = ReactiveCommand.CreateFromTask(IrParaDashboardAsync, podeNavegar);
        IrParaPdvCommand = ReactiveCommand.CreateFromTask(() => IrParaPdvAsync(CaixaAberto!.Id), podeNavegar);
        IrParaListaPedidosCommand = ReactiveCommand.CreateFromTask(() => IrParaListaPedidosAsync(CaixaAberto!.Id), podeNavegar);
        // Fechar caixa: mesma regra de navegação (precisa de caixa aberto; bloqueado com venda em andamento —
        // fechar com o carrinho cheio descartaria a venda).
        IrParaFecharCaixaCommand = ReactiveCommand.CreateFromTask(() => IrParaFecharCaixaAsync(CaixaAberto!.Id), podeNavegar);

        // Cadastros não depende de caixa (é só leitura/cadastro local), só de haver operador logado — assim continua
        // acessível no Dashboard com ExigirAberturaCaixa desligado e sem caixa aberto. Mesmo bloqueio de venda em
        // andamento (descartaria o carrinho). E não enquanto a tela de ABRIR CAIXA está aberta: com a abertura exigida o fluxo obriga a abrir o caixa antes
        // de qualquer outra coisa (o botão ficava ativo com o operador ainda escolhendo o turno).
        var podeEntrarEmCadastros = this.WhenAnyValue(vm => vm.OperadorLogado, vm => vm.TelaAtual,
            (operador, tela) => operador is not null && tela != Tela.AbrirCaixa);
        var podeAbrirCadastros = podeEntrarEmCadastros.CombineLatest(semVendaEmAndamento, (podeEntrar, semVenda) => podeEntrar && semVenda);
        IrParaCadastrosCommand = ReactiveCommand.CreateFromTask(IrParaCadastrosAsync, podeAbrirCadastros);
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

    // Sem infraestrutura de log ainda no projeto — pelo menos isso dá pra tela
    // mostrar alguma coisa quando ExecutarComTratamentoDeErroAsync captura uma
    // falha, em vez de travar em silêncio (ver o método).
    public string? Mensagem
    {
        get => mensagem;
        private set => this.RaiseAndSetIfChanged(ref mensagem, value);
    }

    public bool VendaEmAndamento
    {
        get => vendaEmAndamento;
        private set => this.RaiseAndSetIfChanged(ref vendaEmAndamento, value);
    }

    // Indicador de conexão do header (Task 50): quem observa o SincronizacaoBackgroundService
    // (App) chama DefinirConexao na thread de UI — o Shell não conhece o serviço.
    public EstadoConexao Conexao
    {
        get => conexao;
        private set => this.RaiseAndSetIfChanged(ref conexao, value);
    }

    // Dica do indicador: por que ficou offline (ex: URL sem HTTPS), qual recurso falhou, ou o
    // que o último catálogo trouxe.
    public string? DetalheConexao
    {
        get => detalheConexao;
        private set => this.RaiseAndSetIfChanged(ref detalheConexao, value);
    }

    // Emite quando o dispositivo acabou de ser vinculado com sucesso (App liga isso ao
    // serviço de sincronização pra rodar já, sem esperar os 30 s).
    public IObservable<Unit> DispositivoVinculado => dispositivoVinculado;

    // Um ciclo de sincronização mexeu no banco: se a tela aberta lista esses dados,
    // recarrega. Chamar na thread de UI (o App faz o Post).
    public void NotificarDadosSincronizados()
    {
        if (CurrentViewModel is IAtualizavelPorSincronizacao tela)
            _ = ExecutarComTratamentoDeErroAsync(tela.AtualizarAposSincronizacaoAsync);
    }

    // Erro inesperado de um comando (ver TratamentoDeErros): vira o banner em vez de derrubar o app.
    // Chamar na thread de UI (o App faz o Post).
    public void ReportarErro(Exception excecao) => Mensagem = $"Ocorreu um erro inesperado: {excecao.Message}";

    public void DefinirConexao(EstadoConexao estado, string? detalhe)
    {
        DetalheConexao = estado == EstadoConexao.Desconhecida ? null : detalhe;
        Conexao = estado;
    }

    public ReactiveCommand<Unit, Unit> IrParaDashboardCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaPdvCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaListaPedidosCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaCadastrosCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaFecharCaixaCommand { get; }

    // Marcado aqui (não a classe inteira) porque IniciarAsync é o único caminho que
    // pode chegar em IrParaConfiguracoes, que constrói ConfiguracoesViewModel
    // (Windows-only por causa do vínculo de dispositivo via DPAPI).
    [SupportedOSPlatform("windows")]
    public async Task IniciarAsync() => await ExecutarComTratamentoDeErroAsync(async () =>
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
    });

    [SupportedOSPlatform("windows")]
    private void IrParaConfiguracoes()
    {
        var viewModel = new ConfiguracoesViewModel(configuracaoService);

        // Vincular com sucesso é o que destrava o resto (login, sincronização): leva ao
        // Login sem reabrir o app e avisa quem quiser sincronizar já, em vez de esperar o
        // próximo tick do timer — sem funcionários locais a chave do operador não passa.
        viewModel.VincularCommand
            .Where(vinculou => vinculou)
            .Subscribe(_ =>
            {
                // Avisa ANTES de navegar: quem espera a troca de tela já encontra o aviso feito.
                dispositivoVinculado.OnNext(Unit.Default);
                IrParaLogin();
            });
        // CurrentViewModel antes de TelaAtual de propósito: quem observa TelaAtual
        // (ex: um teste com WhenAnyValue) só deve acordar depois que o resto do
        // estado da navegação já está pronto — inverter a ordem cria uma corrida
        // onde TelaAtual muda mas CurrentViewModel ainda é o da tela anterior.
        CurrentViewModel = viewModel;
        TelaAtual = Tela.Configuracoes;
        _ = ExecutarComTratamentoDeErroAsync(viewModel.IniciarAsync);
    }

    private void IrParaLogin()
    {
        var viewModel = new LoginViewModel(loginOperadorService);

        // EntrarCommand é o próprio IObservable<Funcionario?> do comando (ver
        // docs/APRENDIZADOS.md #26) — Shell escuta sem LoginViewModel precisar
        // conhecer Shell.
        viewModel.EntrarCommand
            .Where(funcionario => funcionario is not null)
            .Subscribe(funcionario => _ = ExecutarComTratamentoDeErroAsync(() => AposLoginAsync(funcionario!)));

        CurrentViewModel = viewModel;
        TelaAtual = Tela.Login;
    }

    private async Task AposLoginAsync(Funcionario funcionario)
    {
        OperadorLogado = funcionario;

        var configuracao = await configuracaoService.ObterOuCriarAsync();
        CaixaAberto = await caixaService.ObterCaixaAbertoAsync(funcionario.Id);

        if (CaixaAberto is not null || !configuracao.ExigirAberturaCaixa)
        {
            await IrParaDashboardAsync();
            return;
        }

        await IrParaAbrirCaixaAsync(funcionario.Id);
    }

    // Carrega ANTES de mostrar a tela (e não em segundo plano depois): o turno sugerido já é o certo quando ela
    // aparece (sem piscar "Turno 1" e trocar) e nada mexe no banco em paralelo com o que o operador faz a seguir.
    private async Task IrParaAbrirCaixaAsync(int funcionarioId)
    {
        var viewModel = new AbrirCaixaViewModel(caixaService, funcionarioId);
        await viewModel.IniciarAsync();   // sugere o 1º turno ainda livre hoje

        viewModel.AbrirCommand
            .Where(caixa => caixa is not null)
            .Subscribe(caixa =>
            {
                CaixaAberto = caixa;
                _ = ExecutarComTratamentoDeErroAsync(IrParaDashboardAsync);
            });

        CurrentViewModel = viewModel;
        TelaAtual = Tela.AbrirCaixa;
    }

    // IrPara*Async: carregam os dados ANTES de mostrar a tela (e não em segundo plano depois). Tela vazia que se enche
    // sozinha é pior pro operador, e o carregamento em paralelo mexia no banco ao mesmo tempo que a ação seguinte.
    private async Task IrParaDashboardAsync()
    {
        var viewModel = new DashboardViewModel(dashboardService, CaixaAberto?.Id);
        await viewModel.IniciarAsync();

        // Nova Venda não navega sozinho — só emite (fica desabilitado sem caixa
        // aberto, ver DashboardViewModel), o Shell decide o que fazer com isso.
        viewModel.NovaVendaCommand.Subscribe(evento => _ = ExecutarComTratamentoDeErroAsync(() => IrParaPdvAsync(CaixaAberto!.Id)));

        CurrentViewModel = viewModel;
        TelaAtual = Tela.Dashboard;
    }

    private async Task IrParaPdvAsync(int caixaId)
    {
        var viewModel = new PdvViewModel(vendaService, catalogoLocalService, caixaId);
        await viewModel.IniciarAsync();
        viewModel.WhenAnyValue(vm => vm.TemVendaEmAndamento).Subscribe(emAndamento => VendaEmAndamento = emAndamento);
        CurrentViewModel = viewModel;
        TelaAtual = Tela.Pdv;
    }

    private async Task IrParaListaPedidosAsync(int caixaId)
    {
        var viewModel = new ListaPedidosViewModel(vendaLocalService, caixaId);
        await viewModel.IniciarAsync();
        CurrentViewModel = viewModel;
        TelaAtual = Tela.ListaPedidos;
    }

    // Também carrega antes de mostrar: com a apuração ainda vazia o operador poderia clicar em "Fechar caixa" e fechar
    // SEM conferir nada (a lista de formas ainda não tinha chegado).
    private async Task IrParaFecharCaixaAsync(int caixaId)
    {
        var viewModel = new FecharCaixaViewModel(caixaService, vendaLocalService, caixaId);
        await viewModel.IniciarAsync();

        // ConfirmarCommand devolve o Caixa fechado (ou null se a regra recusou — a própria tela mostra o motivo).
        viewModel.ConfirmarCommand
            .Where(fechado => fechado is not null)
            .Subscribe(fechado => _ = ExecutarComTratamentoDeErroAsync(AposFecharCaixaAsync));
        viewModel.CancelarCommand.Subscribe(evento => _ = ExecutarComTratamentoDeErroAsync(IrParaDashboardAsync));

        CurrentViewModel = viewModel;
        TelaAtual = Tela.FecharCaixa;
    }

    // Sem caixa aberto não há mais o que fazer no Dashboard/Venda: com ExigirAberturaCaixa, volta pra abrir o próximo
    // (o operador continua logado); sem a exigência, cai no Dashboard (Nova Venda fica desabilitada sem caixa).
    // O fechamento em si só vai à API depois, pelo serviço de sincronização (outbox).
    private async Task AposFecharCaixaAsync()
    {
        CaixaAberto = null;

        var configuracao = await configuracaoService.ObterOuCriarAsync();
        if (configuracao.ExigirAberturaCaixa && OperadorLogado is { } operador)
            await IrParaAbrirCaixaAsync(operador.Id);
        else
            await IrParaDashboardAsync();
    }

    private async Task IrParaCadastrosAsync()
    {
        var viewModel = new CadastrosViewModel(cadastroLocalService);
        await viewModel.IniciarAsync();
        CurrentViewModel = viewModel;
        TelaAtual = Tela.Cadastros;
    }

    // "Fire-and-forget seguro": IrPara* dispara trabalho assíncrono (carregar
    // dados, reagir a login) sem o chamador esperar — sem esse try/catch, qualquer
    // exceção nesse trabalho vira uma task nunca observada e desaparece
    // silenciosamente, travando a tela sem explicação nenhuma (achado numa revisão
    // de código, 2026-09-18). Sem infraestrutura de log ainda no projeto, o mínimo
    // é nunca deixar isso sumir em silêncio — Mensagem ao menos dá pra tela mostrar
    // alguma coisa em vez de nada.
    private async Task ExecutarComTratamentoDeErroAsync(Func<Task> operacao)
    {
        try
        {
            await operacao();
        }
        catch (Exception ex)
        {
            Mensagem = $"Ocorreu um erro inesperado: {ex.Message}";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly CaixasApiService? caixasApiService;   // consulta dos caixas da API (Configurações); opcional para os testes de outras telas
    private readonly CepService? cepService;   // consulta de CEP do modal de cliente; opcional pelo mesmo motivo

    private Tela telaAtual;
    private ViewModelBase? currentViewModel;
    private Funcionario? operadorLogado;
    private Models.Caixa? caixaAberto;
    private string? mensagem;
    private bool vendaEmAndamento;
    private int pendentesSync;
    private EstadoConexao conexao;
    private string? detalheConexao;
    private readonly Subject<Unit> dispositivoVinculado = new();
    private readonly Subject<Unit> sincronizacaoSolicitada = new();

    // A conexão voltou e o operador ainda não foi avisado de que a fila esvaziou (ver AvisarMudancaDeConexao).
    private bool avisarFilaVazia;
    private bool painelOutboxAberto;
    private AndamentoDoEnvio andamentoDoEnvio = AndamentoDoEnvio.Ocioso;
    private PendenciasPorTipo pendencias = new(0, 0, 0, 0);

    // A recontagem disparada ao reconectar roda em segundo plano; quem precisa esperá-la (os testes, antes de soltar o banco)
    // usa isto. Em produção ninguém aguarda.
    public Task RecontagemAposReconexao { get; private set; } = Task.CompletedTask;

    // Avisos temporários no canto da tela (conexão caiu/voltou, item lançado).
    public ToastCentral Toasts { get; } = new();

    // Marcado (como o de ConfiguracoesViewModel) porque IrParaConfiguracoesCommand referencia IrParaConfiguracoesAsync, que
    // constrói o ViewModel de Configurações (Windows-only por causa do vínculo de dispositivo via DPAPI).
    [SupportedOSPlatform("windows")]
    public ShellViewModel(
        ConfiguracaoService configuracaoService,
        LoginOperadorService loginOperadorService,
        CaixaService caixaService,
        DashboardService dashboardService,
        VendaService vendaService,
        CatalogoLocalService catalogoLocalService,
        VendaLocalService vendaLocalService,
        CadastroLocalService cadastroLocalService,
        CaixasApiService? caixasApiService = null,
        CepService? cepService = null)
    {
        this.configuracaoService = configuracaoService;
        this.loginOperadorService = loginOperadorService;
        this.caixaService = caixaService;
        this.dashboardService = dashboardService;
        this.vendaService = vendaService;
        this.catalogoLocalService = catalogoLocalService;
        this.vendaLocalService = vendaLocalService;
        this.cadastroLocalService = cadastroLocalService;
        this.caixasApiService = caixasApiService;
        this.cepService = cepService;

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

        // Cadastros segue a mesma regra dos outros botões (decisão do usuário, 2026-09-20): só com caixa aberto e sem
        // venda em andamento. (Antes exigia só operador logado, e ficava ativo na tela de abrir caixa.)
        IrParaCadastrosCommand = ReactiveCommand.CreateFromTask(IrParaCadastrosAsync, podeNavegar);

        // Configurações: qualquer tela depois do login (não exige caixa aberto — o código do PDV precisa ser definido
        // ANTES de vender), bloqueada só com venda em andamento como as outras. A chave do supervisor é pedida na
        // própria tela (ConfiguracoesViewModel), não aqui.
        var logadoSemVendaEmAndamento = this.WhenAnyValue(vm => vm.OperadorLogado).Select(operador => operador is not null)
            .CombineLatest(semVendaEmAndamento, (logado, semVenda) => logado && semVenda);
        IrParaConfiguracoesCommand = ReactiveCommand.CreateFromTask(() => IrParaConfiguracoesAsync(exigirSupervisor: true), logadoSemVendaEmAndamento);

        // Sair (logoff): volta ao login para trocar de operador. Mesma regra das Configurações — sair com o carrinho cheio
        // descartaria a venda, então fica bloqueado até finalizar ou cancelar.
        SairCommand = ReactiveCommand.CreateFromTask(SairAsync, logadoSemVendaEmAndamento);

        // Painel lateral da fila outbox (abre pela pílula "Sync: N pendentes" da barra do topo). Disponível em qualquer tela
        // depois do login — inclusive sem caixa aberto (a fila pode ter clientes e produtos novos) — e fecha com Esc.
        AbrirPainelOutboxCommand = ReactiveCommand.Create(() => { PainelOutboxAberto = true; });
        FecharPainelOutboxCommand = ReactiveCommand.Create(
            () => { PainelOutboxAberto = false; },
            this.WhenAnyValue(vm => vm.PainelOutboxAberto));
        // Desabilitado enquanto um envio está em curso: um segundo "disparar" não adianta nada (o serviço serializa os ciclos) e o botão
        // parado deixa claro que já está trabalhando.
        SincronizarAgoraCommand = ReactiveCommand.Create(SolicitarSincronizacao, this.WhenAnyValue(vm => vm.Sincronizando).Select(ocupado => !ocupado));
    }

    public Tela TelaAtual
    {
        get => telaAtual;
        private set
        {
            this.RaiseAndSetIfChanged(ref telaAtual, value);
            // A aba da barra do topo que corresponde à tela atual fica destacada (ver as classes "ativa" no ShellView).
            this.RaisePropertyChanged(nameof(DashboardAtivo));
            this.RaisePropertyChanged(nameof(PdvAtivo));
            this.RaisePropertyChanged(nameof(PedidosAtivo));
            this.RaisePropertyChanged(nameof(CadastrosAtivo));
            this.RaisePropertyChanged(nameof(FecharCaixaAtivo));
            this.RaisePropertyChanged(nameof(ConfiguracoesAtivo));
        }
    }

    public bool DashboardAtivo => TelaAtual == Tela.Dashboard;
    public bool PdvAtivo => TelaAtual == Tela.Pdv;
    public bool PedidosAtivo => TelaAtual == Tela.ListaPedidos;
    public bool CadastrosAtivo => TelaAtual == Tela.Cadastros;
    public bool FecharCaixaAtivo => TelaAtual == Tela.FecharCaixa;
    public bool ConfiguracoesAtivo => TelaAtual == Tela.Configuracoes;

    // ---- chip do operador e pílula de sincronização da barra do topo ----

    // "Carlos • Caixa 02" (o número é o do caixa local, com dois dígitos como no protótipo).
    public string RotuloOperadorCaixa => OperadorLogado is null
        ? string.Empty
        : CaixaAberto is null ? OperadorLogado.Nome : $"{OperadorLogado.Nome} • Caixa {CaixaAberto.Id:00}";

    public string InicialOperador => string.IsNullOrWhiteSpace(OperadorLogado?.Nome) ? "?" : OperadorLogado!.Nome.Trim()[..1].ToUpperInvariant();

    // Sob o nome, no lugar do "JWT Ativo" do protótipo (que não existe neste app): o estado que importa ao operador.
    public string SituacaoCaixa => CaixaAberto is null ? "Sem caixa aberto" : "Caixa aberto";

    // Itens que ainda não chegaram à API (caixas, vendas, clientes e produtos novos): "Sync: 3 pendentes".
    public int PendentesSync
    {
        get => pendentesSync;
        private set
        {
            this.RaiseAndSetIfChanged(ref pendentesSync, value);
            this.RaisePropertyChanged(nameof(TextoSync));
            this.RaisePropertyChanged(nameof(TextoFilaLocal));
            RaiseEstadoDaFilaChanged();
        }
    }

    // O que está esperando, por tipo (o total é o PendentesSync). Atualizada junto com o contador, em AtualizarPendentesAsync.
    public PendenciasPorTipo Pendencias
    {
        get => pendencias;
        private set
        {
            this.RaiseAndSetIfChanged(ref pendencias, value);
            this.RaisePropertyChanged(nameof(ChipsDePendencias));
        }
    }

    // ---- cartão de estado do painel da fila ----

    // O envio da fila está rodando agora? (O App leva o andamento do serviço de fundo para a thread de interface: DefinirAndamento.)
    public bool Sincronizando => andamentoDoEnvio.Sincronizando;

    public void DefinirAndamento(AndamentoDoEnvio novo)
    {
        andamentoDoEnvio = novo;
        this.RaisePropertyChanged(nameof(Sincronizando));
        this.RaisePropertyChanged(nameof(TextoBotaoSincronizar));
        RaiseEstadoDaFilaChanged();
    }

    // Uma palavra para o estado. Sincronizando vence tudo (um ciclo que está enviando prova que há conexão); depois a falta de
    // conexão; depois o que está esperando.
    public EstadoDaFila EstadoDaFila =>
        Sincronizando ? EstadoDaFila.Sincronizando
        : Conexao == EstadoConexao.Offline ? EstadoDaFila.SemConexao
        : PendentesSync > 0 ? EstadoDaFila.Aguardando
        : EstadoDaFila.TudoEnviado;

    public string IconeEstadoDaFila => EstadoDaFila switch
    {
        EstadoDaFila.Sincronizando => "🔄",
        EstadoDaFila.SemConexao => "🔴",
        EstadoDaFila.Aguardando => "🟡",
        _ => "🟢",
    };

    public string TituloEstadoDaFila => EstadoDaFila switch
    {
        EstadoDaFila.Sincronizando => "Sincronizando…",
        EstadoDaFila.SemConexao => "Sem conexão",
        EstadoDaFila.Aguardando => "Aguardando envio",
        _ => "Tudo enviado",
    };

    public string DetalheEstadoDaFila => EstadoDaFila switch
    {
        EstadoDaFila.Sincronizando => andamentoDoEnvio.Etapa is { } etapa ? $"Enviando: {etapa}" : "Preparando o envio…",
        EstadoDaFila.SemConexao => "O que está na fila sai assim que a internet voltar.",
        EstadoDaFila.Aguardando => $"{TextoFilaLocal} na fila local.",
        _ => "Nenhuma pendência na fila local.",
    };

    // Um booleano por estado só para a tela pintar o cartão (classes de estilo); a fonte é o EstadoDaFila.
    public bool FilaTudoEnviado => EstadoDaFila == EstadoDaFila.TudoEnviado;
    public bool FilaAguardando => EstadoDaFila == EstadoDaFila.Aguardando;
    public bool FilaSemConexao => EstadoDaFila == EstadoDaFila.SemConexao;

    // O botão diz o que está acontecendo (fica parado enquanto o envio dura).
    public string TextoBotaoSincronizar => Sincronizando ? "🔄 Sincronizando…" : "🔄 Disparar Sincronização Agora";

    // "🧾 Vendas 2 · 👥 Clientes 1": só os tipos que têm algo esperando, sempre na mesma ordem.
    public IReadOnlyList<PendenciaChip> ChipsDePendencias
    {
        get
        {
            var chips = new List<PendenciaChip>();
            if (Pendencias.Caixas > 0) chips.Add(new PendenciaChip("🏦", "Caixas", Pendencias.Caixas));
            if (Pendencias.Vendas > 0) chips.Add(new PendenciaChip("🧾", "Vendas", Pendencias.Vendas));
            if (Pendencias.Clientes > 0) chips.Add(new PendenciaChip("👥", "Clientes", Pendencias.Clientes));
            if (Pendencias.Produtos > 0) chips.Add(new PendenciaChip("📦", "Produtos", Pendencias.Produtos));
            return chips;
        }
    }

    private void RaiseEstadoDaFilaChanged()
    {
        this.RaisePropertyChanged(nameof(EstadoDaFila));
        this.RaisePropertyChanged(nameof(IconeEstadoDaFila));
        this.RaisePropertyChanged(nameof(TituloEstadoDaFila));
        this.RaisePropertyChanged(nameof(DetalheEstadoDaFila));
        this.RaisePropertyChanged(nameof(FilaTudoEnviado));
        this.RaisePropertyChanged(nameof(FilaAguardando));
        this.RaisePropertyChanged(nameof(FilaSemConexao));
    }

    // ---- painel lateral da fila outbox ----

    public bool PainelOutboxAberto
    {
        get => painelOutboxAberto;
        private set => this.RaiseAndSetIfChanged(ref painelOutboxAberto, value);
    }

    public ReactiveCommand<Unit, Unit> AbrirPainelOutboxCommand { get; }
    public ReactiveCommand<Unit, Unit> FecharPainelOutboxCommand { get; }
    public ReactiveCommand<Unit, Unit> SincronizarAgoraCommand { get; }

    // "Pendências na fila local: 3 itens" — o mesmo número da pílula da barra do topo, por extenso.
    public string TextoFilaLocal => PendentesSync switch
    {
        0 => "0 itens",
        1 => "1 item",
        var n => $"{n} itens",
    };

    // O que a sincronização fez, da mais recente para a mais antiga (o App leva as linhas do serviço de fundo para a thread de
    // interface, ver AdicionarAtividade). Fica só na memória: ao reabrir o app o painel começa vazio (o histórico é o arquivo de log).
    public ObservableCollection<EntradaDeLog> Atividades { get; } = new();

    public bool SemAtividades => Atividades.Count == 0;

    // Chamar na thread de UI (o App faz o Post).
    public void AdicionarAtividade(EntradaDeLog entrada)
    {
        Atividades.Insert(0, entrada);
        while (Atividades.Count > LogDeSincronizacao.Capacidade)
            Atividades.RemoveAt(Atividades.Count - 1);

        this.RaisePropertyChanged(nameof(SemAtividades));
    }

    public string TextoSync => PendentesSync switch
    {
        1 => "1 pendente",
        var n => $"{n} pendentes",
    };

    // Recontagem barata (só as três contagens do outbox). Chamada quando o operador entra, quando uma venda é finalizada
    // e quando a sincronização em segundo plano mexeu no banco.
    private async Task AtualizarPendentesAsync()
    {
        var porTipo = await dashboardService.ContarPendenciasPorTipoAsync();
        Pendencias = porTipo;
        PendentesSync = porTipo.Total;

        if (avisarFilaVazia && PendentesSync == 0)
        {
            avisarFilaVazia = false;
            Toasts.Publicar("Nenhuma pendência na fila local. Todos os pedidos estão na nuvem!", "✅", ToastTipo.Sucesso, chave: "fila");
        }
    }

    public ViewModelBase? CurrentViewModel
    {
        get => currentViewModel;
        private set => this.RaiseAndSetIfChanged(ref currentViewModel, value);
    }

    public Funcionario? OperadorLogado
    {
        get => operadorLogado;
        private set
        {
            this.RaiseAndSetIfChanged(ref operadorLogado, value);
            this.RaisePropertyChanged(nameof(RotuloOperadorCaixa));
            this.RaisePropertyChanged(nameof(InicialOperador));
        }
    }

    public Models.Caixa? CaixaAberto
    {
        get => caixaAberto;
        private set
        {
            this.RaiseAndSetIfChanged(ref caixaAberto, value);
            this.RaisePropertyChanged(nameof(RotuloOperadorCaixa));
            this.RaisePropertyChanged(nameof(SituacaoCaixa));
        }
    }

    // Mensagem mostra na tela o erro que ExecutarComTratamentoDeErroAsync capturou (o detalhe vai pro log), em vez
    // de travar em silêncio (ver o método).
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
        private set
        {
            this.RaiseAndSetIfChanged(ref conexao, value);
            RaiseEstadoDaFilaChanged();
        }
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

    // O operador pediu "sincronizar agora" (botão da listagem de pedidos): o App liga isto ao serviço de sincronização.
    public IObservable<Unit> SincronizacaoSolicitada => sincronizacaoSolicitada;

    public void SolicitarSincronizacao()
    {
        // O aviso primeiro: o operador vê a resposta ao clique antes de o serviço começar a trabalhar. E ele diz a verdade:
        // "Sincronizando..." com a internet fora ou com a fila vazia deixaria o operador esperando algo que não vai acontecer.
        var (texto, icone) = Conexao == EstadoConexao.Offline
            ? ("Sem conexão: o que está na fila sai assim que a internet voltar.", "🛡️")
            : PendentesSync == 0
                ? ("Nenhuma pendência na fila local.", "✅")
                : ("Sincronizando a fila outbox...", "🔄");
        Toasts.Publicar(texto, icone, chave: "sincronizacao", duracao: TimeSpan.FromSeconds(2.5));
        AdicionarAtividade(new EntradaDeLog(DateTime.Now, NivelAtividade.Info, "Sincronização solicitada pelo operador."));
        sincronizacaoSolicitada.OnNext(Unit.Default);   // pedir mesmo assim: o serviço confere a rede e a fila por conta própria
    }

    // Um ciclo de sincronização mexeu no banco: se a tela aberta lista esses dados,
    // recarrega. Chamar na thread de UI (o App faz o Post).
    public void NotificarDadosSincronizados()
    {
        if (CurrentViewModel is IAtualizavelPorSincronizacao tela)
            _ = ExecutarComTratamentoDeErroAsync(tela.AtualizarAposSincronizacaoAsync);

        _ = ExecutarComTratamentoDeErroAsync(AtualizarPendentesAsync);   // "Sync: N pendentes" da barra do topo
    }

    // Erro inesperado de um comando (ver TratamentoDeErros): vira o banner em vez de derrubar o app.
    // Chamar na thread de UI (o App faz o Post).
    public void ReportarErro(Exception excecao) => Mensagem = $"Ocorreu um erro inesperado: {excecao.Message}";

    public void DefinirConexao(EstadoConexao estado, string? detalhe)
    {
        var anterior = Conexao;
        DetalheConexao = estado == EstadoConexao.Desconhecida ? null : detalhe;
        Conexao = estado;
        (CurrentViewModel as DashboardViewModel)?.DefinirConexao(estado);
        AvisarMudancaDeConexao(anterior, estado);
    }

    // Só a MUDANÇA vira aviso (cada ciclo de 30 s republica o mesmo estado). Abrir o app já offline avisa; abrir online não:
    // não há nada "restaurado" a dizer. "Online com falhas" conta como conectado (a API respondeu).
    private void AvisarMudancaDeConexao(EstadoConexao anterior, EstadoConexao atual)
    {
        var estavaOffline = anterior == EstadoConexao.Offline;
        var ficouOffline = atual == EstadoConexao.Offline;
        if (estavaOffline == ficouOffline)
            return;

        if (ficouOffline)
        {
            avisarFilaVazia = false;
            Toasts.Publicar("Internet desconectada. O PDV continua operando 100% no banco local!", "🛡️", ToastTipo.Erro, chave: "conexao");
            return;
        }

        Toasts.Publicar("Conexão com a nuvem SoftcomShop restaurada! Sincronizando fila...", "✅", ToastTipo.Sucesso, chave: "conexao");

        // Se a fila já está vazia (ou esvazia quando o ciclo terminar), o operador ganha o segundo aviso: sem isso ficaria a
        // dúvida "será que mandou tudo?". A recontagem imediata cobre o caso de não haver nada a enviar (nenhum ciclo mexeria
        // no banco, então nada dispararia a recontagem sozinho).
        avisarFilaVazia = true;
        RecontagemAposReconexao = ExecutarComTratamentoDeErroAsync(AtualizarPendentesAsync);
    }

    public ReactiveCommand<Unit, Unit> IrParaDashboardCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaPdvCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaListaPedidosCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaCadastrosCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaFecharCaixaCommand { get; }
    public ReactiveCommand<Unit, Unit> IrParaConfiguracoesCommand { get; }
    public ReactiveCommand<Unit, Unit> SairCommand { get; }

    // Marcado aqui (não a classe inteira) porque IniciarAsync é o único caminho que
    // pode chegar em IrParaConfiguracoesAsync, que constrói ConfiguracoesViewModel
    // (Windows-only por causa do vínculo de dispositivo via DPAPI).
    [SupportedOSPlatform("windows")]
    public async Task IniciarAsync() => await ExecutarComTratamentoDeErroAsync(async () =>
    {
        var configuracao = await configuracaoService.ObterOuCriarAsync();

        // Sem UrlApi, o dispositivo nunca foi vinculado — não tem como logar nem
        // sincronizar nada ainda, então Configurações vem antes até do Login.
        if (string.IsNullOrWhiteSpace(configuracao.UrlApi))
        {
            await IrParaConfiguracoesAsync();
            return;
        }

        await IrParaLoginAsync();
    });

    [SupportedOSPlatform("windows")]
    private async Task IrParaConfiguracoesAsync(bool exigirSupervisor = false)
    {
        var viewModel = new ConfiguracoesViewModel(configuracaoService, exigirSupervisor, caixasApiService);

        // Vincular com sucesso é o que destrava o resto (login, sincronização): leva ao
        // Login sem reabrir o app e avisa quem quiser sincronizar já, em vez de esperar o
        // próximo tick do timer — sem funcionários locais a chave do operador não passa.
        viewModel.VincularCommand
            .Where(vinculou => vinculou)
            .Subscribe(vinculou =>
            {
                // Um novo vínculo pode ser de OUTRA empresa: quem estava logado e o caixa aberto deixam de valer, o
                // operador entra de novo. (Na 1ª vinculação já são null — não muda nada.)
                OperadorLogado = null;
                CaixaAberto = null;

                // Avisa ANTES de navegar: quem espera a troca de tela já encontra o aviso feito.
                dispositivoVinculado.OnNext(Unit.Default);
                _ = ExecutarComTratamentoDeErroAsync(IrParaLoginAsync);
            });

        // "Voltar" (só existe quando aberta pelo botão): de volta ao ponto em que o operador estava.
        viewModel.VoltarCommand.Subscribe(evento => _ = ExecutarComTratamentoDeErroAsync(IrParaTelaInicialAsync));

        // Carrega ANTES de mostrar (ver APRENDIZADOS #61): com o formulário vazio, um "Salvar" precoce gravaria os
        // valores padrão por cima da configuração de verdade.
        await viewModel.IniciarAsync();

        // CurrentViewModel antes de TelaAtual de propósito: quem observa TelaAtual
        // (ex: um teste com WhenAnyValue) só deve acordar depois que o resto do
        // estado da navegação já está pronto — inverter a ordem cria uma corrida
        // onde TelaAtual muda mas CurrentViewModel ainda é o da tela anterior.
        CurrentViewModel = viewModel;
        TelaAtual = Tela.Configuracoes;
    }

    private async Task IrParaLoginAsync()
    {
        var viewModel = new LoginViewModel(loginOperadorService);

        // EntrarCommand é o próprio IObservable<Funcionario?> do comando (ver
        // docs/APRENDIZADOS.md #26) — Shell escuta sem LoginViewModel precisar
        // conhecer Shell.
        viewModel.EntrarCommand
            .Where(funcionario => funcionario is not null)
            .Subscribe(funcionario => _ = ExecutarComTratamentoDeErroAsync(() => AposLoginAsync(funcionario!)));

        // Carrega ANTES de mostrar: a tela já abre com a lista (ou com o aviso de "nenhum operador"), sem piscar vazia.
        await viewModel.CarregarOperadoresAsync();

        CurrentViewModel = viewModel;
        TelaAtual = Tela.Login;
    }

    // Logoff: esquece o operador e o caixa DELE na tela e volta ao login. Nada é fechado nem apagado — o caixa segue aberto
    // no banco e o operador o reencontra ao entrar de novo (AposLoginAsync busca o caixa aberto do funcionário).
    private async Task SairAsync()
    {
        if (OperadorLogado is { } operador)
            Registro.Info("Auditoria", $"Logoff: {operador.Nome}");

        OperadorLogado = null;
        CaixaAberto = null;
        PainelOutboxAberto = false;
        Mensagem = null;

        await IrParaLoginAsync();
    }

    private async Task AposLoginAsync(Funcionario funcionario)
    {
        OperadorLogado = funcionario;
        CaixaAberto = await caixaService.ObterCaixaAbertoAsync(funcionario.Id);
        await AtualizarPendentesAsync();

        await IrParaTelaInicialAsync();
    }

    // Onde o operador logado "começa": Dashboard se já tem caixa aberto (ou a abertura não é exigida), senão a tela de
    // abrir caixa. Usado depois do login, depois de fechar o caixa e ao voltar das Configurações.
    private async Task IrParaTelaInicialAsync()
    {
        var configuracao = await configuracaoService.ObterOuCriarAsync();

        if (CaixaAberto is null && configuracao.ExigirAberturaCaixa && OperadorLogado is { } operador)
            await IrParaAbrirCaixaAsync(operador.Id);
        else
            await IrParaDashboardAsync();
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
        var viewModel = new DashboardViewModel(dashboardService, vendaLocalService, CaixaAberto?.Id);
        viewModel.DefinirConexao(Conexao);
        await viewModel.IniciarAsync();

        // Nova Venda não navega sozinho — só emite (fica desabilitado sem caixa
        // aberto, ver DashboardViewModel), o Shell decide o que fazer com isso.
        viewModel.NovaVendaCommand.Subscribe(evento => _ = ExecutarComTratamentoDeErroAsync(() => IrParaPdvAsync(CaixaAberto!.Id)));
        viewModel.VerPedidosCommand.Subscribe(evento => _ = ExecutarComTratamentoDeErroAsync(() => IrParaListaPedidosAsync(CaixaAberto!.Id)));
        // "Detalhes" de uma venda da tabela: leva à listagem com ela já selecionada (é onde o detalhe e o descarte moram).
        viewModel.DetalhesCommand.Subscribe(venda => _ = ExecutarComTratamentoDeErroAsync(() => IrParaListaPedidosAsync(CaixaAberto!.Id, venda.Id)));

        CurrentViewModel = viewModel;
        TelaAtual = Tela.Dashboard;
    }

    private async Task IrParaPdvAsync(int caixaId)
    {
        var viewModel = new PdvViewModel(vendaService, catalogoLocalService, caixaId);
        await viewModel.IniciarAsync();
        viewModel.WhenAnyValue(vm => vm.TemVendaEmAndamento).Subscribe(emAndamento => VendaEmAndamento = emAndamento);
        // Um aviso só, sempre do último item: quem bipa vários produtos seguidos não vê uma pilha deles (chave "item").
        viewModel.ItemLancado.Subscribe(nome => Toasts.Publicar($"\"{nome}\" adicionado ao cupom.", "🔔", chave: "item", duracao: TimeSpan.FromSeconds(2.5)));
        // Venda finalizada = mais um item esperando envio: a pílula "Sync: N pendentes" acompanha na hora.
        viewModel.FinalizarVendaCommand.Where(venda => venda is not null)
            .Subscribe(venda => _ = ExecutarComTratamentoDeErroAsync(AtualizarPendentesAsync));
        CurrentViewModel = viewModel;
        TelaAtual = Tela.Pdv;
    }

    private async Task IrParaListaPedidosAsync(int caixaId, Guid? vendaParaSelecionar = null)
    {
        var viewModel = new ListaPedidosViewModel(vendaLocalService, caixaId, OperadorLogado?.Id);
        await viewModel.IniciarAsync();
        // Vindo do "Detalhes" do painel principal: já chega com o modal daquela venda aberto.
        if (vendaParaSelecionar is { } vendaId && viewModel.Vendas.FirstOrDefault(v => v.Id == vendaId) is { } venda)
            await viewModel.AbrirDetalheAsync(venda);
        // Só emitem (mesmo padrão do painel): o Shell decide navegar ou pedir a sincronização.
        viewModel.NovaVendaCommand.Subscribe(evento => _ = ExecutarComTratamentoDeErroAsync(() => IrParaPdvAsync(caixaId)));
        viewModel.SincronizarAgoraCommand.Subscribe(evento => SolicitarSincronizacao());
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

        await IrParaTelaInicialAsync();
    }

    private async Task IrParaCadastrosAsync()
    {
        var viewModel = new CadastrosViewModel(cadastroLocalService, cepService);
        // Cliente ou produto novo = mais um item esperando envio: a pílula "Sync: N pendentes" acompanha na hora.
        viewModel.CadastroCriado.Subscribe(criado => _ = ExecutarComTratamentoDeErroAsync(AtualizarPendentesAsync));
        await viewModel.IniciarAsync();
        CurrentViewModel = viewModel;
        TelaAtual = Tela.Cadastros;
    }

    // "Fire-and-forget seguro": IrPara* dispara trabalho assíncrono (carregar
    // dados, reagir a login) sem o chamador esperar — sem esse try/catch, qualquer
    // exceção nesse trabalho vira uma task nunca observada e desaparece
    // silenciosamente, travando a tela sem explicação nenhuma (achado numa revisão
    // de código, 2026-09-18). Nunca deixa isso sumir em silêncio: o detalhe vai pro log e a Mensagem mostra algo na tela.
    private async Task ExecutarComTratamentoDeErroAsync(Func<Task> operacao)
    {
        try
        {
            await operacao();
        }
        catch (Exception ex)
        {
            Registro.Erro("Interface", "Erro inesperado numa operação em segundo plano", ex);
            Mensagem = $"Ocorreu um erro inesperado: {ex.Message}";
        }
    }
}

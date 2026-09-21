using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// As três abas da tela de Cadastros.
public enum AbaCadastros
{
    Produtos,
    Clientes,
    Operadores,
}

// Produtos, clientes e operadores do banco local (leitura + busca, em três abas) e o formulário de criar
// cliente. Tudo local: criar grava PendenteSync e o cliente aparece na hora com
// 🟡 Pendente — o envio pra API é do serviço de sincronização, nunca desta tela
// (por isso funciona offline e não trava esperando rede).
//
// A busca é manual (Enter/botão), mesma decisão de ListaPedidosViewModel: sem
// debounce automático, o comportamento é previsível e testável.
public class CadastrosViewModel : ViewModelBase, IAtualizavelPorSincronizacao
{
    private readonly CadastroLocalService cadastroLocalService;

    private IReadOnlyList<ClienteResumo> clientes = Array.Empty<ClienteResumo>();
    private IReadOnlyList<ProdutoResumo> produtos = Array.Empty<ProdutoResumo>();
    private string busca = string.Empty;
    private string buscaAplicada = string.Empty;
    private string novoNome = string.Empty;
    private string novoCpfCnpj = string.Empty;
    private string novoTelefone = string.Empty;
    private string novoEmail = string.Empty;
    private string novaCidadeUf = string.Empty;
    private string? mensagemForm;
    private bool mensagemFormEhErro;
    private string? mensagemReenvio;
    private AbaCadastros abaAtual = AbaCadastros.Produtos;
    private ContagemCadastros contagem = new(0, 0, 0);
    private IReadOnlyList<OperadorResumo> operadores = Array.Empty<OperadorResumo>();
    private bool formularioClienteAberto;
    private string? mensagemProdutoSalvo;
    private readonly ObservableAsPropertyHelper<bool> modalAberto;
    private readonly Subject<Unit> cadastroCriado = new();

    public CadastrosViewModel(CadastroLocalService cadastroLocalService)
    {
        this.cadastroLocalService = cadastroLocalService;

        BuscarCommand = ReactiveCommand.CreateFromTask(BuscarAsync);
        ReenviarFalhasCommand = ReactiveCommand.CreateFromTask(ReenviarFalhasAsync);

        SelecionarAbaCommand = ReactiveCommand.Create<AbaCadastros>(aba => { AbaAtual = aba; });
        // "Novo Cliente" (no topo ou na aba de clientes): leva à aba de clientes e abre o modal de cadastro.
        NovoClienteCommand = ReactiveCommand.CreateFromTask(AbrirFormularioAsync);

        // "＋ Novo Produto": abre o modal de cadastro de produto (formulário à parte, ver ProdutoFormViewModel).
        FormProduto = new ProdutoFormViewModel(cadastroLocalService, RecarregarAposCriarProdutoAsync);
        NovoProdutoCommand = ReactiveCommand.CreateFromTask(AbrirFormularioProdutoAsync);

        // Qualquer dos dois modais aberto: a tela de trás fica desabilitada.
        this.WhenAnyValue(vm => vm.FormularioClienteAberto)
            .CombineLatest(FormProduto.WhenAnyValue(f => f.Aberto), (cliente, produto) => cliente || produto)
            .ToProperty(this, vm => vm.ModalAberto, out modalAberto);
        // Cancelar / ✕ / Esc: fecha e descarta o que foi digitado (o modal sempre abre limpo).
        FecharFormularioClienteCommand = ReactiveCommand.Create(FecharFormulario);

        // Nome e CPF/CNPJ são obrigatórios (os dois com * no modal); o resto é opcional.
        var podeCriar = this.WhenAnyValue(vm => vm.NovoNome, vm => vm.NovoCpfCnpj,
            (nome, documento) => !string.IsNullOrWhiteSpace(nome) && !string.IsNullOrWhiteSpace(documento));
        CriarClienteCommand = ReactiveCommand.CreateFromTask(CriarClienteAsync, podeCriar);
    }

    public IReadOnlyList<ClienteResumo> Clientes
    {
        get => clientes;
        private set
        {
            this.RaiseAndSetIfChanged(ref clientes, value);
            this.RaisePropertyChanged(nameof(MensagemVazioClientes));
            this.RaisePropertyChanged(nameof(ClientesCortados));
        }
    }

    public IReadOnlyList<ProdutoResumo> Produtos
    {
        get => produtos;
        private set
        {
            this.RaiseAndSetIfChanged(ref produtos, value);
            this.RaisePropertyChanged(nameof(MensagemVazioProdutos));
            this.RaisePropertyChanged(nameof(ProdutosCortados));
        }
    }

    // ---- abas ----

    public AbaCadastros AbaAtual
    {
        get => abaAtual;
        private set
        {
            this.RaiseAndSetIfChanged(ref abaAtual, value);
            this.RaisePropertyChanged(nameof(ProdutosAtiva));
            this.RaisePropertyChanged(nameof(ClientesAtiva));
            this.RaisePropertyChanged(nameof(OperadoresAtiva));
        }
    }

    public bool ProdutosAtiva => AbaAtual == AbaCadastros.Produtos;
    public bool ClientesAtiva => AbaAtual == AbaCadastros.Clientes;
    public bool OperadoresAtiva => AbaAtual == AbaCadastros.Operadores;

    // "Produtos ( 12 )": o total de cadastros, não o tamanho da lista filtrada nem o corte de 200.
    public string RotuloProdutos => $"Produtos ( {Contagem.Produtos} )";
    public string RotuloClientes => $"Clientes ( {Contagem.Clientes} )";
    public string RotuloOperadores => $"Operadores de Caixa ( {Contagem.Operadores} )";

    public ContagemCadastros Contagem
    {
        get => contagem;
        private set
        {
            this.RaiseAndSetIfChanged(ref contagem, value);
            this.RaisePropertyChanged(nameof(RotuloProdutos));
            this.RaisePropertyChanged(nameof(RotuloClientes));
            this.RaisePropertyChanged(nameof(RotuloOperadores));
        }
    }

    public IReadOnlyList<OperadorResumo> Operadores
    {
        get => operadores;
        private set
        {
            this.RaiseAndSetIfChanged(ref operadores, value);
            this.RaisePropertyChanged(nameof(MensagemVazioOperadores));
        }
    }

    public string? MensagemVazioOperadores => Operadores.Count > 0 ? null : MensagemVazio("operador");

    // O formulário de novo cliente fica recolhido até pedirem (botão "Novo Cliente" ou "Cadastrar Cliente"): a tela abre limpa.
    public bool FormularioClienteAberto
    {
        get => formularioClienteAberto;
        private set => this.RaiseAndSetIfChanged(ref formularioClienteAberto, value);
    }

    public ReactiveCommand<AbaCadastros, Unit> SelecionarAbaCommand { get; }
    public ReactiveCommand<Unit, Unit> NovoClienteCommand { get; }
    public ReactiveCommand<Unit, Unit> NovoProdutoCommand { get; }

    public ProdutoFormViewModel FormProduto { get; }

    // Dispara quando um cliente ou produto novo acaba de ser gravado (pendente de envio): o Shell atualiza na hora o "Sync: N pendentes"
    // da barra do topo, do mesmo jeito que faz ao finalizar uma venda.
    public IObservable<Unit> CadastroCriado => cadastroCriado;

    // Um dos modais (cliente ou produto) está aberto: a tela de trás fica desabilitada.
    public bool ModalAberto => modalAberto.Value;

    // "Produto salvo": o modal fecha ao salvar e o aviso fica na tela de trás, junto da lista.
    public string? MensagemProdutoSalvo
    {
        get => mensagemProdutoSalvo;
        private set => this.RaiseAndSetIfChanged(ref mensagemProdutoSalvo, value);
    }
    public ReactiveCommand<Unit, Unit> FecharFormularioClienteCommand { get; }

    public string Busca
    {
        get => busca;
        set => this.RaiseAndSetIfChanged(ref busca, value);
    }

    public string NovoNome
    {
        get => novoNome;
        set => this.RaiseAndSetIfChanged(ref novoNome, value);
    }

    public string NovoCpfCnpj
    {
        get => novoCpfCnpj;
        set => this.RaiseAndSetIfChanged(ref novoCpfCnpj, value);
    }

    public string NovoTelefone
    {
        get => novoTelefone;
        set => this.RaiseAndSetIfChanged(ref novoTelefone, value);
    }

    public string NovoEmail
    {
        get => novoEmail;
        set => this.RaiseAndSetIfChanged(ref novoEmail, value);
    }

    // Já abre preenchida com a cidade da empresa (ver AbrirFormularioAsync); o operador troca se o cliente for de outra.
    public string NovaCidadeUf
    {
        get => novaCidadeUf;
        set => this.RaiseAndSetIfChanged(ref novaCidadeUf, value);
    }

    public string? MensagemForm
    {
        get => mensagemForm;
        private set
        {
            this.RaiseAndSetIfChanged(ref mensagemForm, value);
            RaiseMensagemFormChanged();
        }
    }

    public bool MensagemFormEhErro
    {
        get => mensagemFormEhErro;
        private set
        {
            this.RaiseAndSetIfChanged(ref mensagemFormEhErro, value);
            RaiseMensagemFormChanged();
        }
    }

    // Duas "fatias" da mesma mensagem, só pra tela poder colorir e esconder cada uma
    // com um binding simples (vermelho pro erro, verde pro sucesso).
    public string? MensagemFormErro => MensagemFormEhErro ? MensagemForm : null;
    public string? MensagemFormSucesso => MensagemFormEhErro ? null : MensagemForm;

    // Estado vazio nunca é tela em branco (spec, "Convenções de UI"): null = tem
    // itens (a tela esconde a mensagem), texto = o que mostrar no lugar da lista.
    public string? MensagemVazioClientes => Clientes.Count > 0 ? null : MensagemVazio("cliente");
    public string? MensagemVazioProdutos => Produtos.Count > 0 ? null : MensagemVazio("produto");

    // O serviço corta em LimiteLista; a tela avisa pra ninguém achar que aquilo é tudo.
    public bool ClientesCortados => Clientes.Count >= CadastroLocalService.LimiteLista;
    public bool ProdutosCortados => Produtos.Count >= CadastroLocalService.LimiteLista;
    public string AvisoLimite => $"Mostrando os primeiros {CadastroLocalService.LimiteLista} — use a busca para encontrar os demais.";

    public ReactiveCommand<Unit, Unit> BuscarCommand { get; }
    public ReactiveCommand<Unit, Unit> CriarClienteCommand { get; }

    // Clientes que falharam ou desistiram voltam à fila de envio — ver PoliticaRetentativa.
    public ReactiveCommand<Unit, Unit> ReenviarFalhasCommand { get; }

    public string? MensagemReenvio
    {
        get => mensagemReenvio;
        private set => this.RaiseAndSetIfChanged(ref mensagemReenvio, value);
    }

    public Task IniciarAsync() => BuscarAsync();

    // Chamado pelo Shell quando um ciclo de sincronização mexeu no banco. Recarrega com a
    // busca JÁ APLICADA (a do último Enter/botão), não com o que o operador está digitando
    // agora — senão a lista mudaria de filtro sozinha no meio da digitação. O formulário
    // de novo cliente nem é tocado.
    public Task AtualizarAposSincronizacaoAsync() => CarregarAsync();

    private Task BuscarAsync()
    {
        buscaAplicada = Busca;
        return CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        // Duas consultas em sequência (e não Task.WhenAll): cada uma abre seu próprio
        // contexto, mas em SQLite o ganho de paralelizar é nenhum e a ordem fica simples.
        Clientes = await cadastroLocalService.ListarClientesAsync(buscaAplicada);
        Produtos = await cadastroLocalService.ListarProdutosAsync(buscaAplicada);
        Operadores = await cadastroLocalService.ListarOperadoresAsync(buscaAplicada);
        Contagem = await cadastroLocalService.ContarAsync();
    }

    private async Task ReenviarFalhasAsync()
    {
        var reenviados = await cadastroLocalService.ReenviarFalhasAsync();
        MensagemReenvio = reenviados == 0
            ? "Nenhuma falha para reenviar."
            : $"{reenviados} cadastro(s) voltaram para a fila e serão enviados no próximo ciclo (até 30 s).";
        await CarregarAsync();
    }

    private async Task AbrirFormularioProdutoAsync()
    {
        AbaAtual = AbaCadastros.Produtos;
        MensagemProdutoSalvo = null;
        await FormProduto.AbrirAsync();
    }

    // O modal já fechou e o produto está gravado (pendente): limpa a busca (uma busca ativa poderia escondê-lo) e recarrega.
    private async Task RecarregarAposCriarProdutoAsync()
    {
        cadastroCriado.OnNext(Unit.Default);
        MensagemProdutoSalvo = "Produto salvo. Ele será enviado à API na próxima sincronização.";
        Busca = string.Empty;
        await BuscarAsync();
    }

    private async Task AbrirFormularioAsync()
    {
        AbaAtual = AbaCadastros.Clientes;
        MensagemForm = null;

        if (string.IsNullOrWhiteSpace(NovaCidadeUf))
            NovaCidadeUf = await cadastroLocalService.ObterCidadeUfPadraoAsync();

        FormularioClienteAberto = true;
    }

    private void FecharFormulario()
    {
        FormularioClienteAberto = false;
        LimparFormulario();
        MensagemForm = null;
    }

    private void LimparFormulario()
    {
        NovoNome = string.Empty;
        NovoCpfCnpj = string.Empty;
        NovoTelefone = string.Empty;
        NovoEmail = string.Empty;
        NovaCidadeUf = string.Empty;   // a próxima abertura traz de novo a cidade da empresa
    }

    private async Task CriarClienteAsync()
    {
        var resultado = await cadastroLocalService.CriarClienteAsync(
            new NovoClienteDados(NovoNome, NovoCpfCnpj, NovoTelefone, NovoEmail, NovaCidadeUf));

        if (!resultado.Sucesso)
        {
            // Mantém o modal aberto e o que o operador digitou pra ele corrigir sem redigitar; o erro aparece no próprio modal.
            MensagemFormEhErro = true;
            MensagemForm = resultado.Mensagem;
            return;
        }

        // Salvou: o modal fecha e o aviso verde fica na tela de trás, junto da lista onde o cliente acabou de aparecer.
        MensagemFormEhErro = false;
        MensagemForm = "Cliente salvo. Ele será enviado à API na próxima sincronização.";
        FormularioClienteAberto = false;
        LimparFormulario();
        cadastroCriado.OnNext(Unit.Default);

        // Busca ativa poderia esconder o cliente recém-criado — limpa pra ele aparecer.
        Busca = string.Empty;
        await BuscarAsync();
    }

    private void RaiseMensagemFormChanged()
    {
        this.RaisePropertyChanged(nameof(MensagemFormErro));
        this.RaisePropertyChanged(nameof(MensagemFormSucesso));
    }

    // Só o cliente se cadastra aqui; produtos e operadores vêm da sincronização — a mensagem não pode mandar "cadastrar o
    // primeiro" onde não há como.
    private string MensagemVazio(string item) => !string.IsNullOrWhiteSpace(buscaAplicada)
        ? $"Nenhum {item} encontrado para essa busca."
        : item == "cliente"
            ? "Nenhum cliente cadastrado ainda — sincronize com a API ou cadastre o primeiro."
            : $"Nenhum {item} sincronizado ainda — aguarde a sincronização com a API.";
}

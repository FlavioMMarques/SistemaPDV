using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.ViewModels;

// O núcleo do módulo: carrinho (Itens) + pagamentos (suporta misto) + cliente
// opcional. Finalizar só chama VendaService.RegistrarVendaLocalAsync — nunca
// VendaSyncService, que é papel do SincronizacaoBackgroundService (Task 50).
// caixaId vem de quem navega pra cá (assumido sempre um caixa aberto — é assim
// que o ShellViewModel só chega aqui depois de abrir caixa).
public class PdvViewModel : ViewModelBase, IAtualizavelPorSincronizacao
{
    private readonly VendaService vendaService;
    private readonly CatalogoLocalService catalogoLocalService;
    private readonly int caixaId;

    private IReadOnlyList<Produto> produtosDisponiveis = Array.Empty<Produto>();
    private IReadOnlyList<Cliente> clientesDisponiveis = Array.Empty<Cliente>();
    private IReadOnlyList<FormaPagamento> formasPagamentoDisponiveis = Array.Empty<FormaPagamento>();
    private IReadOnlyList<string> bandeirasDisponiveis = Array.Empty<string>();
    private string? bandeiraSelecionada;
    private Cliente? clienteSelecionado;
    private string filtroProduto = string.Empty;
    private string quantidadeAdicionar = "1";
    private string valorPagamentoAdicionar = string.Empty;
    private string? mensagem;

    public PdvViewModel(VendaService vendaService, CatalogoLocalService catalogoLocalService, int caixaId)
    {
        this.vendaService = vendaService;
        this.catalogoLocalService = catalogoLocalService;
        this.caixaId = caixaId;

        // ObservableCollection não participa de WhenAnyValue sozinha — CollectionChanged
        // dispara o RaisePropertyChanged manual que faz PodeFinalizarVenda/Total
        // reagirem como qualquer outra propriedade reativa.
        Itens.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(PodeFinalizarVenda));
            this.RaisePropertyChanged(nameof(Total));
            this.RaisePropertyChanged(nameof(TemVendaEmAndamento));
        };
        Pagamentos.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(PodeFinalizarVenda));
            this.RaisePropertyChanged(nameof(TemVendaEmAndamento));
        };

        var podeFinalizar = this.WhenAnyValue(vm => vm.PodeFinalizarVenda);
        FinalizarVendaCommand = ReactiveCommand.CreateFromTask(FinalizarVendaAsync, podeFinalizar);
        NovoCommand = ReactiveCommand.CreateFromTask(NovoAsync);
        CancelarCommand = ReactiveCommand.CreateFromTask(NovoAsync);

        // Wrappers finos só pra dar um ICommand pro XAML chamar (Button.Command não
        // aceita um método comum) — a lógica de verdade continua nos métodos
        // públicos, que os testes chamam direto sem precisar passar por comando.
        AdicionarItemCommand = ReactiveCommand.Create<Produto>(AdicionarItem);
        RemoverItemCommand = ReactiveCommand.Create<ItemCarrinho>(RemoverItem);
        AdicionarPagamentoCommand = ReactiveCommand.Create<FormaPagamento>(AdicionarPagamento);
        RemoverPagamentoCommand = ReactiveCommand.Create<PagamentoAlocado>(RemoverPagamento);
    }

    public ObservableCollection<ItemCarrinho> Itens { get; } = new();
    public ObservableCollection<PagamentoAlocado> Pagamentos { get; } = new();

    public IReadOnlyList<Produto> ProdutosDisponiveis
    {
        get => produtosDisponiveis;
        private set
        {
            this.RaiseAndSetIfChanged(ref produtosDisponiveis, value);
            this.RaisePropertyChanged(nameof(ProdutosFiltrados));
        }
    }

    // Alimenta o atalho F4 (buscar produto) — filtro simples por nome, sem
    // acento/case sensitive, sobre o que já está em ProdutosDisponiveis (nenhuma
    // consulta nova ao banco a cada tecla digitada).
    public string FiltroProduto
    {
        get => filtroProduto;
        set
        {
            this.RaiseAndSetIfChanged(ref filtroProduto, value);
            this.RaisePropertyChanged(nameof(ProdutosFiltrados));
        }
    }

    public IReadOnlyList<Produto> ProdutosFiltrados => string.IsNullOrWhiteSpace(FiltroProduto)
        ? ProdutosDisponiveis
        : ProdutosDisponiveis.Where(p => p.Nome.Contains(FiltroProduto, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<Cliente> ClientesDisponiveis
    {
        get => clientesDisponiveis;
        private set => this.RaiseAndSetIfChanged(ref clientesDisponiveis, value);
    }

    public IReadOnlyList<FormaPagamento> FormasPagamentoDisponiveis
    {
        get => formasPagamentoDisponiveis;
        private set => this.RaiseAndSetIfChanged(ref formasPagamentoDisponiveis, value);
    }

    // Nulo = Consumidor Final implícito — resolvido só na hora de sincronizar
    // (VendaSyncService), não aqui. Pode ser um cliente sem IdExterno ainda
    // (recém-criado): decisão do usuário (2026-09-18), ver docs/APRENDIZADOS.md.
    public Cliente? ClienteSelecionado
    {
        get => clienteSelecionado;
        set => this.RaiseAndSetIfChanged(ref clienteSelecionado, value);
    }

    public string QuantidadeAdicionar
    {
        get => quantidadeAdicionar;
        set => this.RaiseAndSetIfChanged(ref quantidadeAdicionar, value);
    }

    // Bandeiras dos cartões sincronizados. Vazia = ainda não há cartões: o pagamento em cartão segue sem bandeira.
    public IReadOnlyList<string> BandeirasDisponiveis
    {
        get => bandeirasDisponiveis;
        private set
        {
            this.RaiseAndSetIfChanged(ref bandeirasDisponiveis, value);
            this.RaisePropertyChanged(nameof(TemBandeiras));
        }
    }

    public bool TemBandeiras => BandeirasDisponiveis.Count > 0;

    // Escolha do operador para o PRÓXIMO pagamento em cartão; volta a nula depois de cada pagamento adicionado.
    public string? BandeiraSelecionada
    {
        get => bandeiraSelecionada;
        set => this.RaiseAndSetIfChanged(ref bandeiraSelecionada, value);
    }

    public string ValorPagamentoAdicionar
    {
        get => valorPagamentoAdicionar;
        set => this.RaiseAndSetIfChanged(ref valorPagamentoAdicionar, value);
    }

    public string? Mensagem
    {
        get => mensagem;
        private set => this.RaiseAndSetIfChanged(ref mensagem, value);
    }

    public decimal Total => Itens.Sum(item => item.Total);

    // Achado numa revisão de código (2026-09-18): carrinho não vazio sozinho não
    // bastava — dava pra finalizar com pagamento parcial ou nenhum. Pagar A MAIS
    // do que o total é permitido (dinheiro com troco); a MENOS, não. Troco em si
    // não é modelado ainda.
    public bool PodeFinalizarVenda => Itens.Count > 0 && Pagamentos.Sum(p => p.Valor) >= Total;

    // Qualquer coisa já digitada conta (item OU pagamento) — o Shell usa isso pra
    // bloquear a navegação e não descartar o que o operador já montou.
    public bool TemVendaEmAndamento => Itens.Count > 0 || Pagamentos.Count > 0;

    public ReactiveCommand<Unit, Venda?> FinalizarVendaCommand { get; }
    public ReactiveCommand<Unit, Unit> NovoCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelarCommand { get; }
    public ReactiveCommand<Produto, Unit> AdicionarItemCommand { get; }
    public ReactiveCommand<ItemCarrinho, Unit> RemoverItemCommand { get; }
    public ReactiveCommand<FormaPagamento, Unit> AdicionarPagamentoCommand { get; }
    public ReactiveCommand<PagamentoAlocado, Unit> RemoverPagamentoCommand { get; }

    public async Task IniciarAsync()
    {
        ProdutosDisponiveis = await catalogoLocalService.ListarProdutosDisponiveisAsync();
        ClientesDisponiveis = await catalogoLocalService.ListarClientesAsync();
        FormasPagamentoDisponiveis = await catalogoLocalService.ListarFormasPagamentoDisponiveisAsync();
        BandeirasDisponiveis = await catalogoLocalService.ListarBandeirasAsync();
    }

    // O Shell chama quando um ciclo de sincronização mexeu no banco. Na tela de venda recarrega SÓ as bandeiras: os
    // cartões podem chegar com a tela já aberta (1º uso, ou cartão novo no SoftcomShop) e, sem isto, um pagamento em
    // cartão seria gravado sem bandeira e ficaria fora da apuração. Produtos, clientes, formas e o carrinho NÃO são
    // tocados — recarregar o catálogo no meio de uma venda seria pior que deixá-lo desatualizado.
    public async Task AtualizarAposSincronizacaoAsync()
    {
        var bandeiras = await catalogoLocalService.ListarBandeirasAsync();
        BandeirasDisponiveis = bandeiras;

        // Se a bandeira que estava escolhida deixou de existir, não fica uma escolha fantasma.
        if (BandeiraSelecionada is not null && !bandeiras.Contains(BandeiraSelecionada))
            BandeiraSelecionada = null;
    }

    public void AdicionarItem(Produto produto)
    {
        var quantidade = ValorMonetario.TentarLer(QuantidadeAdicionar, out var valor, casasDecimais: 3) && valor > 0
            ? valor
            : 1m;

        Itens.Add(new ItemCarrinho { Produto = produto, Quantidade = quantidade, PrecoUnitario = produto.PrecoVenda });
    }

    public void RemoverItem(ItemCarrinho item) => Itens.Remove(item);

    public void AdicionarPagamento(FormaPagamento forma)
    {
        // Diferente de AdicionarItem (onde quantidade inválida cai num default
        // sensato de 1): dinheiro não tem default sensato nenhum — um valor
        // inválido/vazio tem que rejeitar a ação, não adicionar um pagamento de
        // R$ 0,00 silencioso (achado numa revisão de código, 2026-09-18).
        if (!ValorMonetario.TentarLer(ValorPagamentoAdicionar, out var valor) || valor <= 0)
        {
            Mensagem = "Informe um valor válido pra adicionar o pagamento.";
            return;
        }

        // Cartão com bandeiras sincronizadas: a bandeira é OBRIGATÓRIA (é o que alimenta a apuração por bandeira no
        // fechamento). Sem cartões sincronizados não há o que escolher, então não trava a venda — só fica sem bandeira.
        string? bandeira = null;
        if (forma.EhCartao && TemBandeiras)
        {
            if (string.IsNullOrEmpty(BandeiraSelecionada))
            {
                Mensagem = "Escolha a bandeira do cartão antes de adicionar o pagamento.";
                return;
            }

            bandeira = BandeiraSelecionada;
        }

        Mensagem = null;
        Pagamentos.Add(new PagamentoAlocado { FormaPagamento = forma, Valor = valor, Bandeira = bandeira });
        BandeiraSelecionada = null;   // a escolha vale para um pagamento só (o próximo cartão pode ser de outra bandeira)
    }

    public void RemoverPagamento(PagamentoAlocado pagamento) => Pagamentos.Remove(pagamento);

    private async Task<Venda?> FinalizarVendaAsync()
    {
        Mensagem = null;

        var itens = Itens
            .Select(i => (i.Produto.Id, i.Quantidade, i.PrecoUnitario, i.DescontoItem, i.AcrescimoItem))
            .ToList();
        var pagamentos = Pagamentos
            .Select(p => (p.FormaPagamento.Id, p.Valor, p.Bandeira))
            .ToList();

        var venda = await vendaService.RegistrarVendaLocalAsync(caixaId, ClienteSelecionado?.Id, itens, pagamentos);

        await LimparAsync();

        return venda;
    }

    private Task NovoAsync() => LimparAsync();

    private Task LimparAsync()
    {
        Itens.Clear();
        Pagamentos.Clear();
        ClienteSelecionado = null;
        QuantidadeAdicionar = "1";
        ValorPagamentoAdicionar = string.Empty;
        BandeiraSelecionada = null;
        return Task.CompletedTask;
    }
}

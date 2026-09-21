using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
    private bool emPagamento;
    private Produto? produtoAguardandoPreco;
    private string precoInformado = string.Empty;
    private string? mensagemPreco;
    private IReadOnlyList<OpcaoPagamento> opcoesPagamento = Array.Empty<OpcaoPagamento>();
    private FormaPagamento? formaSelecionada;

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
            Renumerar();
            this.RaisePropertyChanged(nameof(PodeFinalizarVenda));
            this.RaisePropertyChanged(nameof(PagamentosPassamDoTotal));
            this.RaisePropertyChanged(nameof(AvisoExcesso));
            this.RaisePropertyChanged(nameof(PodeConfirmar));
            this.RaisePropertyChanged(nameof(TrocoPrevisto));
            this.RaisePropertyChanged(nameof(Total));
            this.RaisePropertyChanged(nameof(Subtotal));
            this.RaisePropertyChanged(nameof(TotalDescontos));
            this.RaisePropertyChanged(nameof(ResumoItens));
            this.RaisePropertyChanged(nameof(TemItens));
            this.RaisePropertyChanged(nameof(Restante));
            this.RaisePropertyChanged(nameof(Troco));
            this.RaisePropertyChanged(nameof(TemVendaEmAndamento));
        };
        Pagamentos.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(PodeFinalizarVenda));
            this.RaisePropertyChanged(nameof(PagamentosPassamDoTotal));
            this.RaisePropertyChanged(nameof(AvisoExcesso));
            this.RaisePropertyChanged(nameof(PodeConfirmar));
            this.RaisePropertyChanged(nameof(TrocoPrevisto));
            this.RaisePropertyChanged(nameof(TotalPago));
            this.RaisePropertyChanged(nameof(TemPagamentos));
            this.RaisePropertyChanged(nameof(Restante));
            this.RaisePropertyChanged(nameof(Troco));
            this.RaisePropertyChanged(nameof(TemVendaEmAndamento));
        };

        var podeFinalizar = this.WhenAnyValue(vm => vm.PodeFinalizarVenda);
        FinalizarVendaCommand = ReactiveCommand.CreateFromTask(FinalizarVendaAsync, podeFinalizar);
        NovoCommand = ReactiveCommand.CreateFromTask(NovoAsync);
        CancelarCommand = ReactiveCommand.CreateFromTask(NovoAsync);

        // Painel de pagamento (protótipo): "FINALIZAR VENDA (F10)" no cupom só ABRE o painel; a venda é registrada pelo
        // botão de confirmar dele. F10 faz as duas coisas em sequência (abre; com o painel aberto e o valor pago, confirma).
        var podeAbrirPagamento = this.WhenAnyValue(vm => vm.TemItens, vm => vm.ModalAberto, (temItens, modalAberto) => temItens && !modalAberto);
        AbrirPagamentoCommand = ReactiveCommand.Create(AbrirPagamento, podeAbrirPagamento);
        FecharPagamentoCommand = ReactiveCommand.Create(FecharPagamento);
        AvancarCommand = ReactiveCommand.CreateFromTask(AvancarAsync);
        EscCommand = ReactiveCommand.CreateFromTask(EscAsync);
        LimparBuscaCommand = ReactiveCommand.Create(() => { FiltroProduto = string.Empty; });
        AdicionarPorBuscaCommand = ReactiveCommand.Create(AdicionarPorBusca);

        // Painel de pagamento no estilo do protótipo: escolhe a forma (cartão), ajusta o valor, lança; pode repetir (misto).
        SelecionarFormaCommand = ReactiveCommand.Create<OpcaoPagamento>(SelecionarForma);
        AdicionarSelecionadaCommand = ReactiveCommand.Create(
            () => AdicionarPagamento(FormaSelecionada!),
            this.WhenAnyValue(vm => vm.FormaSelecionada).Select(forma => forma is not null));
        ConfirmarVendaCommand = ReactiveCommand.CreateFromTask(ConfirmarAsync, this.WhenAnyValue(vm => vm.PodeConfirmar));

        // Wrappers finos só pra dar um ICommand pro XAML chamar (Button.Command não
        // aceita um método comum) — a lógica de verdade continua nos métodos
        // públicos, que os testes chamam direto sem precisar passar por comando.
        AdicionarItemCommand = ReactiveCommand.Create<Produto>(AdicionarItem);
        RemoverItemCommand = ReactiveCommand.Create<ItemCarrinho>(RemoverItem);
        RemoverPagamentoCommand = ReactiveCommand.Create<PagamentoAlocado>(RemoverPagamento);

        // Painel de preço (produto com preço zero no cadastro): confirmar lança o item; cancelar descarta o lançamento.
        ConfirmarPrecoCommand = ReactiveCommand.Create(ConfirmarPreco);
        CancelarPrecoCommand = ReactiveCommand.Create(CancelarPreco);
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
            this.RaisePropertyChanged(nameof(SemResultados));
            this.RaisePropertyChanged(nameof(TextoSemResultados));
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
            this.RaisePropertyChanged(nameof(SemResultados));
            this.RaisePropertyChanged(nameof(TextoSemResultados));
        }
    }

    // Busca por nome OU por código (barras, SKU, referência): "Digite o nome do produto ou bipe o código de barras".
    public IReadOnlyList<Produto> ProdutosFiltrados => string.IsNullOrWhiteSpace(FiltroProduto)
        ? ProdutosDisponiveis
        : ProdutosDisponiveis.Where(p => Casa(p, FiltroProduto.Trim())).ToList();

    // Estado vazio da grade: catálogo ainda sem produtos (1ª sincronização) ou busca sem resultado — mensagens diferentes,
    // porque a ação do operador é diferente (esperar × trocar o texto).
    public bool SemResultados => ProdutosFiltrados.Count == 0;

    public string TextoSemResultados => ProdutosDisponiveis.Count == 0
        ? "Nenhum produto sincronizado ainda — aguarde a sincronização do catálogo."
        : $"Nenhum produto encontrado para “{FiltroProduto.Trim()}”.";

    private static bool Casa(Produto p, string texto) =>
        p.Nome.Contains(texto, StringComparison.OrdinalIgnoreCase)
        || (p.CodigoBarras?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false)
        || (p.Sku?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false)
        || (p.Referencia?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false);

    public IReadOnlyList<Cliente> ClientesDisponiveis
    {
        get => clientesDisponiveis;
        private set => this.RaiseAndSetIfChanged(ref clientesDisponiveis, value);
    }

    public IReadOnlyList<FormaPagamento> FormasPagamentoDisponiveis
    {
        get => formasPagamentoDisponiveis;
        private set
        {
            this.RaiseAndSetIfChanged(ref formasPagamentoDisponiveis, value);
            OpcoesPagamento = value.Select(forma => new OpcaoPagamento(forma)).ToList();
            FormaSelecionada = null;
        }
    }

    // Os cartões do painel de pagamento (um por forma de pagamento sincronizada).
    public IReadOnlyList<OpcaoPagamento> OpcoesPagamento
    {
        get => opcoesPagamento;
        private set => this.RaiseAndSetIfChanged(ref opcoesPagamento, value);
    }

    // A forma escolhida para o PRÓXIMO lançamento. Pagamento misto: escolhe uma, lança, escolhe outra…
    public FormaPagamento? FormaSelecionada
    {
        get => formaSelecionada;
        private set
        {
            this.RaiseAndSetIfChanged(ref formaSelecionada, value);
            foreach (var opcao in OpcoesPagamento)
                opcao.Selecionada = ReferenceEquals(opcao.Forma, value);
            this.RaisePropertyChanged(nameof(TemFormaSelecionada));
            this.RaisePropertyChanged(nameof(DinheiroSelecionado));
            this.RaisePropertyChanged(nameof(ExigeBandeiraSelecionada));
            this.RaisePropertyChanged(nameof(RotuloValor));
            this.RaisePropertyChanged(nameof(TrocoPrevisto));
            this.RaisePropertyChanged(nameof(PodeConfirmar));
        }
    }

    public bool TemFormaSelecionada => FormaSelecionada is not null;
    public bool DinheiroSelecionado => FormaSelecionada?.EhDinheiro ?? false;

    // Mostra a escolha de bandeira só quando a forma escolhida é cartão E já há bandeiras sincronizadas.
    public bool ExigeBandeiraSelecionada => (FormaSelecionada?.EhCartao ?? false) && TemBandeiras;

    public bool TemPagamentos => Pagamentos.Count > 0;

    // No dinheiro o campo é o que o cliente ENTREGOU (o app calcula o troco); nas demais é o valor a lançar.
    public string RotuloValor => DinheiroSelecionado ? "Valor recebido do cliente (R$)" : "Valor a lançar (R$)";

    // Troco do lançamento em digitação (dinheiro): o que o cliente entregou menos o que falta pagar.
    public decimal TrocoPrevisto =>
        DinheiroSelecionado && ValorMonetario.TentarLer(ValorPagamentoAdicionar, out var recebido)
            ? Math.Max(recebido - Restante, 0m)
            : 0m;

    // Confirmar a venda: já está toda paga, OU há uma forma escolhida a lançar (o Confirmar lança e conclui — o caso
    // de uma forma só não precisa de um clique extra em "Adicionar").
    public bool PodeConfirmar => PodeFinalizarVenda || (FormaSelecionada is not null && TemItens);

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
            this.RaisePropertyChanged(nameof(ExigeBandeiraSelecionada));
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
        set
        {
            this.RaiseAndSetIfChanged(ref valorPagamentoAdicionar, value);
            this.RaisePropertyChanged(nameof(TrocoPrevisto));   // dinheiro: o troco acompanha o que se digita
        }
    }

    public string? Mensagem
    {
        get => mensagem;
        private set => this.RaiseAndSetIfChanged(ref mensagem, value);
    }

    public decimal Total => Itens.Sum(item => item.Total);

    // Subtotal − Desconto = Total (o acréscimo entra no subtotal). Mostrados no cupom.
    public decimal Subtotal => Itens.Sum(item => item.Quantidade * item.PrecoUnitario + item.AcrescimoItem);
    public decimal TotalDescontos => Itens.Sum(item => item.DescontoItem);
    public bool TemItens => Itens.Count > 0;

    // "2 item(ns)" — o selo amarelo do cabeçalho do cupom.
    public string ResumoItens => $"{Itens.Count} item(ns)";

    // Painel de pagamento: quanto já foi pago, quanto falta e o troco (pago a mais — dinheiro).
    public decimal TotalPago => Pagamentos.Sum(p => p.Valor);
    public decimal Restante => Math.Max(Total - TotalPago, 0m);

    // O que falta pagar EM CENTAVOS, como a tela mostra (R$ {0:F2}) e como o operador digita: com total fracionado
    // (0,333 kg × 9,99 = 3,32667) o valor válido é 3,33, e comparar com 3,32667 recusaria o pagamento certo.
    private decimal FaltaEmCentavos => Math.Round(Restante, 2, MidpointRounding.AwayFromZero);
    // Troco = o que o cliente entregou a mais no DINHEIRO (só ele dá troco; ver AdicionarPagamento).
    public decimal Troco => Pagamentos.Sum(p => p.Troco);

    public bool EmPagamento
    {
        get => emPagamento;
        private set
        {
            this.RaiseAndSetIfChanged(ref emPagamento, value);
            this.RaisePropertyChanged(nameof(ModalAberto));
        }
    }

    // Precisa de item, de pagamento que COBRE o total e de pagamento que NÃO passe do total. O dinheiro nunca passa (só o
    // que falta é lançado; o resto é troco), mas o total pode CAIR depois do pagamento — tirar um item do cupom com um
    // PIX já lançado deixaria o PIX acima do total —, então isso é conferido aqui, não só na hora de lançar.
    public bool PodeFinalizarVenda => Itens.Count > 0 && TotalPago >= Total && !PagamentosPassamDoTotal;

    // Uma tolerância de 1 centavo cobre o arredondamento de totais fracionados (0,333 kg × 9,99 = 3,32667 → paga-se 3,33).
    public bool PagamentosPassamDoTotal => Pagamentos.Count > 0 && TotalPago - Total >= 0.01m;

    public string? AvisoExcesso => PagamentosPassamDoTotal
        ? $"Os pagamentos somam R$ {ValorMonetario.Formatar(TotalPago)}, acima do total de R$ {ValorMonetario.Formatar(Total)} — remova ou ajuste um pagamento."
        : null;

    // Qualquer coisa já digitada conta (item OU pagamento) — o Shell usa isso pra
    // bloquear a navegação e não descartar o que o operador já montou.
    public bool TemVendaEmAndamento => Itens.Count > 0 || Pagamentos.Count > 0;

    public ReactiveCommand<Unit, Venda?> FinalizarVendaCommand { get; }
    public ReactiveCommand<Unit, Unit> NovoCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelarCommand { get; }
    public ReactiveCommand<Produto, Unit> AdicionarItemCommand { get; }
    public ReactiveCommand<ItemCarrinho, Unit> RemoverItemCommand { get; }
    public ReactiveCommand<PagamentoAlocado, Unit> RemoverPagamentoCommand { get; }
    public ReactiveCommand<Unit, Unit> AbrirPagamentoCommand { get; }
    public ReactiveCommand<Unit, Unit> FecharPagamentoCommand { get; }
    public ReactiveCommand<Unit, Unit> AvancarCommand { get; }              // F10
    public ReactiveCommand<Unit, Unit> EscCommand { get; }                  // Esc
    public ReactiveCommand<Unit, Unit> LimparBuscaCommand { get; }
    public ReactiveCommand<Unit, Unit> AdicionarPorBuscaCommand { get; }    // Enter na busca (leitor de código de barras)
    public ReactiveCommand<OpcaoPagamento, Unit> SelecionarFormaCommand { get; }
    public ReactiveCommand<Unit, Unit> AdicionarSelecionadaCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmarVendaCommand { get; }

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

    // ---- produto sem preço no cadastro (R$ 0,00): o operador informa o preço na hora de lançar ----

    // Um produto com preço zero NÃO entra no cupom sozinho (sairia de graça por engano): o painel de preço abre e só o
    // lançamento com um preço maior que zero o coloca no cupom. O preço digitado vale só para ESTE item do cupom — não altera o
    // cadastro — e é o que a venda grava e envia à API (o preço do item já viajava na requisição).
    public Produto? ProdutoAguardandoPreco
    {
        get => produtoAguardandoPreco;
        private set
        {
            this.RaiseAndSetIfChanged(ref produtoAguardandoPreco, value);
            this.RaisePropertyChanged(nameof(PedindoPreco));
            this.RaisePropertyChanged(nameof(ModalAberto));
        }
    }

    public bool PedindoPreco => ProdutoAguardandoPreco is not null;

    // Algum painel modal está aberto por cima do cupom (pagamento ou preço): a tela de trás fica desabilitada.
    public bool ModalAberto => EmPagamento || PedindoPreco;

    public string PrecoInformado
    {
        get => precoInformado;
        set => this.RaiseAndSetIfChanged(ref precoInformado, value);
    }

    public string? MensagemPreco
    {
        get => mensagemPreco;
        private set => this.RaiseAndSetIfChanged(ref mensagemPreco, value);
    }

    public ReactiveCommand<Unit, Unit> ConfirmarPrecoCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelarPrecoCommand { get; }

    // Teto de sanidade: acima disso quase certamente é erro de digitação e o painel recusa. Erros menores, como "1250" no lugar
    // de "12,50", a tela não tem como perceber — o total do cupom fica à vista para o operador conferir.
    public const decimal PrecoMaximoInformado = 1_000_000m;

    private void PedirPreco(Produto produto)
    {
        PrecoInformado = string.Empty;
        MensagemPreco = null;
        ProdutoAguardandoPreco = produto;
    }

    private void ConfirmarPreco()
    {
        var produto = ProdutoAguardandoPreco;
        if (produto is null)
            return;

        if (!ValorMonetario.TentarLer(PrecoInformado, out var preco) || preco <= 0)
        {
            MensagemPreco = "Informe um preço maior que zero (ex: 12,50).";
            return;
        }

        if (preco > PrecoMaximoInformado)
        {
            MensagemPreco = $"Confira o valor: o preço máximo aceito é R$ {ValorMonetario.Formatar(PrecoMaximoInformado)}.";
            return;
        }

        ProdutoAguardandoPreco = null;
        MensagemPreco = null;
        LancarItem(produto, preco, precoInformado: true);
    }

    private void CancelarPreco()
    {
        ProdutoAguardandoPreco = null;
        MensagemPreco = null;
        PrecoInformado = string.Empty;
    }

    // Avisa a tela ("Sabonete" adicionado ao cupom) sem o ViewModel conhecer o canto da tela onde o aviso aparece.
    public IObservable<string> ItemLancado => itemLancado;
    private readonly Subject<string> itemLancado = new();

    // Todos os caminhos de lançamento (clique no card, Enter da busca, leitor de código de barras) passam por aqui — então a
    // regra do preço zero vale em todos eles.
    public void AdicionarItem(Produto produto)
    {
        if (produto.PrecoVenda <= 0)
        {
            PedirPreco(produto);
            return;
        }

        LancarItem(produto, produto.PrecoVenda, precoInformado: false);
    }

    private void LancarItem(Produto produto, decimal preco, bool precoInformado)
    {
        var quantidade = ValorMonetario.TentarLer(QuantidadeAdicionar, out var valor, casasDecimais: 3) && valor > 0
            ? valor
            : 1m;

        Itens.Add(new ItemCarrinho { Produto = produto, Quantidade = quantidade, PrecoUnitario = preco, PrecoInformado = precoInformado });
        itemLancado.OnNext(produto.Nome);
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

        // Já está toda paga (ou não há nada no cupom): outro lançamento só criaria um pagamento de R$ 0,00.
        var falta = FaltaEmCentavos;
        if (falta <= 0)
        {
            Mensagem = "A venda já está totalmente paga.";
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

        // Só o DINHEIRO dá troco. O que o cliente entregou (valor) pode passar do que falta: lança-se o que falta e a
        // diferença vira troco. As outras formas não passam do que falta pagar (um cartão não cobra a mais).

        PagamentoAlocado pagamento;
        if (forma.EhDinheiro)
        {
            pagamento = new PagamentoAlocado { FormaPagamento = forma, Valor = Math.Min(valor, falta), ValorRecebido = valor };
        }
        else if (valor > falta)
        {
            Mensagem = $"O valor passa do que falta pagar (R$ {ValorMonetario.Formatar(falta)}). Só o dinheiro dá troco.";
            return;
        }
        else
        {
            pagamento = new PagamentoAlocado { FormaPagamento = forma, Valor = valor, Bandeira = bandeira };
        }

        Mensagem = null;
        Pagamentos.Add(pagamento);
        BandeiraSelecionada = null;   // a escolha vale para um pagamento só (o próximo cartão pode ser de outra bandeira)
        FormaSelecionada = null;      // pagamento misto: escolhe-se a próxima forma
        ValorPagamentoAdicionar = PreencherComORestante();   // e o valor já vem com o que falta
    }

    // Clique num cartão do painel: escolhe a forma do PRÓXIMO lançamento (clicar de novo na mesma desmarca) e já sugere
    // o valor que falta. Para o dinheiro esse valor vira o "recebido", que o operador ajusta para gerar o troco.
    private void SelecionarForma(OpcaoPagamento opcao)
    {
        Mensagem = null;
        BandeiraSelecionada = null;
        if (ReferenceEquals(FormaSelecionada, opcao.Forma))
        {
            FormaSelecionada = null;
            return;
        }

        FormaSelecionada = opcao.Forma;
        ValorPagamentoAdicionar = PreencherComORestante();
    }

    // Botão "Confirmar Venda" e F10 com o painel aberto: se há uma forma escolhida e ainda falta pagar, LANÇA-a primeiro
    // (validando valor/bandeira como qualquer lançamento) e conclui se isso completou o pagamento.
    private async Task ConfirmarAsync()
    {
        if (FormaSelecionada is { } forma && Restante > 0)
        {
            AdicionarPagamento(forma);
            if (FormaSelecionada is not null)   // o lançamento foi recusado (mensagem já mostrada): fica no painel
                return;
        }

        if (!PodeFinalizarVenda)
        {
            Mensagem = PagamentosPassamDoTotal ? AvisoExcesso : $"Falta pagar R$ {ValorMonetario.Formatar(Restante)}.";
            return;
        }

        await FinalizarVendaCommand.Execute();   // passa pelo comando, pra quem escuta o resultado também ver
    }

    // O valor que o painel de pagamento sugere: o que ainda falta (vazio quando já está pago).
    private string PreencherComORestante() => Restante > 0 ? ValorMonetario.Formatar(Restante) : string.Empty;

    // ---- painel de pagamento (F10) ----

    private void AbrirPagamento()
    {
        Mensagem = null;
        ValorPagamentoAdicionar = PreencherComORestante();   // o operador só escolhe a forma
        EmPagamento = true;
    }

    // Volta ao cupom. Os pagamentos já lançados ficam; a forma escolhida e a mensagem não: reabrir começa limpo.
    private void FecharPagamento()
    {
        EmPagamento = false;
        FormaSelecionada = null;
        BandeiraSelecionada = null;
        Mensagem = null;
    }

    // F10: fecha o ciclo em duas batidas — com o painel fechado ele ABRE; com o painel aberto e o valor pago, CONFIRMA.
    private async Task AvancarAsync()
    {
        // Pedindo o preço de um produto: o F10 não paga nada por baixo do painel (o preço se confirma com Enter).
        if (PedindoPreco)
            return;

        if (!EmPagamento)
        {
            if (TemItens)
                AbrirPagamento();
            return;
        }

        await ConfirmarAsync();
    }

    // Esc: com o painel de pagamento aberto só o fecha (volta ao cupom); senão cancela a venda em andamento.
    private async Task EscAsync()
    {
        // O painel de preço é o que está por cima: o Esc o descarta (o item não entra) antes de qualquer outra coisa.
        if (PedindoPreco)
        {
            CancelarPreco();
            return;
        }

        if (EmPagamento)
        {
            FecharPagamento();
            return;
        }

        await NovoAsync();
    }

    // ---- busca (Enter = leitor de código de barras) ----

    // "Bipe o código de barras": o leitor digita o código e manda Enter. Código exato (barras, SKU ou referência) adiciona
    // o produto na hora; senão, se a busca restringiu a UM produto, adiciona esse; senão avisa (nada é adicionado às cegas).
    private void AdicionarPorBusca()
    {
        var texto = FiltroProduto.Trim();
        if (texto.Length == 0)
            return;

        var exato = ProdutosDisponiveis.FirstOrDefault(p =>
            string.Equals(p.CodigoBarras, texto, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Sku, texto, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Referencia, texto, StringComparison.OrdinalIgnoreCase));

        var filtrados = ProdutosFiltrados;
        var alvo = exato ?? (filtrados.Count == 1 ? filtrados[0] : null);
        if (alvo is null)
        {
            Mensagem = filtrados.Count == 0
                ? $"Nenhum produto encontrado para \"{texto}\"."
                : "Vários produtos encontrados — escolha um na lista.";
            return;
        }

        Mensagem = null;
        AdicionarItem(alvo);
        FiltroProduto = string.Empty;   // pronto pro próximo código
    }

    // Numeração do cupom (1., 2., 3.…): refeita a cada mudança, porque tirar o item 2 faz o 3 virar 2.
    private void Renumerar()
    {
        for (var i = 0; i < Itens.Count; i++)
            Itens[i].Numero = i + 1;
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
        CancelarPreco();
        Itens.Clear();
        Pagamentos.Clear();
        ClienteSelecionado = null;
        QuantidadeAdicionar = "1";
        ValorPagamentoAdicionar = string.Empty;
        BandeiraSelecionada = null;
        EmPagamento = false;
        FormaSelecionada = null;
        return Task.CompletedTask;
    }
}

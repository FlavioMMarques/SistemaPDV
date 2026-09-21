using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.ViewModels;

// Painel principal pós-caixa-aberto: só leitura (DashboardService), nunca dispara
// sincronização sozinho — isso é papel exclusivo do SincronizacaoBackgroundService
// (Task 50). Os comandos de navegação (NovaVenda, VerPedidos, Detalhes) não navegam sozinhos: só
// emitem, o ShellViewModel escuta e decide trocar de tela (mesmo padrão de LoginViewModel/EntrarCommand).
public class DashboardViewModel : ViewModelBase
{
    // Quantas vendas a tabela "Últimas vendas realizadas" mostra (o histórico completo é a Listagem de Pedidos).
    public const int QuantidadeUltimasVendas = 5;

    private readonly DashboardService dashboardService;
    private readonly VendaLocalService vendaLocalService;
    private readonly int? caixaId;

    private decimal faturamentoHoje;
    private int estoqueTotal;
    private int pendentesOutbox;
    private int vendasEmitidas;
    private int produtosCadastrados;
    private DateTimeOffset? ultimaSincronizacao;
    private EstadoConexao conexao;
    private IReadOnlyList<VendaResumo> ultimasVendas = Array.Empty<VendaResumo>();

    // caixaId nullable: com ExigirAberturaCaixa=false, dá pra chegar no Dashboard
    // sem nenhum caixa aberto — nesse caso "Nova Venda" fica desabilitado (não dá
    // pra vender sem caixa, Venda.CaixaId não é opcional).
    public DashboardViewModel(DashboardService dashboardService, VendaLocalService vendaLocalService, int? caixaId)
    {
        this.dashboardService = dashboardService;
        this.vendaLocalService = vendaLocalService;
        this.caixaId = caixaId;

        var temCaixa = Observable.Return(caixaId is not null);
        NovaVendaCommand = ReactiveCommand.Create(() => Unit.Default, temCaixa);
        VerPedidosCommand = ReactiveCommand.Create(() => Unit.Default, temCaixa);
        DetalhesCommand = ReactiveCommand.Create<VendaResumo, VendaResumo>(venda => venda);
    }

    public decimal FaturamentoHoje
    {
        get => faturamentoHoje;
        private set => this.RaiseAndSetIfChanged(ref faturamentoHoje, value);
    }

    public int EstoqueTotal
    {
        get => estoqueTotal;
        private set
        {
            this.RaiseAndSetIfChanged(ref estoqueTotal, value);
            this.RaisePropertyChanged(nameof(DetalheEstoque));
        }
    }

    public int PendentesOutbox
    {
        get => pendentesOutbox;
        private set => this.RaiseAndSetIfChanged(ref pendentesOutbox, value);
    }

    public int VendasEmitidas
    {
        get => vendasEmitidas;
        private set
        {
            this.RaiseAndSetIfChanged(ref vendasEmitidas, value);
            this.RaisePropertyChanged(nameof(TextoVendasEmitidas));
        }
    }

    public int ProdutosCadastrados
    {
        get => produtosCadastrados;
        private set
        {
            this.RaiseAndSetIfChanged(ref produtosCadastrados, value);
            this.RaisePropertyChanged(nameof(TextoProdutosCadastrados));
        }
    }

    public DateTimeOffset? UltimaSincronizacao
    {
        get => ultimaSincronizacao;
        private set
        {
            this.RaiseAndSetIfChanged(ref ultimaSincronizacao, value);
            this.RaisePropertyChanged(nameof(TextoUltimaSincronizacao));
        }
    }

    public EstadoConexao Conexao
    {
        get => conexao;
        private set
        {
            this.RaiseAndSetIfChanged(ref conexao, value);
            this.RaisePropertyChanged(nameof(TextoConexao));
        }
    }

    public IReadOnlyList<VendaResumo> UltimasVendas
    {
        get => ultimasVendas;
        private set
        {
            this.RaiseAndSetIfChanged(ref ultimasVendas, value);
            this.RaisePropertyChanged(nameof(SemVendas));
        }
    }

    // ---- textos dos cartões (o número grande fica no valor; a frase, aqui — testável sem tela) ----

    public string TextoVendasEmitidas => VendasEmitidas switch
    {
        0 => "Nenhuma venda emitida hoje",
        1 => "1 venda emitida hoje",
        var n => $"{n} vendas emitidas hoje",
    };

    public string TextoProdutosCadastrados => ProdutosCadastrados switch
    {
        0 => "Nenhum item cadastrado",
        1 => "1 item cadastrado",
        var n => $"{n} itens cadastrados",
    };

    public string DetalheEstoque => $"{EstoqueTotal} un em estoque local";

    // Em maiúsculas e sem emoji, como o valor do cartão no protótipo; a cor vem do estado (EstadoConexaoParaCorConverter) e o
    // texto diz o mesmo — nunca só cor.
    public string TextoConexao => Conexao switch
    {
        EstadoConexao.Online => "ONLINE",
        EstadoConexao.OnlineComFalhas => "ONLINE (COM FALHAS)",
        EstadoConexao.Offline => "MODO OFFLINE",
        _ => "NÃO VERIFICADA",
    };

    // Hora local; se não é de hoje, com a data (uma hora sozinha, de ontem, enganaria).
    public string TextoUltimaSincronizacao
    {
        get
        {
            if (UltimaSincronizacao is not { } quando)
                return "Ainda não sincronizou";

            var local = quando.ToLocalTime();
            return local.Date == DateTime.Now.Date
                ? $"Última sinc: {local:HH:mm:ss}"
                : $"Última sinc: {local:dd/MM HH:mm}";
        }
    }

    public bool SemVendas => UltimasVendas.Count == 0;

    public string TextoSemVendas => caixaId is null
        ? "Abra um caixa para começar a vender."
        : "Nenhuma venda registrada neste caixa ainda.";

    public ReactiveCommand<Unit, Unit> NovaVendaCommand { get; }
    public ReactiveCommand<Unit, Unit> VerPedidosCommand { get; }

    // Emite a venda escolhida na tabela; o Shell leva à listagem de pedidos com ela selecionada.
    public ReactiveCommand<VendaResumo, VendaResumo> DetalhesCommand { get; }

    // O Shell é quem sabe o estado da conexão (vem do serviço de sincronização) e o repassa aqui: ao abrir o painel e a cada mudança.
    public void DefinirConexao(EstadoConexao estado) => Conexao = estado;

    public async Task IniciarAsync()
    {
        var resumo = await dashboardService.ObterResumoAsync(caixaId);
        FaturamentoHoje = resumo.FaturamentoHoje;
        EstoqueTotal = resumo.EstoqueTotal;
        PendentesOutbox = resumo.PendentesOutbox;
        UltimaSincronizacao = resumo.UltimaSincronizacao;
        VendasEmitidas = resumo.VendasEmitidas;
        ProdutosCadastrados = resumo.ProdutosCadastrados;

        UltimasVendas = caixaId is { } id
            ? await vendaLocalService.ListarVendasDoCaixaAsync(id, QuantidadeUltimasVendas)
            : Array.Empty<VendaResumo>();
    }
}

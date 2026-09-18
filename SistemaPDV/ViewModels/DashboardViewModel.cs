using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// Painel principal pós-caixa-aberto: só leitura (DashboardService), nunca dispara
// sincronização sozinho — isso é papel exclusivo do SincronizacaoBackgroundService
// (Task 50). NovaVendaCommand não navega sozinho: só emite, o ShellViewModel
// escuta e decide trocar pra PdvView (mesmo padrão de LoginViewModel/EntrarCommand).
public class DashboardViewModel : ViewModelBase
{
    private readonly DashboardService dashboardService;
    private readonly int? caixaId;

    private decimal faturamentoHoje;
    private int estoqueTotal;
    private int pendentesOutbox;
    private DateTimeOffset? ultimaSincronizacao;

    // caixaId nullable: com ExigirAberturaCaixa=false, dá pra chegar no Dashboard
    // sem nenhum caixa aberto — nesse caso "Nova Venda" fica desabilitado (não dá
    // pra vender sem caixa, Venda.CaixaId não é opcional).
    public DashboardViewModel(DashboardService dashboardService, int? caixaId)
    {
        this.dashboardService = dashboardService;
        this.caixaId = caixaId;

        NovaVendaCommand = ReactiveCommand.Create(() => Unit.Default, Observable.Return(caixaId is not null));
    }

    public decimal FaturamentoHoje
    {
        get => faturamentoHoje;
        private set => this.RaiseAndSetIfChanged(ref faturamentoHoje, value);
    }

    public int EstoqueTotal
    {
        get => estoqueTotal;
        private set => this.RaiseAndSetIfChanged(ref estoqueTotal, value);
    }

    public int PendentesOutbox
    {
        get => pendentesOutbox;
        private set => this.RaiseAndSetIfChanged(ref pendentesOutbox, value);
    }

    public DateTimeOffset? UltimaSincronizacao
    {
        get => ultimaSincronizacao;
        private set => this.RaiseAndSetIfChanged(ref ultimaSincronizacao, value);
    }

    public ReactiveCommand<Unit, Unit> NovaVendaCommand { get; }

    public async Task IniciarAsync()
    {
        var resumo = await dashboardService.ObterResumoAsync(caixaId);
        FaturamentoHoje = resumo.FaturamentoHoje;
        EstoqueTotal = resumo.EstoqueTotal;
        PendentesOutbox = resumo.PendentesOutbox;
        UltimaSincronizacao = resumo.UltimaSincronizacao;
    }
}

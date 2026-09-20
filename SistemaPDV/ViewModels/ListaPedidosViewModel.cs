using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.ViewModels;

// Lista as vendas do caixa atual. "Atualiza sozinha sem F5" (Success Criterion da
// spec) vem do SincronizacaoBackgroundService (Task 50): quando um ciclo mexe no banco,
// o Shell chama AtualizarAposSincronizacaoAsync. AtualizarCommand segue como botão manual
// (decisão de 2026-09-18, mantido de apoio).
public class ListaPedidosViewModel : ViewModelBase, IAtualizavelPorSincronizacao
{
    private readonly VendaLocalService vendaLocalService;
    private readonly int caixaId;

    private IReadOnlyList<VendaResumo> vendas = Array.Empty<VendaResumo>();

    public ListaPedidosViewModel(VendaLocalService vendaLocalService, int caixaId)
    {
        this.vendaLocalService = vendaLocalService;
        this.caixaId = caixaId;

        AtualizarCommand = ReactiveCommand.CreateFromTask(CarregarAsync);
    }

    public IReadOnlyList<VendaResumo> Vendas
    {
        get => vendas;
        private set => this.RaiseAndSetIfChanged(ref vendas, value);
    }

    public ReactiveCommand<Unit, Unit> AtualizarCommand { get; }

    public Task IniciarAsync() => CarregarAsync();

    // Chamado pelo Shell quando um ciclo de sincronização mexeu no banco (o "atualiza
    // sozinha sem F5" da spec) — o botão Atualizar continua valendo.
    public Task AtualizarAposSincronizacaoAsync() => CarregarAsync();

    private async Task CarregarAsync()
    {
        Vendas = await vendaLocalService.ListarVendasDoCaixaAsync(caixaId);
    }
}

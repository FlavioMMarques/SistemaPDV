using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.ViewModels;

// Lista as vendas do caixa atual. "Atualiza sozinha sem F5" (Success Criterion da
// spec) depende de algo rodando em background sincronizando (Task 50, que ainda
// não existe) — por enquanto AtualizarCommand é manual, decisão confirmada com o
// usuário (2026-09-18). Revisitar quando SincronizacaoBackgroundService existir.
public class ListaPedidosViewModel : ViewModelBase
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

    private async Task CarregarAsync()
    {
        Vendas = await vendaLocalService.ListarVendasDoCaixaAsync(caixaId);
    }
}

using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Models;
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
    private readonly int? operadorId;

    private IReadOnlyList<VendaResumo> vendas = Array.Empty<VendaResumo>();
    private string? mensagemReenvio;
    private VendaResumo? vendaSelecionada;
    private string motivoDescarte = string.Empty;
    private string chaveSupervisor = string.Empty;
    private string? mensagemDescarte;

    // operadorId = quem PEDE o descarte (o operador logado); quem AUTORIZA é o supervisor, pela chave.
    public ListaPedidosViewModel(VendaLocalService vendaLocalService, int caixaId, int? operadorId = null)
    {
        this.vendaLocalService = vendaLocalService;
        this.caixaId = caixaId;
        this.operadorId = operadorId;

        AtualizarCommand = ReactiveCommand.CreateFromTask(CarregarAsync);
        ReenviarFalhasCommand = ReactiveCommand.CreateFromTask(ReenviarFalhasAsync);

        // Descartar: precisa de uma venda EM FALHA selecionada, do motivo e — só se a política do app exige
        // (PoliticaSupervisor) — da chave do supervisor.
        var podeDescartar = this.WhenAnyValue(vm => vm.VendaSelecionada, vm => vm.MotivoDescarte, vm => vm.ChaveSupervisor,
            (venda, motivo, chave) => venda is { SyncStatus: SyncStatus.FalhaSync } && !string.IsNullOrWhiteSpace(motivo)
                && (!ExigeChave || !string.IsNullOrEmpty(chave)));
        DescartarCommand = ReactiveCommand.CreateFromTask(DescartarAsync, podeDescartar);
    }

    public IReadOnlyList<VendaResumo> Vendas
    {
        get => vendas;
        private set => this.RaiseAndSetIfChanged(ref vendas, value);
    }

    public ReactiveCommand<Unit, Unit> AtualizarCommand { get; }

    // Vendas (e o caixa) que falharam ou desistiram voltam à fila de envio — ver PoliticaRetentativa.
    public ReactiveCommand<Unit, Unit> ReenviarFalhasCommand { get; }

    public string? MensagemReenvio
    {
        get => mensagemReenvio;
        private set => this.RaiseAndSetIfChanged(ref mensagemReenvio, value);
    }

    public VendaResumo? VendaSelecionada
    {
        get => vendaSelecionada;
        set
        {
            this.RaiseAndSetIfChanged(ref vendaSelecionada, value);
            this.RaisePropertyChanged(nameof(PodeDescartar));
        }
    }

    // O painel de descarte só aparece com uma venda EM FALHA selecionada (a pendente comum ainda vai ser enviada).
    public bool PodeDescartar => VendaSelecionada is { SyncStatus: SyncStatus.FalhaSync };

    // Se o descarte pede a chave de um supervisor (ver PoliticaSupervisor): a tela só mostra o campo quando pede.
    public bool ExigeChave => vendaLocalService.ExigeChaveSupervisor;

    public string TextoDescarte => ExigeChave
        ? "Use quando a API nunca vai aceitar esta venda. Precisa da chave de um supervisor; a venda não é apagada, fica registrada."
        : "Use quando a API nunca vai aceitar esta venda. A venda não é apagada: fica registrada como descartada, com o motivo.";

    public string MotivoDescarte
    {
        get => motivoDescarte;
        set => this.RaiseAndSetIfChanged(ref motivoDescarte, value);
    }

    // Chave do supervisor: campo de senha, LIMPO depois de cada tentativa (certa ou errada).
    public string ChaveSupervisor
    {
        get => chaveSupervisor;
        set => this.RaiseAndSetIfChanged(ref chaveSupervisor, value);
    }

    public string? MensagemDescarte
    {
        get => mensagemDescarte;
        private set => this.RaiseAndSetIfChanged(ref mensagemDescarte, value);
    }

    public ReactiveCommand<Unit, Unit> DescartarCommand { get; }

    public Task IniciarAsync() => CarregarAsync();

    // Chamado pelo Shell quando um ciclo de sincronização mexeu no banco (o "atualiza
    // sozinha sem F5" da spec) — o botão Atualizar continua valendo.
    public Task AtualizarAposSincronizacaoAsync() => CarregarAsync();

    private async Task DescartarAsync()
    {
        var venda = VendaSelecionada!;
        var chave = ChaveSupervisor;
        ChaveSupervisor = string.Empty;   // some da tela antes mesmo de conferir

        var resultado = await vendaLocalService.DescartarVendaAsync(venda.Id, chave, MotivoDescarte, operadorId);
        if (!resultado.Sucesso)
        {
            MensagemDescarte = resultado.Mensagem;   // o motivo digitado fica: o operador tenta de novo só com a chave certa
            return;
        }

        MotivoDescarte = string.Empty;
        MensagemDescarte = venda.Numero is { } numero ? $"Venda #{numero} descartada." : "Venda descartada.";
        await CarregarAsync();
        VendaSelecionada = null;
    }

    private async Task ReenviarFalhasAsync()
    {
        var reenviados = await vendaLocalService.ReenviarFalhasAsync(caixaId);
        MensagemReenvio = reenviados == 0
            ? "Nenhuma falha para reenviar."
            : $"{reenviados} item(ns) voltaram para a fila e serão enviados no próximo ciclo (até 30 s).";
        await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        Vendas = await vendaLocalService.ListarVendasDoCaixaAsync(caixaId);
    }
}

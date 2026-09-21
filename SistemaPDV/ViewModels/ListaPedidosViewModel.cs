using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.ViewModels;

// Uma escolha do filtro de status ("Todos os Status", "Sincronizado"…). Status nulo = sem filtro.
public record OpcaoStatus(string Rotulo, SyncStatus? Status);

// Lista as vendas do caixa atual. "Atualiza sozinha sem F5" (Success Criterion da
// spec) vem do SincronizacaoBackgroundService (Task 50): quando um ciclo mexe no banco,
// o Shell chama AtualizarAposSincronizacaoAsync. AtualizarCommand segue como botão manual
// (decisão de 2026-09-18, mantido de apoio).
//
// Visual do protótipo (Fase 8): 4 indicadores no topo (sobre TODAS as vendas do caixa), busca e filtros de status/forma
// de pagamento (só afetam a tabela) e o botão Detalhes de cada linha.
public class ListaPedidosViewModel : ViewModelBase, IAtualizavelPorSincronizacao
{
    public const string TodasAsFormas = "Todas Formas";

    // Fixa e na ordem do protótipo. "Pendente" inclui a venda em espera crescente (é a mesma: ainda vai ser enviada).
    public static readonly IReadOnlyList<OpcaoStatus> OpcoesStatus = new[]
    {
        new OpcaoStatus("Todos os Status", null),
        new OpcaoStatus("Sincronizado", SyncStatus.Sincronizado),
        new OpcaoStatus("Pendente de envio", SyncStatus.PendenteSync),
        new OpcaoStatus("Falha no envio", SyncStatus.FalhaSync),
        new OpcaoStatus("Descartada", SyncStatus.Descartada),
    };

    private readonly VendaLocalService vendaLocalService;
    private readonly int caixaId;
    private readonly int? operadorId;

    private IReadOnlyList<VendaResumo> vendas = Array.Empty<VendaResumo>();
    private string? mensagemReenvio;
    private VendaResumo? vendaSelecionada;
    private string motivoDescarte = string.Empty;
    private string chaveSupervisor = string.Empty;
    private string? mensagemDescarte;
    private string busca = string.Empty;
    private OpcaoStatus statusSelecionado = OpcoesStatus[0];
    private string formaSelecionada = TodasAsFormas;
    private IReadOnlyList<string> opcoesForma = new[] { TodasAsFormas };
    private DetalheVenda? detalhe;

    // operadorId = quem PEDE o descarte (o operador logado); quem AUTORIZA é o supervisor, pela chave.
    public ListaPedidosViewModel(VendaLocalService vendaLocalService, int caixaId, int? operadorId = null)
    {
        this.vendaLocalService = vendaLocalService;
        this.caixaId = caixaId;
        this.operadorId = operadorId;

        AtualizarCommand = ReactiveCommand.CreateFromTask(CarregarAsync);
        ReenviarFalhasCommand = ReactiveCommand.CreateFromTask(ReenviarFalhasAsync);

        // Só emitem: o Shell escuta e decide (navegar para o PDV, pedir a sincronização ao serviço de fundo).
        NovaVendaCommand = ReactiveCommand.Create(() => Unit.Default);
        SincronizarAgoraCommand = ReactiveCommand.Create(() => Unit.Default);

        // Detalhes: abre o modal com os itens, os pagamentos e a requisição (e seleciona a linha, que é onde mora o descarte
        // de uma venda em falha). Fechar só fica disponível com o modal aberto — assim o Esc não faz nada com ele fechado.
        DetalhesCommand = ReactiveCommand.CreateFromTask<VendaResumo>(AbrirDetalheAsync);
        FecharDetalheCommand = ReactiveCommand.Create(
            () => { Detalhe = null; },
            this.WhenAnyValue(vm => vm.Detalhe).Select(detalhe => detalhe is not null));
        LimparFiltrosCommand = ReactiveCommand.Create(LimparFiltros);

        // Descartar: precisa de uma venda EM FALHA selecionada, do motivo e — só se a política do app exige
        // (PoliticaSupervisor) — da chave do supervisor.
        var podeDescartar = this.WhenAnyValue(vm => vm.VendaSelecionada, vm => vm.MotivoDescarte, vm => vm.ChaveSupervisor,
            (venda, motivo, chave) => venda is { SyncStatus: SyncStatus.FalhaSync } && !string.IsNullOrWhiteSpace(motivo)
                && (!ExigeChave || !string.IsNullOrEmpty(chave)));
        DescartarCommand = ReactiveCommand.CreateFromTask(DescartarAsync, podeDescartar);
    }

    // Todas as vendas do caixa (a base dos indicadores); a tabela mostra VendasFiltradas.
    public IReadOnlyList<VendaResumo> Vendas
    {
        get => vendas;
        private set
        {
            this.RaiseAndSetIfChanged(ref vendas, value);
            OpcoesForma = new[] { TodasAsFormas }
                .Concat(value.SelectMany(NomesDasFormas).Distinct().OrderBy(nome => nome, StringComparer.CurrentCultureIgnoreCase))
                .ToList();
            AvisarMudancaDeLista();
            this.RaisePropertyChanged(nameof(TotalPedidos));
            this.RaisePropertyChanged(nameof(VolumeFaturado));
            this.RaisePropertyChanged(nameof(Sincronizados));
            this.RaisePropertyChanged(nameof(Pendentes));
            this.RaisePropertyChanged(nameof(DetalhePendentes));
            this.RaisePropertyChanged(nameof(TemFalhas));
        }
    }

    // ---- indicadores (todas as vendas do caixa; a descartada é tratada como cancelada e não entra) ----

    public int TotalPedidos => Vendas.Count(v => v.SyncStatus != SyncStatus.Descartada);

    public decimal VolumeFaturado => Vendas.Where(v => v.SyncStatus != SyncStatus.Descartada).Sum(v => v.Total);

    public int Sincronizados => Vendas.Count(v => v.SyncStatus == SyncStatus.Sincronizado);

    // Ainda não chegaram à API: pendentes e em falha (a descartada não vai mais).
    public int Pendentes => Vendas.Count(v => v.SyncStatus is SyncStatus.PendenteSync or SyncStatus.FalhaSync);

    public int Falhas => Vendas.Count(v => v.SyncStatus == SyncStatus.FalhaSync);

    // O botão "Reenviar falhas" só aparece quando há o que reenviar.
    public bool TemFalhas => Falhas > 0;

    public string DetalhePendentes => Falhas > 0 ? $"{Falhas} com falha de envio" : "Aguardando disparo assíncrono";

    // "Carlos Silva" + "Caixa 01" na coluna Operador: o caixa é o mesmo de todas as linhas.
    public string RotuloCaixa => $"Caixa {caixaId:00}";

    // ---- busca e filtros da tabela ----

    public string Busca
    {
        get => busca;
        set
        {
            this.RaiseAndSetIfChanged(ref busca, value);
            AvisarMudancaDeLista();
        }
    }

    public OpcaoStatus StatusSelecionado
    {
        get => statusSelecionado;
        set
        {
            if (value is null)
                return;

            this.RaiseAndSetIfChanged(ref statusSelecionado, value);
            AvisarMudancaDeLista();
        }
    }

    // As formas que realmente aparecem nas vendas do caixa (uma venda com duas formas conta nas duas). A lista só é
    // trocada quando o CONTEÚDO muda: recarregar a cada ciclo de sincronização e reatribuir a mesma lista faria o ComboBox
    // perder a forma que o operador escolheu.
    public IReadOnlyList<string> OpcoesForma
    {
        get => opcoesForma;
        private set
        {
            if (!opcoesForma.SequenceEqual(value))
                this.RaiseAndSetIfChanged(ref opcoesForma, value);
        }
    }

    public string FormaSelecionada
    {
        get => formaSelecionada;
        set
        {
            // O ComboBox pode mandar null por um instante ao trocar a lista de opções: isso não é uma escolha do operador.
            if (value is null)
                return;

            this.RaiseAndSetIfChanged(ref formaSelecionada, value);
            AvisarMudancaDeLista();
        }
    }

    public IReadOnlyList<VendaResumo> VendasFiltradas => Vendas.Where(Passa).ToList();

    public bool TemFiltro => busca.Trim().Length > 0 || statusSelecionado.Status is not null || formaSelecionada != TodasAsFormas;

    // Vazio por falta de vendas é diferente de vazio por causa dos filtros: a mensagem (e o botão de limpar) dizem qual.
    public bool SemPedidos => VendasFiltradas.Count == 0;

    public string TextoSemPedidos => Vendas.Count == 0
        ? "Nenhum pedido registrado neste caixa ainda."
        : "Nenhum pedido encontrado com esses filtros.";

    public bool PodeLimparFiltros => Vendas.Count > 0 && TemFiltro;

    public ReactiveCommand<Unit, Unit> AtualizarCommand { get; }

    // Vendas (e o caixa) que falharam ou desistiram voltam à fila de envio — ver PoliticaRetentativa.
    public ReactiveCommand<Unit, Unit> ReenviarFalhasCommand { get; }

    public ReactiveCommand<Unit, Unit> NovaVendaCommand { get; }
    public ReactiveCommand<Unit, Unit> SincronizarAgoraCommand { get; }
    public ReactiveCommand<VendaResumo, Unit> DetalhesCommand { get; }

    // ---- modal "Detalhes do pedido" ----

    public DetalheVenda? Detalhe
    {
        get => detalhe;
        private set
        {
            this.RaiseAndSetIfChanged(ref detalhe, value);
            this.RaisePropertyChanged(nameof(DetalheAberto));
            this.RaisePropertyChanged(nameof(DetalheTitulo));
            this.RaisePropertyChanged(nameof(DetalheDataHora));
            this.RaisePropertyChanged(nameof(DetalheTemDesconto));
            this.RaisePropertyChanged(nameof(DetalheOperador));
            this.RaisePropertyChanged(nameof(DetalheIdNaApi));
            this.RaisePropertyChanged(nameof(DetalheNotaRequisicao));
        }
    }

    public bool DetalheAberto => Detalhe is not null;

    public string DetalheTitulo => Detalhe?.Resumo.Numero is { } numero ? $"Detalhes do Pedido #{numero}" : "Detalhes do Pedido";

    public bool DetalheTemDesconto => Detalhe?.Desconto > 0;

    public string DetalheDataHora => Detalhe is { } d ? $"{d.Resumo.DataHora:dd/MM/yyyy} às {d.Resumo.DataHora:HH:mm:ss}" : string.Empty;

    public string DetalheOperador => Detalhe is { } d ? $"{d.Resumo.OperadorNome} ({RotuloCaixa})" : string.Empty;

    public string DetalheIdNaApi => Detalhe?.VendaIdExterno is { } id ? $"Id na API: {id}" : string.Empty;

    // Diz em que pé a requisição mostrada está: aceita, recusada ou ainda por enviar (ela é reconstruída da venda gravada).
    // Sem requisição para mostrar (falta sincronizar algo, ou foi descartada) não há o que comentar: o motivo já ocupa o lugar.
    public string DetalheNotaRequisicao => Detalhe?.Requisicao is null ? string.Empty : Detalhe.Resumo.SyncStatus switch
    {
        SyncStatus.Sincronizado => "Enviada e aceita pela API. Reconstruída a partir da venda gravada.",
        SyncStatus.FalhaSync => "A API recusou esta requisição — veja o motivo acima.",
        SyncStatus.PendenteSync => "Ainda não enviada: sai no próximo ciclo de sincronização.",
        _ => string.Empty,
    };

    public ReactiveCommand<Unit, Unit> FecharDetalheCommand { get; }

    // Público para o Shell abrir o modal direto quando o operador chega aqui pelo "Detalhes" do painel principal.
    public async Task AbrirDetalheAsync(VendaResumo venda)
    {
        VendaSelecionada = venda;
        Detalhe = await vendaLocalService.ObterDetalheAsync(venda.Id);
    }
    public ReactiveCommand<Unit, Unit> LimparFiltrosCommand { get; }

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

    // "Dinheiro, Pix" -> ["Dinheiro", "Pix"] (VendaResumo junta os nomes numa frase só).
    private static IEnumerable<string> NomesDasFormas(VendaResumo venda) =>
        venda.FormasPagamento.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Sem acento e sem diferença de maiúscula: "joao" acha "João" e "cafe" acha "Café".
    private static bool Contem(string texto, string trecho) =>
        CultureInfo.CurrentCulture.CompareInfo.IndexOf(texto, trecho, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private bool Passa(VendaResumo venda)
    {
        if (statusSelecionado.Status is { } status && venda.SyncStatus != status)
            return false;

        if (formaSelecionada != TodasAsFormas && !NomesDasFormas(venda).Contains(formaSelecionada, StringComparer.CurrentCultureIgnoreCase))
            return false;

        // Busca por número do pedido ("1003" ou "#1003") ou por parte do nome do cliente.
        var termo = busca.Trim();
        if (termo.Length == 0)
            return true;

        var numero = termo.TrimStart('#');
        return (numero.Length > 0 && venda.Numero?.ToString().Contains(numero) == true) || Contem(venda.ClienteNome, termo);
    }

    private void LimparFiltros()
    {
        Busca = string.Empty;
        StatusSelecionado = OpcoesStatus[0];
        FormaSelecionada = TodasAsFormas;
    }

    private void AvisarMudancaDeLista()
    {
        this.RaisePropertyChanged(nameof(VendasFiltradas));
        this.RaisePropertyChanged(nameof(SemPedidos));
        this.RaisePropertyChanged(nameof(TextoSemPedidos));
        this.RaisePropertyChanged(nameof(TemFiltro));
        this.RaisePropertyChanged(nameof(PodeLimparFiltros));
    }

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

        // A forma escolhida pode ter sumido da lista (a venda que a tinha foi descartada, por exemplo): volta a "Todas".
        if (formaSelecionada != TodasAsFormas && !OpcoesForma.Contains(formaSelecionada))
            FormaSelecionada = TodasAsFormas;

        // Modal aberto durante um ciclo de sincronização: o selo e a requisição acompanham (a venda pode ter acabado de subir).
        if (Detalhe is { } aberto)
            Detalhe = await vendaLocalService.ObterDetalheAsync(aberto.Resumo.Id);
    }
}

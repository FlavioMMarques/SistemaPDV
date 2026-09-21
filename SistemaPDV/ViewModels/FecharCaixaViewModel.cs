using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.ViewModels;

// Uma linha da conferência: quanto as vendas somaram (Esperado) e quanto o operador contou (Contado, texto porque o
// TextBox liga em string — validado só na hora de fechar). Base comum das linhas por forma de pagamento e por bandeira.
public abstract class LinhaContagem : ReactiveObject
{
    private string contado;
    private readonly Action aoMudar;

    protected LinhaContagem(string nome, decimal esperado, Action aoMudar)
    {
        Nome = nome;
        Esperado = esperado;
        contado = ValorMonetario.Formatar(esperado);   // pré-preenchido: o operador só ajusta o que divergiu
        this.aoMudar = aoMudar;
    }

    public string Nome { get; }
    public decimal Esperado { get; }

    public string Contado
    {
        get => contado;
        set
        {
            this.RaiseAndSetIfChanged(ref contado, value);
            aoMudar();
        }
    }
}

// Apuração por forma de pagamento.
public class LinhaApuracao : LinhaContagem
{
    public LinhaApuracao(int formaPagamentoId, string nome, decimal esperado, Action aoMudar) : base(nome, esperado, aoMudar) =>
        FormaPagamentoId = formaPagamentoId;

    public int FormaPagamentoId { get; }
}

// Apuração por bandeira de cartão (o Nome é o da bandeira, ex: "MASTERCARD").
public class LinhaBandeira : LinhaContagem
{
    public LinhaBandeira(string bandeira, decimal esperado, Action aoMudar) : base(bandeira, esperado, aoMudar)
    {
    }
}

// Fechar caixa (Task 51): o operador confere quanto apurou por forma de pagamento e informa o troco final.
// Só grava LOCAL (CaixaService.FecharCaixaLocalAsync) — o envio à API é do serviço de sincronização, então
// fechar funciona offline. Mesmo padrão de AbrirCaixaViewModel: ConfirmarCommand devolve o Caixa fechado (ou null)
// como resultado observável, e CancelarCommand só emite; quem navega é o Shell.
//
// Também apura por BANDEIRA de cartão (digitacao_bandeiras): uma linha para cada bandeira que aparece nos pagamentos das
// vendas deste caixa (a bandeira é escolhida no pagamento; o "esperado" vem da soma por bandeira).
public class FecharCaixaViewModel : ViewModelBase, IAtualizavelPorSincronizacao
{
    private readonly CaixaService caixaService;
    private readonly VendaLocalService vendaLocalService;
    private readonly int caixaId;

    private IReadOnlyList<LinhaApuracao> formas = Array.Empty<LinhaApuracao>();
    private IReadOnlyList<LinhaBandeira> bandeiras = Array.Empty<LinhaBandeira>();
    private string trocoFinal = ValorMonetario.Formatar(0m);
    private decimal totalVendido;
    private bool apuracaoValida = true;
    private int vendasPendentes;
    private string? mensagem;

    public FecharCaixaViewModel(CaixaService caixaService, VendaLocalService vendaLocalService, int caixaId)
    {
        this.caixaService = caixaService;
        this.vendaLocalService = vendaLocalService;
        this.caixaId = caixaId;

        // Só fecha se NÃO há venda pendente (regra de negócio) — o serviço também recusa, isto só evita o clique à toa.
        var podeConfirmar = this.WhenAnyValue(vm => vm.TrocoFinal, vm => vm.ApuracaoValida, vm => vm.VendasPendentes,
            (troco, apuracao, pendentes) => pendentes == 0 && apuracao && ValorMonetario.TentarLer(troco, out var valor) && valor >= 0);
        ConfirmarCommand = ReactiveCommand.CreateFromTask(ConfirmarAsync, podeConfirmar);
        CancelarCommand = ReactiveCommand.Create(() => Unit.Default);
        AtualizarCommand = ReactiveCommand.CreateFromTask(RecontarVendasPendentesAsync);
    }

    public IReadOnlyList<LinhaApuracao> Formas
    {
        get => formas;
        private set
        {
            this.RaiseAndSetIfChanged(ref formas, value);
            this.RaisePropertyChanged(nameof(SemVendas));
        }
    }

    // Uma linha por bandeira de cartão que aparece nas vendas deste caixa. Vazia = nenhuma venda em cartão com bandeira
    // (a seção some da tela).
    public IReadOnlyList<LinhaBandeira> Bandeiras
    {
        get => bandeiras;
        private set
        {
            this.RaiseAndSetIfChanged(ref bandeiras, value);
            this.RaisePropertyChanged(nameof(TemBandeiras));
        }
    }

    public bool TemBandeiras => Bandeiras.Count > 0;

    // Estado vazio: caixa sem nenhuma venda (a tela avisa em vez de mostrar uma lista em branco).
    public bool SemVendas => Formas.Count == 0;

    public string TrocoFinal
    {
        get => trocoFinal;
        set => this.RaiseAndSetIfChanged(ref trocoFinal, value);
    }

    public decimal TotalVendido
    {
        get => totalVendido;
        private set => this.RaiseAndSetIfChanged(ref totalVendido, value);
    }

    // Recalculada quando qualquer "Contado" muda (as linhas avisam) — o comando escuta isso.
    public bool ApuracaoValida
    {
        get => apuracaoValida;
        private set => this.RaiseAndSetIfChanged(ref apuracaoValida, value);
    }

    // Vendas deste caixa que ainda não chegaram à API — enquanto houver, não dá pra fechar.
    public int VendasPendentes
    {
        get => vendasPendentes;
        private set
        {
            this.RaiseAndSetIfChanged(ref vendasPendentes, value);
            this.RaisePropertyChanged(nameof(AvisoVendasPendentes));
        }
    }

    public string? AvisoVendasPendentes => VendasPendentes == 0 ? null : CaixaService.MensagemVendasNaoEnviadas(VendasPendentes);

    public string? Mensagem
    {
        get => mensagem;
        private set => this.RaiseAndSetIfChanged(ref mensagem, value);
    }

    public ReactiveCommand<Unit, Models.Caixa?> ConfirmarCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelarCommand { get; }

    // "Verificar de novo": a sincronização roda sozinha em segundo plano; o botão só reconta na hora.
    public ReactiveCommand<Unit, Unit> AtualizarCommand { get; }

    public async Task IniciarAsync()
    {
        var totais = await vendaLocalService.TotaisPorFormaPagamentoAsync(caixaId);

        Formas = totais.Select(t => new LinhaApuracao(t.FormaPagamentoId, t.Nome, t.Total, Revalidar)).ToList();
        Bandeiras = (await vendaLocalService.TotaisPorBandeiraAsync(caixaId))
            .Select(t => new LinhaBandeira(t.Bandeira, t.Total, Revalidar))
            .ToList();
        TotalVendido = totais.Sum(t => t.Total);
        VendasPendentes = await vendaLocalService.ContarNaoEnviadasAsync(caixaId);
        Revalidar();
    }

    // O Shell chama quando um ciclo de sincronização mexeu no banco: as vendas pendentes podem ter saído.
    public Task AtualizarAposSincronizacaoAsync() => RecontarVendasPendentesAsync();

    private async Task RecontarVendasPendentesAsync() =>
        VendasPendentes = await vendaLocalService.ContarNaoEnviadasAsync(caixaId);

    // Formas E bandeiras: um valor inválido em qualquer linha trava o botão.
    private void Revalidar() =>
        ApuracaoValida = Formas.Cast<LinhaContagem>().Concat(Bandeiras)
            .All(l => ValorMonetario.TentarLer(l.Contado, out var valor) && valor >= 0);

    private async Task<Models.Caixa?> ConfirmarAsync()
    {
        Mensagem = null;

        ValorMonetario.TentarLer(TrocoFinal, out var troco);   // já validado pelo CanExecute do comando
        var digitacoes = Formas
            .Select(f =>
            {
                ValorMonetario.TentarLer(f.Contado, out var valor);
                return (f.FormaPagamentoId, valor);
            })
            .ToList();

        var digitacoesBandeiras = Bandeiras
            .Select(b =>
            {
                ValorMonetario.TentarLer(b.Contado, out var valor);
                return (b.Nome, valor);
            })
            .ToList();

        var resultado = await caixaService.FecharCaixaLocalAsync(caixaId, troco, digitacoes, digitacoesBandeiras);
        if (!resultado.Sucesso)
        {
            Mensagem = resultado.Mensagem;
            return null;
        }

        return resultado.Valor;
    }
}

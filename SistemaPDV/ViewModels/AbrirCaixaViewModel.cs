using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.ViewModels;

// Tela de abertura de caixa: só troco inicial + turno. funcionarioId vem de quem
// navegou pra cá (ShellViewModel, já sabe o operador logado) — não é a tela que
// resolve login. Mesmo padrão de LoginViewModel: AbrirCommand devolve o Caixa
// aberto (ou null) como resultado observável, sem acoplar a quem escuta.
public class AbrirCaixaViewModel : ViewModelBase
{
    private readonly CaixaService caixaService;
    private readonly int funcionarioId;

    private string trocoInicial = string.Empty;
    private int turno = 1;
    private string? mensagem;

    public AbrirCaixaViewModel(CaixaService caixaService, int funcionarioId)
    {
        this.caixaService = caixaService;
        this.funcionarioId = funcionarioId;

        var podeAbrir = this.WhenAnyValue(vm => vm.TrocoInicial,
            troco => ValorMonetario.TentarLer(troco, out var valor) && valor >= 0);
        AbrirCommand = ReactiveCommand.CreateFromTask(AbrirAsync, podeAbrir);
    }

    // 1..6, os turnos do SoftcomShop — a tela lista isso em vez de itens fixos no XAML.
    public IReadOnlyList<int> Turnos { get; } =
        Enumerable.Range(CaixaService.TurnoMinimo, CaixaService.TurnoMaximo - CaixaService.TurnoMinimo + 1).ToList();

    public string TrocoInicial
    {
        get => trocoInicial;
        set => this.RaiseAndSetIfChanged(ref trocoInicial, value);
    }

    public int Turno
    {
        get => turno;
        set => this.RaiseAndSetIfChanged(ref turno, value);
    }

    public string? Mensagem
    {
        get => mensagem;
        private set => this.RaiseAndSetIfChanged(ref mensagem, value);
    }

    public ReactiveCommand<Unit, Models.Caixa?> AbrirCommand { get; }

    // Sugere o primeiro turno de hoje ainda não usado por este operador: depois de fechar o turno 1, a tela voltava
    // sugerindo "Turno 1" e a abertura era recusada. Se todos já foram usados, mantém o turno atual (a mensagem explica).
    public async Task IniciarAsync()
    {
        var usados = await caixaService.TurnosUsadosAsync(funcionarioId, DateOnly.FromDateTime(DateTime.Now));
        var livre = Turnos.Where(t => !usados.Contains(t)).Cast<int?>().FirstOrDefault();
        if (livre is { } proximo)
            Turno = proximo;
    }

    private async Task<Models.Caixa?> AbrirAsync()
    {
        Mensagem = null;

        ValorMonetario.TentarLer(TrocoInicial, out var trocoValor);   // já validado pelo CanExecute do comando
        var dataCaixa = DateOnly.FromDateTime(DateTime.Now);

        var resultado = await caixaService.AbrirCaixaLocalAsync(funcionarioId, dataCaixa, Turno, trocoValor);
        if (!resultado.Sucesso)
        {
            Mensagem = resultado.Mensagem;
            return null;
        }

        return resultado.Valor;
    }
}

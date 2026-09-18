using System;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
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
            troco => decimal.TryParse(troco, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) && valor >= 0);
        AbrirCommand = ReactiveCommand.CreateFromTask(AbrirAsync, podeAbrir);
    }

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

    private async Task<Models.Caixa?> AbrirAsync()
    {
        Mensagem = null;

        var trocoValor = decimal.Parse(TrocoInicial, NumberStyles.Number, CultureInfo.InvariantCulture);
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

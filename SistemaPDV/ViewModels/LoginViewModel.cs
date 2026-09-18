using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.ViewModels;

// Login local, sem rede — só chama LoginOperadorService.AutenticarAsync, que já faz
// toda a comparação de hash. EntrarCommand devolve o Funcionario autenticado (ou
// null) como resultado da execução: quem hospeda esse ViewModel (ShellViewModel, a
// partir da Task 42) se inscreve em EntrarCommand como observable pra saber quando
// navegar adiante, sem precisar de um evento customizado.
public class LoginViewModel : ViewModelBase
{
    private readonly LoginOperadorService loginOperadorService;
    private string pdvKeyDigitada = string.Empty;
    private string? mensagemErro;

    public LoginViewModel(LoginOperadorService loginOperadorService)
    {
        this.loginOperadorService = loginOperadorService;

        var podeEntrar = this.WhenAnyValue(vm => vm.PdvKeyDigitada, chave => !string.IsNullOrWhiteSpace(chave));
        EntrarCommand = ReactiveCommand.CreateFromTask(EntrarAsync, podeEntrar);
    }

    public string PdvKeyDigitada
    {
        get => pdvKeyDigitada;
        set => this.RaiseAndSetIfChanged(ref pdvKeyDigitada, value);
    }

    public string? MensagemErro
    {
        get => mensagemErro;
        private set => this.RaiseAndSetIfChanged(ref mensagemErro, value);
    }

    public ReactiveCommand<Unit, Funcionario?> EntrarCommand { get; }

    private async Task<Funcionario?> EntrarAsync()
    {
        MensagemErro = null;

        var funcionario = await loginOperadorService.AutenticarAsync(PdvKeyDigitada);

        // Mensagem genérica de propósito — não revela se a chave existe ou não,
        // nem se o funcionário está desativado, pra não dar pista útil num ataque
        // de força bruta contra a pdv_key.
        if (funcionario is null)
            MensagemErro = "Chave inválida.";

        return funcionario;
    }
}

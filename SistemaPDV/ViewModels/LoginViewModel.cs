using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.ViewModels;

// Login local, sem rede: o operador escolhe o próprio nome e digita a chave; LoginOperadorService.AutenticarAsync confere a
// chave contra o hash DELE. EntrarCommand devolve o Funcionario autenticado (ou null) como resultado da execução: quem
// hospeda esse ViewModel (ShellViewModel, a partir da Task 42) se inscreve em EntrarCommand como observable pra saber
// quando navegar adiante, sem precisar de um evento customizado.
//
// A lista de operadores vem do catálogo sincronizado. Na primeira vez que o aparelho é vinculado ela pode estar vazia (o
// catálogo ainda não chegou): por isso a tela se recarrega quando a sincronização termina (IAtualizavelPorSincronizacao).
public class LoginViewModel : ViewModelBase, IAtualizavelPorSincronizacao
{
    private readonly LoginOperadorService loginOperadorService;
    private IReadOnlyList<OperadorLogin> operadores = Array.Empty<OperadorLogin>();
    private OperadorLogin? operadorSelecionado;
    private string pdvKeyDigitada = string.Empty;
    private string? mensagemErro;
    private bool carregou;

    public LoginViewModel(LoginOperadorService loginOperadorService)
    {
        this.loginOperadorService = loginOperadorService;

        // Sem operador escolhido o botão fica desabilitado: a tela nunca tenta "adivinhar" de quem é a chave.
        var podeEntrar = this.WhenAnyValue(
            vm => vm.OperadorSelecionado,
            vm => vm.PdvKeyDigitada,
            (operador, chave) => operador is not null && !string.IsNullOrWhiteSpace(chave));
        EntrarCommand = ReactiveCommand.CreateFromTask(EntrarAsync, podeEntrar);
    }

    public IReadOnlyList<OperadorLogin> Operadores
    {
        get => operadores;
        private set
        {
            this.RaiseAndSetIfChanged(ref operadores, value);
            this.RaisePropertyChanged(nameof(SemOperadores));
        }
    }

    public OperadorLogin? OperadorSelecionado
    {
        get => operadorSelecionado;
        set
        {
            if (Equals(operadorSelecionado, value))
                return;

            this.RaiseAndSetIfChanged(ref operadorSelecionado, value);

            // Trocar de operador começa do zero: a chave e o erro eram de outra pessoa.
            PdvKeyDigitada = string.Empty;
            MensagemErro = null;
        }
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

    // Só vale depois de a 1ª leitura terminar: antes disso "não há operadores" seria mentira (ainda não olhamos).
    public bool SemOperadores => carregou && Operadores.Count == 0;

    public ReactiveCommand<Unit, Funcionario?> EntrarCommand { get; }

    public async Task CarregarOperadoresAsync()
    {
        var lista = await loginOperadorService.ListarOperadoresAsync();

        // A sincronização recarrega esta tela a cada ciclo: lista igual = não mexe em nada (trocar os itens faria o
        // ListBox zerar a escolha e apagar a chave que o operador está digitando).
        if (carregou && lista.SequenceEqual(Operadores))
            return;

        // A lista mudou de verdade (operador novo, desativado, nome corrigido). Quem estava escolhido continua escolhido —
        // com a chave já digitada — a menos que tenha saído da lista (desativado no SoftcomShop): aí a escolha se desfaz.
        var idEscolhido = OperadorSelecionado?.Id;
        var chave = PdvKeyDigitada;

        carregou = true;
        Operadores = lista;

        var escolhido = idEscolhido is null ? null : lista.FirstOrDefault(o => o.Id == idEscolhido);
        OperadorSelecionado = escolhido;
        if (escolhido is not null)
            PdvKeyDigitada = chave;   // o setter do operador a apagou ao trocar o objeto
    }

    public Task AtualizarAposSincronizacaoAsync() => CarregarOperadoresAsync();

    private async Task<Funcionario?> EntrarAsync()
    {
        MensagemErro = null;

        if (OperadorSelecionado is not { } operador)
            return null;

        // Em espera (erros seguidos): nem tenta — o serviço ignoraria mesmo a chave certa.
        if (loginOperadorService.EsperaRestante > TimeSpan.Zero)
        {
            MensagemErro = TextoDeEspera(loginOperadorService.EsperaRestante);
            return null;
        }

        var funcionario = await loginOperadorService.AutenticarAsync(operador.Id, PdvKeyDigitada);

        // Mensagem genérica de propósito — não revela se o operador foi desativado nem o que errou, pra não dar pista
        // útil num ataque de força bruta contra a pdv_key.
        if (funcionario is null)
        {
            var espera = loginOperadorService.EsperaRestante;
            MensagemErro = espera > TimeSpan.Zero ? $"Chave inválida. {TextoDeEspera(espera)}" : "Chave inválida.";
        }

        return funcionario;
    }

    private static string TextoDeEspera(TimeSpan espera) =>
        $"Muitas tentativas erradas — aguarde {LimitadorDeTentativas.Descrever(espera)} para tentar de novo.";
}

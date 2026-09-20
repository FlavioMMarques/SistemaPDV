using System;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// Provisionamento do dispositivo (link de cadastro -> client_secret protegido) e
// edição dos campos de ConfiguracaoSincronizacao. IniciarAsync carrega o estado
// atual — chamado explicitamente por quem navega pra essa tela (não no construtor,
// que não pode ser async), mesmo raciocínio de qualquer inicialização assíncrona
// de ViewModel em MVVM.
public class ConfiguracoesViewModel : ViewModelBase
{
    private readonly ConfiguracaoService configuracaoService;

    private string linkCadastro = string.Empty;
    private string nomeDispositivo = string.Empty;
    private string clienteConsumidorFinalIdExterno = string.Empty;
    private bool exigirAberturaCaixa = true;
    private string? mensagem;

    // Marcado aqui (não a classe inteira) porque só referencia VincularAsync como
    // delegate do comando — IniciarAsync/SalvarAsync não têm nada de Windows-only e
    // ficam sem a restrição. Como o app inteiro só roda em Windows de qualquer forma
    // (ver App.axaml.cs), quem for construir esse ViewModel já está dentro dessa
    // cadeia — não é uma restrição nova se propagando, só reconhecendo a que já existe.
    [SupportedOSPlatform("windows")]
    public ConfiguracoesViewModel(ConfiguracaoService configuracaoService)
    {
        this.configuracaoService = configuracaoService;

        var podeVincular = this.WhenAnyValue(
            vm => vm.LinkCadastro, vm => vm.NomeDispositivo,
            (link, nome) => !string.IsNullOrWhiteSpace(link) && !string.IsNullOrWhiteSpace(nome));
        VincularCommand = ReactiveCommand.CreateFromTask(VincularAsync, podeVincular);

        SalvarCommand = ReactiveCommand.CreateFromTask(SalvarAsync);
    }

    public string LinkCadastro
    {
        get => linkCadastro;
        set => this.RaiseAndSetIfChanged(ref linkCadastro, value);
    }

    public string NomeDispositivo
    {
        get => nomeDispositivo;
        set => this.RaiseAndSetIfChanged(ref nomeDispositivo, value);
    }

    // Texto, não int?, porque o TextBox do Avalonia liga em string — convertido só
    // na hora de salvar (SalvarAsync), com fallback silencioso pra null se não for
    // um número válido (não trava a tela por um typo, só não persiste o valor).
    public string ClienteConsumidorFinalIdExterno
    {
        get => clienteConsumidorFinalIdExterno;
        set => this.RaiseAndSetIfChanged(ref clienteConsumidorFinalIdExterno, value);
    }

    public bool ExigirAberturaCaixa
    {
        get => exigirAberturaCaixa;
        set => this.RaiseAndSetIfChanged(ref exigirAberturaCaixa, value);
    }

    public string? Mensagem
    {
        get => mensagem;
        private set => this.RaiseAndSetIfChanged(ref mensagem, value);
    }

    // Devolve se vinculou: o Shell escuta pra levar ao Login (e avisar a sincronização)
    // só quando deu certo — com falha, a tela fica pro operador corrigir o link.
    public ReactiveCommand<Unit, bool> VincularCommand { get; }
    public ReactiveCommand<Unit, Unit> SalvarCommand { get; }

    public async Task IniciarAsync()
    {
        var configuracao = await configuracaoService.ObterOuCriarAsync();
        LinkCadastro = configuracao.UrlApi ?? string.Empty;
        NomeDispositivo = configuracao.NomeDispositivo ?? string.Empty;
        ClienteConsumidorFinalIdExterno = configuracao.ClienteConsumidorFinalIdExterno?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ExigirAberturaCaixa = configuracao.ExigirAberturaCaixa;
    }

    // Único método que chama VincularDispositivoAsync (DPAPI, Windows-only) —
    // mesma granularidade de todo o resto do projeto (ver AppServices).
    [SupportedOSPlatform("windows")]
    private async Task<bool> VincularAsync()
    {
        var (sucesso, mensagemResultado) = await configuracaoService.VincularDispositivoAsync(LinkCadastro, NomeDispositivo);
        Mensagem = mensagemResultado;
        return sucesso;
    }

    private async Task SalvarAsync()
    {
        var clienteConsumidorFinalId = int.TryParse(ClienteConsumidorFinalIdExterno, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : (int?)null;

        await configuracaoService.AtualizarAsync(c =>
        {
            c.ExigirAberturaCaixa = ExigirAberturaCaixa;
            c.ClienteConsumidorFinalIdExterno = clienteConsumidorFinalId;
        });

        Mensagem = "Configurações salvas.";
    }
}

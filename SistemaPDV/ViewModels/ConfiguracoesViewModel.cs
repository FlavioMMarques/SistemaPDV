using System;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;
using SistemaPDV.Services.Sales;

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
    private string codigoPdv = string.Empty;
    private bool exigirAberturaCaixa = true;
    private string? mensagem;
    private bool bloqueada;
    private string chaveSupervisor = string.Empty;
    private string? mensagemDesbloqueio;

    // Marcado aqui (não a classe inteira) porque só referencia VincularAsync como
    // delegate do comando — IniciarAsync/SalvarAsync não têm nada de Windows-only e
    // ficam sem a restrição. Como o app inteiro só roda em Windows de qualquer forma
    // (ver App.axaml.cs), quem for construir esse ViewModel já está dentro dessa
    // cadeia — não é uma restrição nova se propagando, só reconhecendo a que já existe.
    // exigirSupervisor: aberta pelo botão da barra (dispositivo já vinculado) — o formulário só aparece depois da chave
    // de um supervisor. Na 1ª vez (sem vínculo) não há como logar nem supervisor local, então abre direto.
    [SupportedOSPlatform("windows")]
    public ConfiguracoesViewModel(ConfiguracaoService configuracaoService, bool exigirSupervisor = false)
    {
        this.configuracaoService = configuracaoService;
        PodeVoltar = exigirSupervisor;
        // Só bloqueia se a política do app exige a chave (PoliticaSupervisor); "Voltar" existe de qualquer jeito.
        bloqueada = exigirSupervisor && configuracaoService.ExigeChaveSupervisor;

        var podeDesbloquear = this.WhenAnyValue(vm => vm.ChaveSupervisor, chave => !string.IsNullOrEmpty(chave));
        DesbloquearCommand = ReactiveCommand.CreateFromTask(DesbloquearAsync, podeDesbloquear);
        VoltarCommand = ReactiveCommand.Create(() => { });

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

    // Prefixo do numero_documento (ver NumeroDocumento): cada PDV da mesma empresa precisa de um código diferente.
    public string CodigoPdv
    {
        get => codigoPdv;
        set => this.RaiseAndSetIfChanged(ref codigoPdv, value);
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

    // Formulário escondido até um supervisor digitar a chave (só quando aberta pelo botão da barra).
    public bool Bloqueada
    {
        get => bloqueada;
        private set
        {
            this.RaiseAndSetIfChanged(ref bloqueada, value);
            this.RaisePropertyChanged(nameof(Liberada));
        }
    }

    public bool Liberada => !Bloqueada;

    // "Voltar" só existe quando aberta pelo botão (na 1ª vinculação não há para onde voltar).
    public bool PodeVoltar { get; }

    // Campo de senha, LIMPO depois de cada tentativa (certa ou errada) — a chave nunca fica no formulário.
    public string ChaveSupervisor
    {
        get => chaveSupervisor;
        set => this.RaiseAndSetIfChanged(ref chaveSupervisor, value);
    }

    public string? MensagemDesbloqueio
    {
        get => mensagemDesbloqueio;
        private set => this.RaiseAndSetIfChanged(ref mensagemDesbloqueio, value);
    }

    public ReactiveCommand<Unit, Unit> DesbloquearCommand { get; }

    // Só emite: quem sabe para onde voltar é o Shell (Dashboard ou Abrir caixa).
    public ReactiveCommand<Unit, Unit> VoltarCommand { get; }

    private async Task DesbloquearAsync()
    {
        var chave = ChaveSupervisor;
        ChaveSupervisor = string.Empty;

        if (await configuracaoService.AutenticarSupervisorAsync(chave))
        {
            MensagemDesbloqueio = null;
            Bloqueada = false;
            return;
        }

        // Uma mensagem só (não revela se a chave existe nem se é de um operador comum).
        MensagemDesbloqueio = "Chave de supervisor inválida — as Configurações só abrem com a chave de um supervisor.";
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
        CodigoPdv = configuracao.CodigoPdv ?? string.Empty;
        ExigirAberturaCaixa = configuracao.ExigirAberturaCaixa;
    }

    // Único método que chama VincularDispositivoAsync (DPAPI, Windows-only) —
    // mesma granularidade de todo o resto do projeto (ver AppServices).
    [SupportedOSPlatform("windows")]
    private async Task<bool> VincularAsync()
    {
        if (Bloqueada)
        {
            Mensagem = MensagemBloqueada;   // Execute() ignora o CanExecute: a regra não pode depender do botão escondido
            return false;
        }

        var (sucesso, mensagemResultado) = await configuracaoService.VincularDispositivoAsync(LinkCadastro, NomeDispositivo);
        Mensagem = mensagemResultado;
        return sucesso;
    }

    private const string MensagemBloqueada = "Digite a chave de um supervisor para alterar as configurações.";

    private async Task SalvarAsync()
    {
        if (Bloqueada)
        {
            Mensagem = MensagemBloqueada;
            return;
        }

        // Código inválido não salva nada: melhor avisar do que gravar meio salvo e o operador achar que valeu.
        if (!NumeroDocumento.TentarNormalizarCodigo(CodigoPdv, out var codigo))
        {
            Mensagem = $"O código do PDV aceita até {NumeroDocumento.TamanhoMaximoCodigo} letras ou números, sem espaço nem hífen.";
            return;
        }

        var clienteConsumidorFinalId = int.TryParse(ClienteConsumidorFinalIdExterno, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : (int?)null;

        await configuracaoService.AtualizarAsync(c =>
        {
            c.ExigirAberturaCaixa = ExigirAberturaCaixa;
            c.ClienteConsumidorFinalIdExterno = clienteConsumidorFinalId;
            c.CodigoPdv = codigo;
        });
        CodigoPdv = codigo ?? string.Empty;   // mostra como ficou (maiúsculo, sem espaços)

        Mensagem = "Configurações salvas.";
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.ViewModels;

// A tela "Caixas no SoftcomShop" (aberta por um botão em Configurações): lista, SÓ PARA LEITURA, os caixas que a API tem —
// abertos e fechados por qualquer PDV da empresa. Sem filtro, a API devolve os últimos 7 dias; dá para informar um intervalo
// (dd/mm/aaaa) e ver só os fechados. Filho do ConfiguracoesViewModel. Precisa de rede: nada é gravado localmente.
[SupportedOSPlatform("windows")]
public class CaixasApiViewModel : ViewModelBase
{
    private static readonly string[] FormatosDeData = { "dd/MM/yyyy", "d/M/yyyy" };

    private readonly CaixasApiService? servico;

    private bool aberto;
    private bool carregando;
    private string dataInicial = string.Empty;
    private string dataFinal = string.Empty;
    private bool somenteFechados;
    private IReadOnlyList<CaixaApiLinha> linhas = Array.Empty<CaixaApiLinha>();
    private int paginaAtual = 1;
    private int totalPaginas = 1;
    private int total;
    private string? mensagem;
    private bool consultou;

    // O que a última consulta USOU: "Próxima/Anterior" repetem esse filtro, não o que já foi digitado depois.
    private (DateOnly? Inicial, DateOnly? Final, bool Fechados) filtroAplicado;

    public CaixasApiViewModel(CaixasApiService? servico)
    {
        this.servico = servico;

        var podeConsultar = this.WhenAnyValue(vm => vm.Carregando, carregando => !carregando);
        ConsultarCommand = ReactiveCommand.CreateFromTask(ConsultarNovaBuscaAsync, podeConsultar);

        var temAnterior = this.WhenAnyValue(vm => vm.PaginaAtual, vm => vm.Carregando, (pagina, ocupado) => pagina > 1 && !ocupado);
        AnteriorCommand = ReactiveCommand.CreateFromTask(() => IrParaPaginaAsync(PaginaAtual - 1), temAnterior);

        var temProxima = this.WhenAnyValue(vm => vm.PaginaAtual, vm => vm.TotalPaginas, vm => vm.Carregando,
            (pagina, ultima, ocupado) => pagina < ultima && !ocupado);
        ProximaCommand = ReactiveCommand.CreateFromTask(() => IrParaPaginaAsync(PaginaAtual + 1), temProxima);

        FecharCommand = ReactiveCommand.Create(() => { Aberto = false; });
    }

    // Sem o serviço (só nos testes de outras telas) o botão que abre esta tela fica desabilitado.
    public bool Disponivel => servico is not null;

    public bool Aberto
    {
        get => aberto;
        private set => this.RaiseAndSetIfChanged(ref aberto, value);
    }

    public bool Carregando
    {
        get => carregando;
        private set => this.RaiseAndSetIfChanged(ref carregando, value);
    }

    // "dd/mm/aaaa"; vazio = sem limite (a API usa os últimos 7 dias quando as duas estão vazias).
    public string DataInicial
    {
        get => dataInicial;
        set => this.RaiseAndSetIfChanged(ref dataInicial, value);
    }

    public string DataFinal
    {
        get => dataFinal;
        set => this.RaiseAndSetIfChanged(ref dataFinal, value);
    }

    public bool SomenteFechados
    {
        get => somenteFechados;
        set => this.RaiseAndSetIfChanged(ref somenteFechados, value);
    }

    public IReadOnlyList<CaixaApiLinha> Linhas
    {
        get => linhas;
        private set
        {
            this.RaiseAndSetIfChanged(ref linhas, value);
            this.RaisePropertyChanged(nameof(SemResultados));
        }
    }

    public int PaginaAtual
    {
        get => paginaAtual;
        private set
        {
            this.RaiseAndSetIfChanged(ref paginaAtual, value);
            this.RaisePropertyChanged(nameof(ResumoPagina));
        }
    }

    public int TotalPaginas
    {
        get => totalPaginas;
        private set
        {
            this.RaiseAndSetIfChanged(ref totalPaginas, value);
            this.RaisePropertyChanged(nameof(ResumoPagina));
        }
    }

    public int Total
    {
        get => total;
        private set
        {
            this.RaiseAndSetIfChanged(ref total, value);
            this.RaisePropertyChanged(nameof(ResumoPagina));
        }
    }

    // Erro da consulta (data inválida, sem conexão, API recusou): vermelho, dentro da tela.
    public string? Mensagem
    {
        get => mensagem;
        private set
        {
            this.RaiseAndSetIfChanged(ref mensagem, value);
            this.RaisePropertyChanged(nameof(SemResultados));
        }
    }

    // Consulta que deu certo mas não trouxe nenhum caixa: nunca uma tabela em branco sem explicação.
    public bool SemResultados => consultou && Mensagem is null && Linhas.Count == 0;

    public string ResumoPagina => $"Página {PaginaAtual} de {TotalPaginas} • {Total} caixa(s)";

    public ReactiveCommand<Unit, Unit> ConsultarCommand { get; }
    public ReactiveCommand<Unit, Unit> AnteriorCommand { get; }
    public ReactiveCommand<Unit, Unit> ProximaCommand { get; }
    public ReactiveCommand<Unit, Unit> FecharCommand { get; }

    // Abre já consultando (sem filtro = os últimos 7 dias): quem abre quer ver os caixas, não um formulário vazio.
    public async Task AbrirAsync()
    {
        DataInicial = string.Empty;
        DataFinal = string.Empty;
        SomenteFechados = false;
        Linhas = Array.Empty<CaixaApiLinha>();
        consultou = false;
        Mensagem = null;
        Aberto = true;
        await ConsultarNovaBuscaAsync();
    }

    private async Task ConsultarNovaBuscaAsync()
    {
        if (!TentarLerData(DataInicial, out var inicial))
        {
            Mensagem = "Data inicial inválida — use dd/mm/aaaa (ex: 21/09/2026) ou deixe em branco.";
            return;
        }

        if (!TentarLerData(DataFinal, out var final))
        {
            Mensagem = "Data final inválida — use dd/mm/aaaa (ex: 21/09/2026) ou deixe em branco.";
            return;
        }

        filtroAplicado = (inicial, final, SomenteFechados);
        await ConsultarPaginaAsync(1);
    }

    private Task IrParaPaginaAsync(int pagina) => ConsultarPaginaAsync(Math.Clamp(pagina, 1, TotalPaginas));

    private async Task ConsultarPaginaAsync(int pagina)
    {
        if (servico is null)
        {
            Mensagem = "A consulta de caixas não está disponível.";
            return;
        }

        Carregando = true;
        Mensagem = null;
        try
        {
            var resultado = await servico.ConsultarAsync(filtroAplicado.Inicial, filtroAplicado.Final, filtroAplicado.Fechados, pagina);
            consultou = true;

            if (!resultado.Sucesso)
            {
                // Mantém a tabela anterior à vista: um erro de rede não apaga o que já estava na tela.
                Mensagem = resultado.Mensagem;
                this.RaisePropertyChanged(nameof(SemResultados));
                return;
            }

            var dados = resultado.Pagina!;
            Linhas = dados.Linhas;
            PaginaAtual = dados.PaginaAtual;
            TotalPaginas = dados.TotalPaginas;
            Total = dados.Total;
        }
        finally
        {
            Carregando = false;
        }
    }

    private static bool TentarLerData(string? texto, out DateOnly? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(texto))
            return true;

        if (!DateOnly.TryParseExact(texto.Trim(), FormatosDeData, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var lida))
            return false;

        data = lida;
        return true;
    }
}

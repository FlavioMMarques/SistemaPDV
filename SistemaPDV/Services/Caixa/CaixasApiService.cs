using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Services.Caixa.Dtos;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Services.Caixa;

// Uma linha da tela "Caixas no SoftcomShop": só o que a tabela mostra. O nome do operador já vem resolvido (ver ConsultarAsync).
public record CaixaApiLinha(
    int Id, DateOnly? DataCaixa, int? Turno, string Operador, DateTime? Abertura, DateTime? Fechamento, string? Dispositivo)
{
    public bool Fechado => Fechamento is not null;

    // Texto da situação (a tela mostra sempre texto + cor, nunca só cor).
    public string Situacao => Fechado ? "Fechado" : "Aberto";
}

public record PaginaCaixasApi(IReadOnlyList<CaixaApiLinha> Linhas, int PaginaAtual, int TotalPaginas, int Total);

public class ResultadoConsultaCaixas
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public PaginaCaixasApi? Pagina { get; private init; }

    public static ResultadoConsultaCaixas ComSucesso(PaginaCaixasApi pagina) => new() { Sucesso = true, Pagina = pagina };
    public static ResultadoConsultaCaixas ComFalha(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

// Consulta (SÓ LEITURA) os caixas que a API tem — os abertos e fechados por qualquer PDV da empresa, não só os deste aparelho —
// para a tela em Configurações. Nunca grava nada no banco local: o caixa que este PDV opera continua sendo o local
// (CaixaService); aqui é a visão da nuvem, útil para conferir o que subiu e o que ficou aberto.
//
// Uma página por chamada (a API pagina, e a tela pede a próxima): não baixa "tudo" como a sincronização do catálogo.
public class CaixasApiService
{
    public const int PorPagina = 15;

    private readonly Func<AppDbContext> contextFactory;
    private readonly SoftcomApiClient apiClient;
    private readonly SoftcomAuthService authService;

    public CaixasApiService(Func<AppDbContext> contextFactory, SoftcomApiClient apiClient, SoftcomAuthService authService)
    {
        this.contextFactory = contextFactory;
        this.apiClient = apiClient;
        this.authService = authService;
    }

    // dataInicial/dataFinal: opcionais — sem nenhuma das duas a API devolve os últimos 7 dias. somenteFechados: só os que
    // já têm data de fechamento. pagina começa em 1.
    [SupportedOSPlatform("windows")]
    public async Task<ResultadoConsultaCaixas> ConsultarAsync(
        DateOnly? dataInicial, DateOnly? dataFinal, bool somenteFechados, int pagina, CancellationToken ct = default)
    {
        if (dataInicial is { } inicio && dataFinal is { } fim && inicio > fim)
            return ResultadoConsultaCaixas.ComFalha("A data inicial não pode ser depois da data final.");

        await using var context = contextFactory();
        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct);
        if (configuracao is null || string.IsNullOrWhiteSpace(configuracao.UrlApi))
            return ResultadoConsultaCaixas.ComFalha("Este dispositivo ainda não está vinculado ao SoftcomShop — vincule-o em Configurações antes de consultar.");

        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        if (!ConexaoSegura.Permitida(dominio))
            return ResultadoConsultaCaixas.ComFalha(ConexaoSegura.MensagemRecusa);

        var (autenticado, mensagemAutenticacao, accessToken) = await authService.ObterTokenAsync(configuracao, ct);
        if (!autenticado || accessToken is null)
            return ResultadoConsultaCaixas.ComFalha(mensagemAutenticacao);

        var resultado = await apiClient.EnviarAsync(
            HttpMethod.Get, MontarUrl(dominio, dataInicial, dataFinal, somenteFechados, Math.Max(1, pagina)), corpo: null, accessToken, ct);

        if (resultado.Tipo != ResultadoEnvioTipo.Sucesso)
        {
            var motivo = resultado.Tipo == ResultadoEnvioTipo.TokenExpirado
                ? "A sessão com o SoftcomShop expirou — tente de novo."
                : ErroApiExtractor.Extrair(resultado.Conteudo);
            return ResultadoConsultaCaixas.ComFalha($"Não foi possível consultar os caixas: {motivo}");
        }

        var lista = LerLista(resultado.Conteudo);
        if (lista is null)
            return ResultadoConsultaCaixas.ComFalha($"Resposta inesperada da API: {ErroApiExtractor.Extrair(resultado.Conteudo)}");

        var nomes = await NomesDosOperadoresAsync(context, lista.Data, ct);
        var linhas = lista.Data
            .OrderByDescending(c => c.Id)   // o mais recente primeiro
            .Select(c => new CaixaApiLinha(
                c.Id, LerData(c.DataCaixa), c.Turno,
                c.OperadorId is { } id ? nomes.GetValueOrDefault(id) ?? $"Operador {id}" : "—",
                LerDataHora(c.DataAbertura), LerDataHora(c.DataFechamento),
                string.IsNullOrWhiteSpace(c.ApiDeviceId) ? null : c.ApiDeviceId.Trim()))
            .ToList();

        var atual = lista.CurrentPage ?? pagina;
        var total = lista.Total ?? linhas.Count;
        return ResultadoConsultaCaixas.ComSucesso(new PaginaCaixasApi(linhas, atual, Math.Max(1, lista.LastPage ?? atual), total));
    }

    private static string MontarUrl(string dominio, DateOnly? dataInicial, DateOnly? dataFinal, bool somenteFechados, int pagina)
    {
        var consulta = new List<string> { $"per_page={PorPagina}", $"page={pagina}" };
        if (dataInicial is { } inicio)
            consulta.Add($"data_inicial={inicio:yyyy-MM-dd}");
        if (dataFinal is { } fim)
            consulta.Add($"data_final={fim:yyyy-MM-dd}");
        if (somenteFechados)
            consulta.Add("fechado=1");

        return $"{SoftcomRotas.CaixasListar(dominio)}?{string.Join("&", consulta)}";
    }

    // A API devolve a página como objeto na maioria das rotas, mas há rotas que a embrulham num array — aceita as duas formas.
    // A resposta é dado não confiável: nunca lança.
    private static CaixaListaApiDto? LerLista(string conteudo)
    {
        try
        {
            using var documento = JsonDocument.Parse(conteudo);
            var raiz = documento.RootElement;
            if (raiz.ValueKind == JsonValueKind.Array)
            {
                if (raiz.GetArrayLength() == 0)
                    return new CaixaListaApiDto();
                raiz = raiz[0];
            }

            return raiz.ValueKind == JsonValueKind.Object ? raiz.Deserialize<CaixaListaApiDto>(SoftcomJson.Opcoes) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // operador_id é o id do funcionário NA API: casa com Funcionario.IdExterno. Quem não está no cadastro local (funcionário de
    // outro PDV que ainda não sincronizou, ou desativado e removido) aparece como "Operador N".
    private static async Task<Dictionary<int, string>> NomesDosOperadoresAsync(
        AppDbContext context, List<CaixaListaItemApiDto> itens, CancellationToken ct)
    {
        var ids = itens.Where(i => i.OperadorId is not null).Select(i => (int?)i.OperadorId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, string>();

        var funcionarios = await context.Funcionarios
            .Where(f => f.IdExterno != null && ids.Contains(f.IdExterno))
            .Select(f => new { f.IdExterno, f.Nome })
            .ToListAsync(ct);
        return funcionarios.GroupBy(f => f.IdExterno!.Value).ToDictionary(g => g.Key, g => g.First().Nome);
    }

    private static DateOnly? LerData(string? texto) =>
        DateOnly.TryParse(texto?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) ? data : null;

    private static DateTime? LerDataHora(string? texto) =>
        DateTime.TryParse(texto?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) ? data : null;
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SistemaPDV.Services.Sync.Dtos;

namespace SistemaPDV.Services.Sync;

// Único ponto que fala HTTP com a API SoftcomShop — tanto os endpoints paginados
// (BuscarTudoAsync, usados por catalog-sync) quanto os de escrita (EnviarAsync,
// usados por caixa e sales). Antes da limpeza, caixa e sales remontavam a mesma
// sequência de headers/envio/classificação de resposta cada um por conta própria.
public class SoftcomApiClient
{
    private readonly HttpClient httpClient;

    public SoftcomApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    // dominio: a parte {scheme}://{host}[:porta] da UrlApi configurada — não é fixo,
    // muda por dispositivo/cliente (ver ExtrairDominio no projeto de referência).
    public async Task<ResultadoBusca<T>> BuscarTudoAsync<T>(
        string dominio, string caminho, long? ultimaSincronizacao, string accessToken, CancellationToken ct = default)
    {
        // Mesma guarda de EnviarAsync: o access token vai em todo GET paginado.
        if (!ConexaoSegura.Permitida(dominio))
            return ResultadoBusca<T>.ComFalha(ConexaoSegura.MensagemRecusa);

        var itens = new List<T>();
        long? dateSync = null;
        string? url = MontarUrlInicial(dominio, caminho, ultimaSincronizacao);

        while (url is not null)
        {
            // A API pode devolver next_page_url absoluto; nunca segue pra um domínio
            // diferente do configurado (evita vazar o access token se a API um dia
            // devolver isso errado).
            if (!UrlPertenceAoDominio(url, dominio))
                return ResultadoBusca<T>.ComFalha($"A API retornou uma página fora do domínio esperado: {url}");

            using var requisicao = new HttpRequestMessage(HttpMethod.Get, url);
            requisicao.Headers.Add("Api-Version", "v2");
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resposta = await httpClient.SendAsync(requisicao, ct);
            var conteudo = await resposta.Content.ReadAsStringAsync(ct);

            if (!resposta.IsSuccessStatusCode)
                return ResultadoBusca<T>.ComFalha($"Falha ao consultar {caminho}: {(int)resposta.StatusCode} {resposta.ReasonPhrase}. {conteudo}");

            PaginaApiDto<T>? pagina;
            try
            {
                pagina = LerPagina<T>(conteudo);
            }
            catch (JsonException ex)
            {
                return ResultadoBusca<T>.ComFalha($"Resposta inesperada da API: {ex.Message}");
            }

            if (pagina is null)
                return ResultadoBusca<T>.ComFalha($"Resposta inesperada da API: {conteudo}");

            itens.AddRange(pagina.Data);
            dateSync = pagina.DateSync ?? dateSync;
            url = pagina.NextPageUrl;
        }

        return ResultadoBusca<T>.ComSucesso(itens, dateSync);
    }

    // Usado pelos endpoints de escrita (caixa-funcoes/abrir, /fechar, vendas) — monta
    // a requisição (headers padrão), envia, e classifica a resposta num dos quatro
    // tipos. Cada chamador só precisa saber montar o corpo e reagir ao Tipo.
    public async Task<ResultadoEnvio> EnviarAsync(
        HttpMethod metodo, string url, object? corpo, string accessToken, CancellationToken ct = default)
    {
        // Antes de qualquer coisa (inclusive de montar o header com o token): em
        // http:// o Bearer e o corpo (que pode ter CPF/CNPJ) trafegariam em texto puro.
        if (!ConexaoSegura.Permitida(url))
            return ResultadoEnvio.Criar(ResultadoEnvioTipo.ConexaoInsegura, ConexaoSegura.MensagemRecusa);

        try
        {
            using var requisicao = new HttpRequestMessage(metodo, url);
            requisicao.Headers.Add("Api-Version", "v2");
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (corpo is not null)
                requisicao.Content = JsonContent.Create(corpo);

            using var resposta = await httpClient.SendAsync(requisicao, ct);
            var conteudo = await resposta.Content.ReadAsStringAsync(ct);

            var tipo = resposta.StatusCode switch
            {
                HttpStatusCode.Conflict => ResultadoEnvioTipo.Conflito,
                HttpStatusCode.Unauthorized => ResultadoEnvioTipo.TokenExpirado,
                _ when resposta.IsSuccessStatusCode => ResultadoEnvioTipo.Sucesso,
                _ => ResultadoEnvioTipo.Falha,
            };

            return ResultadoEnvio.Criar(tipo, conteudo);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ResultadoEnvio.Criar(ResultadoEnvioTipo.Falha, $"Falha de conexão: {ex.Message}");
        }
    }

    // A API real devolve a página como objeto na maioria dos endpoints, mas o de formas de
    // pagamento (".../forma-pagamento/page/1") a embrulha num array: [ { "data": [...] } ].
    // Aceita as duas formas; array vazio = página sem itens.
    private static PaginaApiDto<T>? LerPagina<T>(string conteudo)
    {
        using var documento = JsonDocument.Parse(conteudo);
        var raiz = documento.RootElement;

        if (raiz.ValueKind == JsonValueKind.Array)
        {
            if (raiz.GetArrayLength() == 0)
                return new PaginaApiDto<T>();

            raiz = raiz[0];
        }

        return raiz.Deserialize<PaginaApiDto<T>>(SoftcomJson.Opcoes);
    }

    private static string MontarUrlInicial(string dominio, string caminho, long? ultimaSincronizacao)
    {
        var url = $"{dominio}/{caminho}?per_page=200";

        if (ultimaSincronizacao is { } valor)
            url += $"&ultima_sincronizacao={valor}";

        return url;
    }

    private static bool UrlPertenceAoDominio(string url, string dominio) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        Uri.TryCreate(dominio, UriKind.Absolute, out var uriDominio) &&
        uri.Scheme == uriDominio.Scheme &&
        string.Equals(uri.Host, uriDominio.Host, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == uriDominio.Port;
}

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

    // Monta uma requisição NOVA a cada tentativa (um HttpRequestMessage já enviado não
    // pode ser reenviado) e a envia sob o RetryHttpTransitorio.
    private Task<HttpResponseMessage> EnviarComRetryAsync(Func<HttpRequestMessage> montarRequisicao, CancellationToken ct) =>
        RetryHttpTransitorio.Politica.ExecuteAsync(cancelToken =>
        {
            using var requisicao = montarRequisicao();
            return httpClient.SendAsync(requisicao, cancelToken);
        }, ct);

    // Uma linha por requisição de verdade enviada (cada tentativa do retry conta a sua). O Registro mascara token,
    // client_secret e CPF/CNPJ sozinho (ver LogArquivo.Mascarar) — seguro logar o corpo cru aqui.
    private static void RegistrarRequisicao(HttpMethod metodo, string url, object? corpo = null) =>
        Registro.Info("SoftcomApiClient", corpo is null ? $"{metodo} {url}" : $"{metodo} {url} | corpo: {JsonSerializer.Serialize(corpo)}");

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
        var ultimoTotal = 0;
        string? url = MontarUrlInicial(dominio, caminho, ultimaSincronizacao);
        var urlsVisitadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (url is not null)
        {
            // Uma API que devolve a mesma next_page_url (ou volta a uma página já lida) prenderia a sincronização
            // num laço infinito, baixando a mesma página pra sempre.
            if (!urlsVisitadas.Add(url))
                return ResultadoBusca<T>.ComFalha($"A API repetiu a página {url} — paginação interrompida para não entrar em laço.");

            // A API pode devolver next_page_url absoluto; nunca segue pra um domínio
            // diferente do configurado (evita vazar o access token se a API um dia
            // devolver isso errado).
            if (!UrlPertenceAoDominio(url, dominio))
                return ResultadoBusca<T>.ComFalha($"A API retornou uma página fora do domínio esperado: {url}");

            using var resposta = await EnviarComRetryAsync(() =>
            {
                RegistrarRequisicao(HttpMethod.Get, url);
                var requisicao = new HttpRequestMessage(HttpMethod.Get, url);
                requisicao.Headers.Add("Api-Version", "v2");
                requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return requisicao;
            }, ct);
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
            ultimoTotal = pagina.Total;
            // O next_page_url que a API devolve não carrega o per_page da chamada original —
            // sem isso, a próxima página usa o per_page PADRÃO do servidor (visto: 500), que
            // pode fazer o total inteiro caber numa página só e a página seguinte (que a gente
            // pediu) voltar vazia como se a paginação tivesse acabado. #produtos-422.
            url = pagina.NextPageUrl is { } proximaUrl ? GarantirPerPage(proximaUrl) : null;
        }

        // A API devolveu next_page_url nulo (parou de paginar) mas o total declarado na
        // última página diz que deveria ter vindo mais — sem essa checagem, um catálogo
        // incompleto era aceito em silêncio (só descobrível contando registro por registro
        // no banco depois, como em #65). Duplicata entre páginas (documentada em
        // api-real.md, tratada via Distinct no upsert) só pode fazer itens.Count subir, nunca
        // descer — então "menor que o total" nunca dá falso positivo por causa de duplicata.
        if (itens.Count < ultimoTotal)
        {
            return ResultadoBusca<T>.ComFalha(
                $"A API parou de paginar {caminho} antes do fim: vieram {itens.Count} de {ultimoTotal} itens declarados (next_page_url ficou nulo cedo demais).");
        }

        return ResultadoBusca<T>.ComSucesso(itens, dateSync);
    }

    // Rotas de paginação "por número" (cartões): {dominio}/{caminhoBase}/{n}, com o envelope PaginaMetaApiDto — a
    // próxima página vem em meta.page.next (número; null na última). Diferente de BuscarTudoAsync, NUNCA segue uma URL
    // devolvida pelo servidor: monta cada URL sozinho, então não há como ser levado a outro domínio com o token.
    public async Task<ResultadoBusca<T>> BuscarPaginasAsync<T>(
        string dominio, string caminhoBase, string accessToken, CancellationToken ct = default)
    {
        if (!ConexaoSegura.Permitida(dominio))
            return ResultadoBusca<T>.ComFalha(ConexaoSegura.MensagemRecusa);

        var itens = new List<T>();
        long? dateSync = null;
        var pagina = 1;

        for (var lidas = 0; lidas < MaximoPaginasPorBusca; lidas++)
        {
            using var resposta = await EnviarComRetryAsync(() =>
            {
                var url = $"{dominio}/{caminhoBase}/{pagina}";
                RegistrarRequisicao(HttpMethod.Get, url);
                var requisicao = new HttpRequestMessage(HttpMethod.Get, url);
                requisicao.Headers.Add("Api-Version", "v2");
                requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return requisicao;
            }, ct);
            var conteudo = await resposta.Content.ReadAsStringAsync(ct);

            if (!resposta.IsSuccessStatusCode)
            {
                // A API responde 500 "Invalid pagination interval." a uma página fora do intervalo — na 1ª página isso é
                // "não há nenhum registro", não uma falha.
                if (pagina == 1 && conteudo.Contains("Invalid pagination interval", StringComparison.OrdinalIgnoreCase))
                    return ResultadoBusca<T>.ComSucesso(itens, dateSync);

                return ResultadoBusca<T>.ComFalha($"Falha ao consultar {caminhoBase}/{pagina}: {(int)resposta.StatusCode} {resposta.ReasonPhrase}. {conteudo}");
            }

            var dto = SoftcomJson.TentarDesserializar<PaginaMetaApiDto<T>>(conteudo);
            if (dto is null)
                return ResultadoBusca<T>.ComFalha($"Resposta inesperada da API: {conteudo}");

            itens.AddRange(dto.Data);
            dateSync = TentarLerDateSync(conteudo) ?? dateSync;

            var proxima = dto.Meta?.Page?.Next;
            if (proxima is not { } proximaPagina)
                return ResultadoBusca<T>.ComSucesso(itens, dateSync);

            // Uma "próxima" que não avança prenderia o laço baixando a mesma página (mesma proteção de BuscarTudoAsync).
            if (proximaPagina <= pagina)
                return ResultadoBusca<T>.ComFalha($"A API repetiu a página {proximaPagina} — paginação interrompida para não entrar em laço.");

            pagina = proximaPagina;
        }

        return ResultadoBusca<T>.ComFalha($"Mais de {MaximoPaginasPorBusca} páginas em {caminhoBase} — paginação interrompida.");
    }

    private const int MaximoPaginasPorBusca = 200;

    private static long? TentarLerDateSync(string conteudo)
    {
        try
        {
            using var documento = JsonDocument.Parse(conteudo);
            return documento.RootElement.TryGetProperty("date_sync", out var valor) && valor.TryGetInt64(out var segundos) ? segundos : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
            using var resposta = await EnviarComRetryAsync(() =>
            {
                RegistrarRequisicao(metodo, url, corpo);
                var requisicao = new HttpRequestMessage(metodo, url);
                requisicao.Headers.Add("Api-Version", "v2");
                requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                if (corpo is not null)
                    requisicao.Content = JsonContent.Create(corpo);
                return requisicao;
            }, ct);
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

    // O next_page_url da API não inclui per_page (ver #produtos-422) — reforça aqui pra
    // não depender do padrão do servidor. Se por algum motivo já vier com per_page (ex:
    // um dia a API for corrigida), não mexe.
    private static string GarantirPerPage(string url) =>
        url.Contains("per_page=", StringComparison.OrdinalIgnoreCase)
            ? url
            : url + (url.Contains('?') ? '&' : '?') + "per_page=200";

    private static bool UrlPertenceAoDominio(string url, string dominio) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        Uri.TryCreate(dominio, UriKind.Absolute, out var uriDominio) &&
        uri.Scheme == uriDominio.Scheme &&
        string.Equals(uri.Host, uriDominio.Host, StringComparison.OrdinalIgnoreCase) &&
        uri.Port == uriDominio.Port;
}

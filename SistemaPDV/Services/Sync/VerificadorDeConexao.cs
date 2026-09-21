using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaPDV.Services.Sync;

// Responde só "o servidor da API está alcançável agora?", com uma requisição levíssima (HEAD na raiz do servidor) e prazo
// curto. Existe porque os ciclos de sincronização só falam com a rede quando têm o que enviar (a cada 30 s) ou o catálogo
// (a cada 5 min): sem isto, uma queda de internet com a fila vazia levava até 5 minutos para aparecer.
public class VerificadorDeConexao
{
    public static readonly TimeSpan PrazoPadrao = TimeSpan.FromSeconds(5);

    private readonly HttpClient httpClient;
    private readonly TimeSpan prazo;

    public VerificadorDeConexao(HttpClient httpClient, TimeSpan? prazo = null)
    {
        this.httpClient = httpClient;
        this.prazo = prazo ?? PrazoPadrao;
    }

    // Qualquer resposta HTTP (até 401/404/500) prova que o servidor está lá; só falta de resposta (DNS, recusa, tempo
    // esgotado) conta como inalcançável. URL inválida ou sem HTTPS devolve true: não há o que verificar, e esse problema de
    // configuração é dito pelos ciclos de sincronização — a verificação não pode mascará-lo como "sem internet".
    public async Task<bool> AlcancavelAsync(string urlApi, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(urlApi, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return true;

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(prazo);

        try
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Head, uri.GetLeftPart(UriPartial.Authority) + "/");
            using var resposta = await httpClient.SendAsync(requisicao, HttpCompletionOption.ResponseHeadersRead, limite.Token);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;   // estourou o prazo (rede pendurada), não foi o app fechando
        }
    }
}

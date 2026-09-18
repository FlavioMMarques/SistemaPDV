using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaPDV.Tests;

// Handler fake reutilizável por qualquer teste que precise de um HttpClient sem rede
// real — devolve o que a função `responder` disser pra cada requisição recebida.
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly System.Func<HttpRequestMessage, HttpResponseMessage> responder;

    public FakeHttpMessageHandler(System.Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        this.responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(responder(request));

    public static HttpClient CriarHttpClient(System.Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new FakeHttpMessageHandler(responder));
}

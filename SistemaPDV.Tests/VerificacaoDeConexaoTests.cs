using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Detecção de queda/volta da internet sem esperar os ciclos de 30 s / 5 min (que só falam com a rede quando têm o que enviar).
[SupportedOSPlatform("windows")]
public class VerificacaoDeConexaoTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";
    private const string PaginaVazia = """{ "current_page": 1, "data": [], "next_page_url": null, "total": 0, "date_sync": 1758000000 }""";

    // Um "servidor" que o teste liga e desliga: desligado, a requisição falha como faria sem internet (nenhuma resposta).
    private sealed class RedeFake
    {
        private readonly object trava = new();
        private readonly List<string> requisicoes = new();

        public bool Ligada { get; set; } = true;
        public bool AutenticacaoRecusada { get; set; }
        public IReadOnlyList<string> Requisicoes { get { lock (trava) return requisicoes.ToList(); } }
        public int Contar(string trecho) => Requisicoes.Count(r => r.Contains(trecho));

        public HttpClient CriarHttpClient() => FakeHttpMessageHandler.CriarHttpClient(requisicao =>
        {
            lock (trava) requisicoes.Add($"{requisicao.Method} {requisicao.RequestUri!.PathAndQuery}");
            if (!Ligada)
                throw new HttpRequestException("Nenhuma conexão pôde ser feita");

            var caminho = requisicao.RequestUri!.AbsolutePath;
            if (requisicao.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.NotFound);   // o servidor respondeu: é o que basta
            if (caminho.EndsWith("/authentication/token"))
                return AutenticacaoRecusada ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Json("""{ "data": { "token": "token-fake" } }""");
            return Json(PaginaVazia);
        });

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class PenduradoHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private static async Task<SincronizacaoBackgroundService> CriarServiceAsync(SqliteInMemoryFixture fixture, RedeFake rede, bool comConfiguracao = true)
    {
        if (comConfiguracao)
        {
            await using var context = fixture.CriarContexto();
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = UrlApi,
                ApiClienteId = "1",
                ApiClienteSecretProtegido = new SegredoProtector().Proteger("segredo"),
            });
            await context.SaveChangesAsync();
        }

        var httpClient = rede.CriarHttpClient();
        var apiClient = new SoftcomApiClient(httpClient);
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        return new SincronizacaoBackgroundService(
            fixture.CriarContexto,
            authService,
            new CatalogSyncService(fixture.CriarContexto, apiClient, segredoProtector, authService),
            new CaixaSyncService(fixture.CriarContexto, apiClient),
            new VendaSyncService(fixture.CriarContexto, apiClient),
            verificador: new VerificadorDeConexao(httpClient));
    }

    // ---- o verificador ----

    [Fact]
    public async Task QualquerRespostaDoServidorContaComoAlcancavelEFalhaDeRedeNao()
    {
        var rede = new RedeFake();
        var verificador = new VerificadorDeConexao(rede.CriarHttpClient());

        Assert.True(await verificador.AlcancavelAsync(UrlApi));      // 404 do HEAD: o servidor respondeu
        Assert.Equal("HEAD /", rede.Requisicoes.Single());           // levíssimo: HEAD na raiz, sem token nem dados

        rede.Ligada = false;
        Assert.False(await verificador.AlcancavelAsync(UrlApi));
    }

    [Fact]
    public async Task RedePendurada_EstouraOPrazoEContaComoInalcancavel()
    {
        var verificador = new VerificadorDeConexao(new HttpClient(new PenduradoHandler()), prazo: TimeSpan.FromMilliseconds(100));

        Assert.False(await verificador.AlcancavelAsync(UrlApi));
    }

    [Fact]
    public async Task FecharOAppNaoViraOfflineFalso()
    {
        var verificador = new VerificadorDeConexao(new HttpClient(new PenduradoHandler()));
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verificador.AlcancavelAsync(UrlApi, cancelamento.Token));
    }

    [Fact]
    public async Task UrlSemHttpsNaoEVerificadaEContaComoAlcancavel()
    {
        var rede = new RedeFake { Ligada = false };
        var verificador = new VerificadorDeConexao(rede.CriarHttpClient());

        Assert.True(await verificador.AlcancavelAsync("http://exemplo.com/registrar"));   // esse problema é dito pelos ciclos de sync
        Assert.Empty(rede.Requisicoes);
    }

    // ---- no serviço ----

    [Fact]
    public async Task SemConfiguracaoNaoVerificaNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var rede = new RedeFake { Ligada = false };
        using var service = await CriarServiceAsync(fixture, rede, comConfiguracao: false);

        await service.VerificarConexaoAsync();

        Assert.Empty(rede.Requisicoes);
        Assert.Equal(EstadoConexao.Desconhecida, service.Estado);
    }

    [Fact]
    public async Task QuedaComAFilaVaziaViraOfflineNaHoraSemEsperarOCicloDoCatalogo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var rede = new RedeFake();
        using var service = await CriarServiceAsync(fixture, rede);
        await service.ExecutarCicloCatalogoAsync();
        Assert.Equal(EstadoConexao.Online, service.Estado);          // tudo sincronizado, nada pendente

        rede.Ligada = false;
        await service.VerificarConexaoAsync();                        // a verificação de 15 s

        Assert.Equal(EstadoConexao.Offline, service.Estado);
        Assert.Contains("Sem acesso à API", service.MensagemUltimoCiclo);
    }

    [Fact]
    public async Task VoltaDaInternetRodaOCatalogoEVoltaAOnlineNaHora()
    {
        using var fixture = new SqliteInMemoryFixture();
        var rede = new RedeFake { Ligada = false };
        using var service = await CriarServiceAsync(fixture, rede);
        await service.VerificarConexaoAsync();
        Assert.Equal(EstadoConexao.Offline, service.Estado);

        rede.Ligada = true;
        await service.VerificarConexaoAsync();

        Assert.Equal(EstadoConexao.Online, service.Estado);           // autenticou de novo, sem esperar os 5 min
        Assert.Equal(1, rede.Contar("/authentication/token"));
    }

    [Fact]
    public async Task ServidorAlcancavelRepetidoNaoAutenticaACada15Segundos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var rede = new RedeFake();
        using var service = await CriarServiceAsync(fixture, rede);

        await service.VerificarConexaoAsync();                        // 1ª verificação: alcançável, nada mudou
        await service.VerificarConexaoAsync();
        await service.VerificarConexaoAsync();

        Assert.Equal(0, rede.Contar("/authentication/token"));        // um Offline por credencial errada não vira tentativa a cada 15 s
        Assert.Equal(3, rede.Contar("HEAD"));
    }

    [Fact]
    public async Task VoltouMasAindaOfflineTentaDeNovoSoAlgumasVezes()
    {
        using var fixture = new SqliteInMemoryFixture();
        var rede = new RedeFake { Ligada = false, AutenticacaoRecusada = true };
        using var service = await CriarServiceAsync(fixture, rede);
        await service.VerificarConexaoAsync();                        // caiu
        rede.Ligada = true;                                           // volta, mas a API recusa a credencial

        for (var i = 0; i < 6; i++)
            await service.VerificarConexaoAsync();

        Assert.Equal(EstadoConexao.Offline, service.Estado);
        Assert.Equal(3, rede.Contar("/authentication/token"));        // tenta 3 vezes e para: não vira um login a cada 15 s
    }

    [Fact]
    public async Task QuedaRepetidaNaoRepublicaOMesmoEstado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var rede = new RedeFake { Ligada = false };
        using var service = await CriarServiceAsync(fixture, rede);
        var mudancas = new List<EstadoConexao>();
        using var assinatura = service.EstadoConexaoAlterada.Subscribe(mudancas.Add);

        await service.VerificarConexaoAsync();
        await service.VerificarConexaoAsync();
        await service.VerificarConexaoAsync();

        Assert.Equal(new[] { EstadoConexao.Desconhecida, EstadoConexao.Offline }, mudancas);
    }
}

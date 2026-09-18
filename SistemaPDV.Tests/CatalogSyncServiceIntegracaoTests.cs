using System.Net;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class CatalogSyncServiceIntegracaoTests
{
    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
        {
            UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
            ApiClienteId = "1",
            ApiClienteSecretProtegido = "secret-de-teste",
        });
        await context.SaveChangesAsync();
    }

    private static HttpResponseMessage RespostaJson(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static readonly string PaginaVazia = """{ "current_page": 1, "data": [], "next_page_url": null, "total": 0 }""";

    [Fact]
    public async Task SincronizarTudoAutenticaEChamaOsCincoRecursos()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var caminhosChamados = new List<string>();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            var caminho = req.RequestUri!.AbsolutePath;
            caminhosChamados.Add(caminho);

            if (caminho.EndsWith("/token"))
                return RespostaJson("""{ "data": { "token": "token-de-teste" } }""");

            return RespostaJson(PaginaVazia);
        });

        var apiClient = new SoftcomApiClient(httpClient);
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        var service = new CatalogSyncService(fixture.CriarContexto, apiClient, segredoProtector, authService);

        var resultado = await service.SincronizarTudoAsync();

        Assert.True(resultado.AutenticacaoSucesso);
        Assert.True(resultado.TudoComSucesso);
        Assert.Contains(caminhosChamados, c => c.Contains("/softauth/authentication/token"));
        Assert.Contains(caminhosChamados, c => c.Contains("/clientes/clientes"));
        Assert.Contains(caminhosChamados, c => c.Contains("/produtos/produtos"));
        Assert.Contains(caminhosChamados, c => c.Contains("/forma-pagamento/page/1"));
        Assert.Contains(caminhosChamados, c => c.Contains("/funcionarios"));
        Assert.Contains(caminhosChamados, c => c.Contains("/empresa/empresas/1"));
    }

    [Fact]
    public async Task FalhaNaAutenticacaoAbortaSemTentarNenhumRecurso()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var caminhosChamados = new List<string>();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            caminhosChamados.Add(req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"message":"invalid_client"}""", Encoding.UTF8, "application/json"),
            };
        });

        var apiClient = new SoftcomApiClient(httpClient);
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        var service = new CatalogSyncService(fixture.CriarContexto, apiClient, segredoProtector, authService);

        var resultado = await service.SincronizarTudoAsync();

        Assert.False(resultado.AutenticacaoSucesso);
        Assert.False(resultado.TudoComSucesso);
        Assert.Single(caminhosChamados); // só a tentativa de token, nenhum recurso chamado
        Assert.Null(resultado.Produtos);
    }
}

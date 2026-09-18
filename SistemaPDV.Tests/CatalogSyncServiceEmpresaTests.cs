using System.Net;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class CatalogSyncServiceEmpresaTests
{
    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
        await context.SaveChangesAsync();
    }

    private static HttpResponseMessage RespostaJson(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task SincronizaEmpresaEProtegeOCertificado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """
            { "current_page": 1, "data": [
                {
                  "empresa_id": 1, "empresa_razao_social": "Softcom Tecnologia LTDA", "empresa_fantasia": "Softcom",
                  "empresa_cnpj": "12345678000199", "empresa_cidade": "Fortaleza", "empresa_uf": "CE",
                  "empresa_modulo_fiscal": true, "empresa_nfce_serie": 1,
                  "empresa_certificado": "MIIC2jCCAcKgAwIBAgIBADANBgkqhkiG9w0B==",
                  "empresa_certificado_senha": "senha-super-secreta"
                }
              ], "next_page_url": null, "total": 1, "date_sync": 1758000000 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarEmpresaAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var empresa = leitura.Empresas.Single(e => e.IdExterno == 1);

        Assert.Equal("Softcom Tecnologia LTDA", empresa.RazaoSocial);
        Assert.True(empresa.ModuloFiscal);
        Assert.NotNull(empresa.CertificadoProtegido);
        Assert.DoesNotContain("senha-super-secreta", empresa.CertificadoProtegido);
        Assert.DoesNotContain("MIIC2jCCAcKgAwIBAgIBADANBgkqhkiG9w0B==", empresa.CertificadoProtegido);
    }

    [Fact]
    public async Task EmpresaSemCertificadoFicaComCertificadoProtegidoNulo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """{ "current_page": 1, "data": [{"empresa_id": 1, "empresa_razao_social": "Softcom", "empresa_cnpj": "12345678000199"}], "next_page_url": null, "total": 1 }""";
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarEmpresaAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.Empresas.Single().CertificadoProtegido);
    }
}

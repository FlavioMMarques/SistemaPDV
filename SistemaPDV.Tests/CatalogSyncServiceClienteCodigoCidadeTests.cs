using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// A API manda DOIS códigos de cidade no cliente: "cidade_id" (número interno da API, ex: 2308) e "codigo_cidade" (o do IBGE, ex:
// 3106200 = Belo Horizonte) — este é o que o cadastro pede como "c_cidade". A sincronização guarda os dois, cada um no seu campo.
[SupportedOSPlatform("windows")]
public class CatalogSyncServiceClienteCodigoCidadeTests
{
    [Theory]
    [InlineData("\"3106200\"")]      // como texto
    [InlineData("3106200")]          // como número
    public async Task GuardaOCodigoIbgeDaCidadeSeparadoDoIdInternoDaApi(string codigoNoJson)
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
            await context.SaveChangesAsync();
        }
        var json = $$"""
            { "current_page": 1, "data": [
                { "id": 7, "nome": "CLIENTE BH", "pessoa": "FISICA", "cidade": "BELO HORIZONTE", "uf": "MG",
                  "codigo_cidade": {{codigoNoJson}}, "cidade_id": "2308" } ],
              "next_page_url": null, "total": 1 }
            """;
        var http = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(http), new SegredoProtector(), new SoftcomAuthService(http, new SegredoProtector()));

        await service.SincronizarClientesAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal("3106200", cliente.CodigoCidade);
        Assert.Equal("2308", cliente.CidadeId);
    }
}

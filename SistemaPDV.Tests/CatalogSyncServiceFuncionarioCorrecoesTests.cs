using System.Net;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Comprova a correção da revisão de código de 2026-09-18: uma resincronização de
// funcionário sem "usuario"/"pdv_key" no payload não pode apagar um login que já
// funcionava.
[SupportedOSPlatform("windows")]
public class CatalogSyncServiceFuncionarioCorrecoesTests
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
    public async Task ResyncSemUsuarioNaRespostaPreservaOPdvKeyHashJaGravado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var jsonComPdvKey = """{ "current_page": 1, "data": [{ "id": 2, "nome": "Carlos Silva", "usuario": { "pdv_key": "1234" } }], "next_page_url": null, "total": 1 }""";
        var jsonSemUsuario = """{ "current_page": 1, "data": [{ "id": 2, "nome": "Carlos Silva" }], "next_page_url": null, "total": 1 }""";

        var primeiraChamada = true;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            var resposta = RespostaJson(primeiraChamada ? jsonComPdvKey : jsonSemUsuario);
            primeiraChamada = false;
            return resposta;
        });
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarFuncionariosAsync("token-fake");
        string? hashAposPrimeiraSync;
        using (var leitura = fixture.CriarContexto())
            hashAposPrimeiraSync = leitura.Funcionarios.Single().PdvKeyHash;

        Assert.NotNull(hashAposPrimeiraSync);

        // Segunda sincronização: a API "esquece" de mandar o usuario dessa vez.
        await service.SincronizarFuncionariosAsync("token-fake");

        using var leituraFinal = fixture.CriarContexto();
        var funcionario = leituraFinal.Funcionarios.Single();
        Assert.Equal(hashAposPrimeiraSync, funcionario.PdvKeyHash); // preservado, não apagado
    }
}

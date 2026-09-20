using System.Net;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Services;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

[SupportedOSPlatform("windows")]
public class CatalogSyncServiceFuncionarioTests
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
    public async Task SincronizaFuncionarioGuardandoOHashBcryptDaApiComoVemEOLoginConfere()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var hashDaApi = PdvKeyTeste.Hash("1234");
        var json = $$"""
            { "current_page": 1, "data": [
                { "id": 2, "nome": "Carlos Silva", "cpf": "11122233344", "supervisor": false, "desativado": false,
                  "usuario": { "id": 2, "name": "Carlos Silva", "pdv_key": "{{hashDaApi}}" } }
              ], "next_page_url": null, "total": 1, "date_sync": 1758000000 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarFuncionariosAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var funcionario = leitura.Funcionarios.Single(f => f.IdExterno == 2);

        Assert.Equal("Carlos Silva", funcionario.Nome);
        Assert.Equal(hashDaApi, funcionario.PdvKeyHash);   // como veio: já é um hash, não se re-hasheia
        Assert.True(PdvKeyHasher.Verificar("1234", funcionario.PdvKeyHash));
    }

    [Fact]
    public async Task PdvKeyEmClaroNuncaEhGravada()
    {
        // Se a API um dia mandasse a chave em claro, ela não pode parar no banco local.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """
            { "current_page": 1, "data": [
                { "id": 1, "nome": "A", "usuario": { "pdv_key": "9999" } }
              ], "next_page_url": null, "total": 1 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarFuncionariosAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.Funcionarios.Single().PdvKeyHash);
    }

    [Fact]
    public async Task FuncionarioSemUsuarioFicaComPdvKeyHashNulo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """{ "current_page": 1, "data": [{"id": 3, "nome": "Supervisor Geral", "supervisor": true}], "next_page_url": null, "total": 1 }""";
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarFuncionariosAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.Funcionarios.Single().PdvKeyHash);
    }
}

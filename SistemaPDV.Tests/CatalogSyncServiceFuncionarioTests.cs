using System.Net;
using System.Runtime.Versioning;
using System.Text;
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
    public async Task SincronizaFuncionarioComPdvKeyHasheada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """
            { "current_page": 1, "data": [
                { "id": 2, "nome": "Carlos Silva", "cpf": "11122233344", "supervisor": false, "desativado": false,
                  "usuario": { "id": 2, "name": "Carlos Silva", "pdv_key": "1234" } }
              ], "next_page_url": null, "total": 1, "date_sync": 1758000000 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarFuncionariosAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var funcionario = leitura.Funcionarios.Single(f => f.IdExterno == 2);

        Assert.Equal("Carlos Silva", funcionario.Nome);
        Assert.NotNull(funcionario.PdvKeyHash);
        Assert.NotEqual("1234", funcionario.PdvKeyHash);
        Assert.Equal(64, funcionario.PdvKeyHash!.Length); // SHA-256 em hex
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

    [Fact]
    public async Task MesmaPdvKeySempreGeraOMesmoHash()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);

        var json = """
            { "current_page": 1, "data": [
                { "id": 1, "nome": "A", "usuario": { "pdv_key": "9999" } },
                { "id": 2, "nome": "B", "usuario": { "pdv_key": "9999" } }
              ], "next_page_url": null, "total": 2 }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(json));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await service.SincronizarFuncionariosAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        var hashes = leitura.Funcionarios.Select(f => f.PdvKeyHash).Distinct().ToList();
        Assert.Single(hashes); // mesma pdv_key -> mesmo hash, comparÃ¡vel no login local
    }
}

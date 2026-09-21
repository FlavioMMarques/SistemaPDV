using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Cartões (GET .../softauth/api/financeiros/cartoes/page/N): a lista de bandeiras do app. Formato REAL conferido em
// 2026-09-20: rota SEM "v2", envelope {code,message,human,data[],meta.page{current,prev,next,count},date_sync}, tudo
// como TEXTO (inclusive bandeira_id "02", com zero à esquerda) e HTTP 500 "Invalid pagination interval." fora do intervalo.
[SupportedOSPlatform("windows")]
public class CartoesSyncTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string Item(string id, string bandeiraId, string bandeiraNome, string tipo = "CREDITO", string credenciadora = "REDE", string taxa = "0.01") =>
        $$"""{"id":"{{id}}","credenciadora":"{{credenciadora}}","cnpj":"01425787000104","nome":"{{tipo}}","bandeira_id":"{{bandeiraId}}","bandeira_nome":"{{bandeiraNome}}","dia":"1","parcelas":"12","taxa_administrativa":"{{taxa}}","alias_cartao":"{{tipo}}","tipo":"{{tipo}}"}""";

    private static string Pagina(int atual, int? proxima, params string[] itens) =>
        "{\"code\":1,\"message\":\"OK\",\"human\":\"Sucesso\",\"data\":[" + string.Join(",", itens) + "]," +
        $"\"meta\":{{\"page\":{{\"current\":{atual},\"prev\":null,\"next\":{(proxima?.ToString() ?? "null")},\"count\":{itens.Length}}}}},\"date_sync\":1789924589}}";

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture, string urlApi = UrlApi)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = urlApi });
        await context.SaveChangesAsync();
    }

    private static CatalogSyncService CriarService(SqliteInMemoryFixture fixture, Func<string, HttpResponseMessage> servidor, List<string>? visitadas = null)
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(requisicao =>
        {
            var caminho = requisicao.RequestUri!.AbsolutePath.TrimStart('/');
            visitadas?.Add(caminho);
            return servidor(caminho);
        });
        return new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));
    }

    // ---- o formato real ----

    [Fact]
    public async Task LeOFormatoRealComTudoComoTextoEPreservaOZeroDaBandeira()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var visitadas = new List<string>();
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, Pagina(1, null, Item("15", "02", "MASTERCARD"))), visitadas);

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(new[] { "softauth/api/financeiros/cartoes/page/1" }, visitadas);   // sem "v2"
        using var leitura = fixture.CriarContexto();
        var cartao = Assert.Single(leitura.Cartoes);
        Assert.Equal(15, cartao.IdExterno);
        Assert.Equal("REDE", cartao.Credenciadora);
        Assert.Equal("02", cartao.BandeiraId);            // texto: com um inteiro viraria 2
        Assert.Equal("MASTERCARD", cartao.BandeiraNome);
        Assert.Equal("CREDITO", cartao.Tipo);
        Assert.Equal("CREDITO", cartao.AliasCartao);
        Assert.Equal(1, cartao.Dia);
        Assert.Equal(12, cartao.Parcelas);
        Assert.Equal(0.01m, cartao.TaxaAdministrativa);
        Assert.Equal(SyncStatus.Sincronizado, cartao.SyncStatus);
    }

    [Fact]
    public async Task BandeiraIdQueChegaComoNumeroTambemEhLida()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var corpo = Pagina(1, null, Item("1", "02", "VISA")).Replace("\"bandeira_id\":\"02\"", "\"bandeira_id\":2").Replace("\"id\":\"1\"", "\"id\":1");
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, corpo));

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal("2", leitura.Cartoes.Single().BandeiraId);
    }

    [Fact]
    public void NaoGuardaOCnpjDaCredenciadora() =>
        Assert.Null(typeof(Cartao).GetProperty("Cnpj"));   // dado a menos para guardar/vazar; o app não usa

    // ---- paginação ----

    [Fact]
    public async Task SegueMetaPageNextAteAUltimaPaginaSemSeguirUrlDoServidor()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var visitadas = new List<string>();
        var service = CriarService(fixture, caminho => caminho switch
        {
            var c when c.EndsWith("/page/1") => Json(HttpStatusCode.OK, Pagina(1, 2, Item("1", "01", "VISA"))),
            var c when c.EndsWith("/page/2") => Json(HttpStatusCode.OK, Pagina(2, 3, Item("2", "02", "MASTERCARD"))),
            var c when c.EndsWith("/page/3") => Json(HttpStatusCode.OK, Pagina(3, null, Item("3", "03", "ELO"))),
            _ => Json(HttpStatusCode.InternalServerError, """{"error":"Invalid pagination interval."}"""),
        }, visitadas);

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(3, visitadas.Count);   // nunca pede a página 4 (o servidor responderia 500)
        using var leitura = fixture.CriarContexto();
        Assert.Equal(new[] { "ELO", "MASTERCARD", "VISA" }, leitura.Cartoes.Select(c => c.BandeiraNome).OrderBy(n => n));
    }

    [Fact]
    public async Task ProximaPaginaQueNaoAvancaInterrompeEmVezDeEntrarEmLaco()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var chamadas = 0;
        var service = CriarService(fixture, _ => { chamadas++; return Json(HttpStatusCode.OK, Pagina(1, 1, Item("1", "01", "VISA"))); });

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.False(resultado.Sucesso);
        Assert.Contains("repetiu", resultado.Mensagem);
        Assert.Equal(1, chamadas);
    }

    [Fact]
    public async Task EmpresaSemNenhumCartaoNaoEhFalhaMesmoQueAApiResponda500NaPrimeiraPagina()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, _ => Json(HttpStatusCode.InternalServerError, """{"error":"Invalid pagination interval."}"""));

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(0, resultado.Quantidade);
    }

    // ---- sincronização por substituição ----

    private static async Task SemearCartaoLocalAsync(SqliteInMemoryFixture fixture, int idExterno, string bandeiraNome)
    {
        await using var context = fixture.CriarContexto();
        context.Cartoes.Add(new Cartao { IdExterno = idExterno, BandeiraNome = bandeiraNome, BandeiraId = "99", Tipo = "CREDITO", Nome = "X", Credenciadora = "REDE", AliasCartao = "X" });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task CartaoQueSumiuDaApiSaiDoBancoLocalEOsNovosEntram()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCartaoLocalAsync(fixture, 1, "VISA");       // continua na API
        await SemearCartaoLocalAsync(fixture, 2, "HIPERCARD");  // sumiu da API
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, Pagina(1, null, Item("1", "01", "VISA"), Item("3", "03", "ELO"))));

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(new[] { "ELO", "VISA" }, leitura.Cartoes.Select(c => c.BandeiraNome).OrderBy(n => n));
    }

    [Fact]
    public async Task SegundoCicloIgualNaoContaComoMudancaParaNaoRecarregarTelasAToa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, Pagina(1, null, Item("1", "01", "VISA"), Item("2", "02", "MASTERCARD"))));

        var primeira = await service.SincronizarCartoesAsync("t");
        var segunda = await service.SincronizarCartoesAsync("t");

        Assert.Equal(2, primeira.Quantidade);   // dois cartões novos
        Assert.Equal(0, segunda.Quantidade);    // a lista toda é baixada de novo, mas nada mudou
    }

    [Fact]
    public async Task MudancaEmUmCartaoExistenteAtualizaSemDuplicar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCartaoLocalAsync(fixture, 1, "VISA");
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, Pagina(1, null, Item("1", "01", "VISA", taxa: "0.05"))));

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.Equal(1, resultado.Quantidade);
        using var leitura = fixture.CriarContexto();
        var cartao = Assert.Single(leitura.Cartoes);
        Assert.Equal(0.05m, cartao.TaxaAdministrativa);
        Assert.Equal("01", cartao.BandeiraId);
    }

    [Fact]
    public async Task IdRepetidoEntrePaginasValeOUltimoENaoQuebraOIndiceUnico()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, caminho => caminho.EndsWith("/page/1")
            ? Json(HttpStatusCode.OK, Pagina(1, 2, Item("1", "01", "VISA")))
            : Json(HttpStatusCode.OK, Pagina(2, null, Item("1", "01", "VISA", taxa: "0.09"))));

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(0.09m, Assert.Single(leitura.Cartoes).TaxaAdministrativa);
    }

    // ---- falhas: nunca esvaziam a lista local ----

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":"Resource not found."}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"message":"Access token expired."}""")]
    [InlineData(HttpStatusCode.OK, "<html>portal cativo</html>")]
    public async Task FalhaNaBuscaNaoApagaOsCartoesJaSincronizados(HttpStatusCode status, string corpo)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCartaoLocalAsync(fixture, 1, "VISA");
        var service = CriarService(fixture, _ => Json(status, corpo));

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal("VISA", Assert.Single(leitura.Cartoes).BandeiraNome);
    }

    [Fact]
    public async Task FalhaNaSegundaPaginaTambemNaoApagaNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCartaoLocalAsync(fixture, 1, "VISA");
        var service = CriarService(fixture, caminho => caminho.EndsWith("/page/1")
            ? Json(HttpStatusCode.OK, Pagina(1, 2, Item("2", "02", "MASTERCARD")))
            : Json(HttpStatusCode.InternalServerError, """{"error":"boom"}"""));

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(new[] { "VISA" }, leitura.Cartoes.Select(c => c.BandeiraNome));   // nem a metade que chegou entra
    }

    [Fact]
    public async Task ConexaoSemHttpsNaoChamaNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, urlApi: "http://exemplo.softcomshop.com.br/registrar?client_id=1");
        var chamou = false;
        var service = CriarService(fixture, _ => { chamou = true; return Json(HttpStatusCode.OK, Pagina(1, null)); });

        var resultado = await service.SincronizarCartoesAsync("t");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);   // o access token nunca vai em http://
    }
}

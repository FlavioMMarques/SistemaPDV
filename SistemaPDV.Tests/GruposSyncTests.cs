using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Grupos (categorias) de produto (GET .../softauth/api/v2/produtos/grupos): de onde vem o nome da categoria no card do PDV e na
// tela de Cadastros. Formato REAL conferido em 2026-09-21 (só leitura): envelope padrão de página (current_page/data/
// next_page_url), 50 por página, dezenas de campos por item (o app só usa id e nome); a rota com "/page/N" responde 500.
[SupportedOSPlatform("windows")]
public class GruposSyncTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    // Um item no formato real (com os campos que o app ignora), para provar que eles não atrapalham.
    private static string Item(int id, string nome) =>
        $$"""{"id":{{id}},"parent_id":null,"nome":"{{nome}}","editavel":"S","vender":true,"created_at":"2020-01-01 10:00:00","imagem":"x.png","armacao":false,"lente":false,"restaurante_familia_id":null,"observacoes":[],"adicionais":[],"hortifruit":false,"restricao_idade":false}""";

    private static string Pagina(int atual, string? proxima, params string[] itens) =>
        $$"""{"current_page":{{atual}},"data":[{{string.Join(",", itens)}}],"next_page_url":{{(proxima is null ? "null" : "\"" + proxima + "\"")}},"date_sync":1789924589}""";

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
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

    [Fact]
    public async Task LeOFormatoRealNaRotaV2SemPageNoCaminho()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var visitadas = new List<string>();
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, Pagina(1, null, Item(3, "Mercearia"), Item(4, "Bebidas"))), visitadas);

        var resultado = await service.SincronizarGruposAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(2, resultado.Quantidade);
        Assert.Equal(new[] { "softauth/api/v2/produtos/grupos" }, visitadas);        // com "/page/1" a API real responde 500
        using var leitura = fixture.CriarContexto();
        var nomes = leitura.Grupos.AsNoTracking().ToDictionary(g => g.IdExterno!.Value, g => g.Nome);
        Assert.Equal("Mercearia", nomes[3]);
        Assert.Equal("Bebidas", nomes[4]);
        Assert.All(leitura.Grupos, g => Assert.Equal(SyncStatus.Sincronizado, g.SyncStatus));
    }

    [Fact]
    public async Task SegueAsPaginasPeloNextPageUrl()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        // A API real não devolve per_page no next_page_url — o cliente reforça esse parâmetro (ver
        // GarantirPerPage), então a 2ª página também vem com "...page=2&per_page=200"; "?page=2"/"&page=2"
        // ancorado evita o falso positivo de "per_page=200" conter a substring "page=2".
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(requisicao => Json(HttpStatusCode.OK,
            requisicao.RequestUri!.Query.Contains("?page=2") || requisicao.RequestUri!.Query.Contains("&page=2")
            ? Pagina(2, null, Item(2, "Segunda"))
            : Pagina(1, "https://exemplo.softcomshop.com.br/softauth/api/v2/produtos/grupos?page=2", Item(1, "Primeira"))));
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        var resultado = await service.SincronizarGruposAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(new[] { "Primeira", "Segunda" }, leitura.Grupos.OrderBy(g => g.IdExterno).Select(g => g.Nome).ToArray());
    }


    [Fact]
    public async Task SegundaRodadaIgualNaoMudaNadaEAlteradoCriadoERemovidoSaoRefletidos()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var resposta = Pagina(1, null, Item(1, "Mercearia"), Item(2, "Bebidas"));
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, resposta));
        await service.SincronizarGruposAsync("t");

        var igual = await service.SincronizarGruposAsync("t");
        Assert.Equal(0, igual.Quantidade);                                            // o ciclo de 5 min não recarrega as telas à toa

        resposta = Pagina(1, null, Item(1, "Mercearia e Grãos"), Item(3, "Higiene"));   // 1 renomeado, 2 sumiu, 3 novo
        var mudou = await service.SincronizarGruposAsync("t");

        Assert.True(mudou.Sucesso);
        Assert.True(mudou.Quantidade > 0);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(new[] { "Mercearia e Grãos", "Higiene" }, leitura.Grupos.OrderBy(g => g.IdExterno).Select(g => g.Nome).ToArray());
    }

    [Fact]
    public async Task RespostaVaziaDeQuemJaTinhaGruposNaoApagaAsCategorias()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var resposta = Pagina(1, null, Item(1, "Mercearia"), Item(2, "Bebidas"));
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK, resposta));
        await service.SincronizarGruposAsync("t");

        resposta = Pagina(1, null);                                                   // a API respondeu 200 com lista vazia
        var resultado = await service.SincronizarGruposAsync("t");

        Assert.True(resultado.Sucesso);
        Assert.Equal(0, resultado.Quantidade);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(2, leitura.Grupos.Count());                                       // as categorias continuam
    }

    [Fact]
    public async Task FalhaNoMeioDaBuscaNaoEsvaziaAListaLocal()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var falhar = false;
        var service = CriarService(fixture, _ => falhar
            ? Json(HttpStatusCode.InternalServerError, """{"error":"Invalid pagination interval."}""")
            : Json(HttpStatusCode.OK, Pagina(1, null, Item(1, "Mercearia"))));
        await service.SincronizarGruposAsync("t");

        falhar = true;
        var resultado = await service.SincronizarGruposAsync("t");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal("Mercearia", Assert.Single(leitura.Grupos).Nome);                // o que já havia continua lá
    }

    [Fact]
    public async Task GrupoSemNomeViraTextoVazioEONomeVemSemEspacosSobrando()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, _ => Json(HttpStatusCode.OK,
            Pagina(1, null, Item(1, "  Mercearia  "), """{"id":2,"nome":null}""")));

        var resultado = await service.SincronizarGruposAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var nomes = leitura.Grupos.OrderBy(g => g.IdExterno).Select(g => g.Nome).ToArray();
        Assert.Equal(new[] { "Mercearia", "" }, nomes);
    }
}

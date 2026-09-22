using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Push de produto criado localmente (modal "Cadastrar Produto") para POST .../produtos/importacao/produto — EXPERIMENTAL
// (2026-09-22): trocado do cadastro em lote (v2) para este endpoint de importação (v1, sem "/v2/"), na aposta de que
// "produto_grade[].preco_venda" grava o preço no registro da EMPRESA, coisa que o lote nunca fazia (#86/#94). A forma da
// resposta é uma hipótese (ver ProdutoImportacaoApiDto.cs); estes testes fixam o formato que o código espera — se a API
// real responder diferente, é aqui que se ajusta depois do teste com produto de verdade.
[SupportedOSPlatform("windows")]
public class CatalogSyncServiceProdutoNovoTests
{
    // Resposta hipotética: mesmo formato do item criado pelo lote antigo, só que direto em "data" (um produto só, sem
    // "created[]"). Produto-base 1049, grade da empresa 44 = 122848.
    private const string RespostaOk = """
        { "data": { "id": 1049, "nome": "Café Torrado 500g", "produto_empresas": [
            { "id": 53231, "empresa_id": "44", "produto_empresa_grade": { "id": 122848, "sku": "UNICO", "preco_venda": "12.50" } } ] } }
        """;

    private static HttpResponseMessage Resposta(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture, string urlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1")
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = urlApi });
        await context.SaveChangesAsync();
    }

    private static async Task<int> SemearProdutoAsync(
        SqliteInMemoryFixture fixture, string nome = "Café Torrado 500g", int? grupoId = 7, decimal preco = 12.5m,
        string? codigoBarras = "7891234567890", string? referencia = null, SyncStatus status = SyncStatus.PendenteSync, decimal? precoCompra = null)
    {
        await using var context = fixture.CriarContexto();
        var produto = new Produto
        {
            Nome = nome, GrupoId = grupoId, PrecoVenda = preco, CodigoBarras = codigoBarras, Referencia = referencia, SyncStatus = status, PrecoCompra = precoCompra,
        };
        context.Produtos.Add(produto);
        await context.SaveChangesAsync();
        return produto.Id;
    }

    private static CatalogSyncService CriarService(SqliteInMemoryFixture fixture, HttpClient httpClient) =>
        new(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

    [Fact]
    public async Task SucessoGravaOsDoisIdsQueAVendaUsaEMarcaSincronizado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, RespostaOk));

        var resultado = await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Equal(122848, produto.IdExterno);      // "produto_empresa_grade_id" da venda
        Assert.Equal(1049, produto.ProdutoIdApi);     // "produto_id" da venda
        Assert.Equal(SyncStatus.Sincronizado, produto.SyncStatus);
        Assert.Null(produto.UltimoErroSync);
        Assert.Equal(0, produto.TentativasEnvio);
    }

    [Fact]
    public async Task CorpoVaiParaOEndpointDeImportacaoComOsCamposDoCadastroEOPrecoNaGrade()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture);
        HttpRequestMessage? requisicao = null;
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            requisicao = req;
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta(HttpStatusCode.OK, RespostaOk);
        });

        await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        Assert.Equal(HttpMethod.Post, requisicao!.Method);
        // Rota v1, sem "/v2/" — como softauth/api/financeiros/cartoes (SoftcomRotas.ProdutosImportar).
        Assert.Equal("https://exemplo.softcomshop.com.br/softauth/api/produtos/importacao/produto", requisicao.RequestUri!.ToString());
        Assert.Equal("Bearer", requisicao.Headers.Authorization!.Scheme);

        using var json = JsonDocument.Parse(corpo!);
        var p = json.RootElement;
        Assert.Equal("Café Torrado 500g", p.GetProperty("nome").GetString());
        Assert.Equal(7, p.GetProperty("grupo_id").GetInt32());
        Assert.Equal(12.5m, p.GetProperty("preco_venda").GetDecimal());
        Assert.Equal("7891234567890", p.GetProperty("codigo_barras").GetString());
        Assert.False(p.TryGetProperty("referencia", out _));   // nulo não vai
        // A peça nova: o preço vai também no registro da empresa (produto_grade), que é o que o lote v2 nunca grava.
        var grade = p.GetProperty("produto_grade");
        Assert.Equal(1, grade.GetArrayLength());
        Assert.Equal(12.5m, grade[0].GetProperty("preco_venda").GetDecimal());
        // Sem custo informado, custo/margem/comissão NÃO vão (nem zerados): com eles a API pode recalcular o preço de venda e
        // anular o preço informado (o produto de teste chegou com preco_venda 0,00 na empresa, 2026-09-21).
        Assert.False(p.TryGetProperty("preco_compra", out _));
        Assert.False(p.TryGetProperty("margem_lucro", out _));
        Assert.False(p.TryGetProperty("percentual_comissao_produto", out _));
        // Os padrões vão explícitos; neste endpoint o Swagger os documenta como inteiro (0/1), não booleano.
        Assert.Equal(1, p.GetProperty("vender").GetInt32());
        Assert.Equal(1, p.GetProperty("controlar_estoque").GetInt32());
        Assert.Equal(0, p.GetProperty("desativado").GetInt32());
        // Nada de campo interno local vazando.
        Assert.DoesNotContain("SyncStatus", corpo);
        Assert.DoesNotContain("UltimoErro", corpo);
        Assert.DoesNotContain("token-fake", corpo);
    }

    [Fact]
    public async Task CustoInformadoVaiComoPrecoCompraSemMargemNemComissao()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture, precoCompra: 8.25m);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta(HttpStatusCode.OK, RespostaOk);
        });

        await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        using var json = JsonDocument.Parse(corpo!);
        var p = json.RootElement;
        Assert.Equal(8.25m, p.GetProperty("preco_compra").GetDecimal());
        Assert.Equal(12.5m, p.GetProperty("preco_venda").GetDecimal());
        Assert.False(p.TryGetProperty("margem_lucro", out _));
        Assert.False(p.TryGetProperty("percentual_comissao_produto", out _));
    }

    [Fact]
    public async Task CodigoQueNaoEhBarrasVaiComoReferencia()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture, codigoBarras: null, referencia: "CAFE-500");
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta(HttpStatusCode.OK, RespostaOk);
        });

        await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        using var json = JsonDocument.Parse(corpo!);
        var p = json.RootElement;
        Assert.Equal("CAFE-500", p.GetProperty("referencia").GetString());
        Assert.False(p.TryGetProperty("codigo_barras", out _));
    }

    [Fact]
    public async Task ComVariasEmpresasNaRespostaUsaAGradeDaEmpresaDoAparelho()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 44 });
            await context.SaveChangesAsync();
        }
        var id = await SemearProdutoAsync(fixture);
        const string duasEmpresas = """
            { "data": { "id": 1049, "produto_empresas": [
                { "empresa_id": "12", "produto_empresa_grade": { "id": 111 } },
                { "empresa_id": "44", "produto_empresa_grade": { "id": 444 } } ] } }
            """;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, duasEmpresas));

        await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.Equal(444, leitura.Produtos.Single().IdExterno);
    }

    [Theory]
    [InlineData("""{ "data": { "id": 0, "produto_empresas": [] } }""")]          // id inválido
    [InlineData("""{ "data": { "id": 5, "produto_empresas": [] } }""")]           // sem grade
    [InlineData("""{ "data": { "id": 5, "produto_empresas": [ { "empresa_id": "1", "produto_empresa_grade": { "id": 0 } } ] } }""")]
    [InlineData("""{ "data": null }""")]                                          // sem "data"
    [InlineData("nao e json")]
    public async Task RespostaSemOsIdsNaoContaComoEnviado(string resposta)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, resposta));

        var resultado = await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Null(produto.IdExterno);
        Assert.Null(produto.ProdutoIdApi);
        Assert.Equal(SyncStatus.FalhaSync, produto.SyncStatus);
        Assert.False(string.IsNullOrWhiteSpace(produto.UltimoErroSync));
    }

    [Fact]
    public async Task Erro422DaApiFicaVisivelNoProdutoEEntraNaEsperaCrescente()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(
            (HttpStatusCode)422, """{ "errors": { "nome": ["Nome deve ser único."] } }"""));

        var resultado = await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Equal(SyncStatus.FalhaSync, produto.SyncStatus);
        Assert.Contains("único", produto.UltimoErroSync);
        Assert.Equal(1, produto.TentativasEnvio);
        Assert.NotNull(produto.ProximaTentativaEm);
        Assert.Null(produto.IdExterno);
    }

    [Fact]
    public async Task Erro500ComErrorsEmTextoTambemFicaVisivel()
    {
        // O 500 do Swagger: { "message": "...", "errors": "O nome informado já se encontra utilizado." } (errors é texto).
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(
            HttpStatusCode.InternalServerError,
            """{ "message": "Erro ao processar importação. Nenhuma alteração foi aplicada.", "errors": "O nome informado já se encontra utilizado." }"""));

        await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.Contains("já se encontra utilizado", leitura.Produtos.Single().UltimoErroSync);
    }

    [Fact]
    public async Task ProdutoSemCategoriaNaoSaiEFicaComOMotivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture, grupoId: null);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, RespostaOk); });

        var resultado = await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        Assert.Contains("categoria", leitura.Produtos.Single().UltimoErroSync);
    }

    [Fact]
    public async Task UrlSemHttpsNaoEnviaENaoMarcaOProduto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, urlApi: "http://exemplo.softcomshop.com.br/registrar?client_id=1");
        var id = await SemearProdutoAsync(fixture);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, RespostaOk); });

        var resultado = await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Equal(SyncStatus.PendenteSync, produto.SyncStatus);   // problema de configuração, não do produto
        Assert.Null(produto.UltimoErroSync);
    }

    [Fact]
    public async Task ProdutoQueVeioDaApiNaoEhReenviado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        int id;
        await using (var context = fixture.CriarContexto())
        {
            var produto = new Produto { Nome = "Da API", IdExterno = 206, ProdutoIdApi = 77, SyncStatus = SyncStatus.Sincronizado };
            context.Produtos.Add(produto);
            await context.SaveChangesAsync();
            id = produto.Id;
        }
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, RespostaOk); });

        var resultado = await CriarService(fixture, httpClient).SincronizarProdutoNovoAsync(id, "token-fake");

        Assert.True(resultado.Sucesso);
        Assert.False(chamou);
    }

    [Fact]
    public async Task LoteEnviaSoOsPendentesEUmComErroNaoTravaOsOutros()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var ruim = await SemearProdutoAsync(fixture, nome: "Repetido", codigoBarras: "1");
        var bom = await SemearProdutoAsync(fixture, nome: "Novo", codigoBarras: "2");
        await SemearProdutoAsync(fixture, nome: "Antigo", codigoBarras: "3", status: SyncStatus.Sincronizado);   // sem IdExterno mas já sincronizado
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            chamadas++;
            var corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return corpo.Contains("Repetido")
                ? Resposta((HttpStatusCode)422, """{ "errors": { "nome": ["Nome deve ser único."] } }""")
                : Resposta(HttpStatusCode.OK, RespostaOk);
        });

        var resultado = await CriarService(fixture, httpClient).SincronizarProdutosNovosPendentesAsync("token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(1, resultado.Quantidade);
        Assert.Equal(2, chamadas);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.FalhaSync, leitura.Produtos.Single(p => p.Id == ruim).SyncStatus);
        Assert.Equal(SyncStatus.Sincronizado, leitura.Produtos.Single(p => p.Id == bom).SyncStatus);
    }

    [Fact]
    public async Task ProdutoEmEsperaCrescenteNaoEhTentadoDeNovoAntesDoTempo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearProdutoAsync(fixture);
        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamadas++;
            return Resposta((HttpStatusCode)422, """{ "errors": { "nome": ["Nome deve ser único."] } }""");
        });
        var service = CriarService(fixture, httpClient);

        await service.SincronizarProdutosNovosPendentesAsync("token-fake");
        await service.SincronizarProdutosNovosPendentesAsync("token-fake");   // logo em seguida: ainda na espera

        Assert.Equal(1, chamadas);
    }
}

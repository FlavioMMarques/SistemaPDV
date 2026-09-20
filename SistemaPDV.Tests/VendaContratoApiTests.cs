using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Contrato do POST de venda com a API REAL. A API (422) recusou o corpo mínimo que o app enviava:
// faltavam numero_documento, cancelada, bloqueada e produtos[].produto_empresa_grade_id. E o
// produto tem TRÊS ids distintos (id, produto_id, produto_empresa_id): o app mandava o `id` como
// produto_id. Mapeamento adotado (HIPÓTESE a confirmar na primeira venda real):
//   produtos[].produto_empresa_grade_id = `id` do item na listagem (Produto.IdExterno)
//   produtos[].produto_id               = `produto_id` da listagem (Produto.ProdutoIdApi)
[SupportedOSPlatform("windows")]
public class VendaContratoApiTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task<(int CaixaId, int ProdutoId, int FormaId)> SemearBaseAsync(SqliteInMemoryFixture fixture, int? produtoIdApi = 77)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
        var produto = new Produto { Nome = "Refri", PrecoVenda = 40m, PrecoCompra = 6.50m, IdExterno = 206, ProdutoIdApi = produtoIdApi };
        var forma = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
        context.AddRange(funcionario, produto, forma);
        context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi, ClienteConsumidorFinalIdExterno = 1 });
        await context.SaveChangesAsync();

        var caixa = new Models.Caixa
        {
            FuncionarioId = funcionario.Id, IdExterno = 28, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1,
            DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0), TrocoInicial = 10m, AberturaSincronizada = true,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();
        return (caixa.Id, produto.Id, forma.Id);
    }

    private static Task<Venda> RegistrarAsync(SqliteInMemoryFixture fixture, (int CaixaId, int ProdutoId, int FormaId) b) =>
        new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            b.CaixaId, null, new[] { (b.ProdutoId, 1m, 40m, 0m, 0m) }, new[] { (b.FormaId, 40m) });

    // ---- número do pedido (numero_documento) ----

    [Fact]
    public async Task VendasRecebemNumeroDePedidoSequencial()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);

        var v1 = await RegistrarAsync(fixture, b);
        var v2 = await RegistrarAsync(fixture, b);
        var v3 = await RegistrarAsync(fixture, b);

        Assert.Equal(new[] { 1, 2, 3 }, new[] { v1.NumeroPedido, v2.NumeroPedido, v3.NumeroPedido });
    }

    [Fact]
    public async Task NumeroDePedidoContinuaDepoisDeReabrirOApp()
    {
        // Outro VendaService/contexto = "app reaberto": o próximo número sai do banco, não da memória.
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        await RegistrarAsync(fixture, b);
        await RegistrarAsync(fixture, b);

        var proxima = await RegistrarAsync(fixture, b);

        Assert.Equal(3, proxima.NumeroPedido);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(new[] { 1, 2, 3 }, leitura.Vendas.OrderBy(v => v.NumeroPedido).Select(v => v.NumeroPedido).ToArray());
    }

    [Fact]
    public async Task ListagemMostraONumeroDoPedidoMesmoOffline()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        await RegistrarAsync(fixture, b);
        await RegistrarAsync(fixture, b);

        var vendas = await new VendaLocalService(fixture.CriarContexto).ListarVendasDoCaixaAsync(b.CaixaId);

        Assert.Equal(new int?[] { 2, 1 }, vendas.Select(v => v.Numero).ToArray());   // mais recente primeiro
    }

    // ---- corpo do POST ----

    [Fact]
    public async Task CorpoTemOsCamposQueAApiExigiaEOsIdsCertosDoProduto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            corpo = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "id": 900 } }""");
        });

        var resultado = await new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarVendaAsync(venda.Id, "t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var doc = JsonDocument.Parse(corpo!);
        var raiz = doc.RootElement;
        Assert.Equal(JsonValueKind.String, raiz.GetProperty("numero_documento").ValueKind);   // string no Swagger
        Assert.Equal("1", raiz.GetProperty("numero_documento").GetString());
        Assert.False(raiz.GetProperty("cancelada").GetBoolean());
        Assert.False(raiz.GetProperty("bloqueada").GetBoolean());

        var produto = raiz.GetProperty("produtos")[0];
        Assert.Equal("77", produto.GetProperty("produto_id").ToString());                    // produto_id da listagem
        Assert.Equal("206", produto.GetProperty("produto_empresa_grade_id").ToString());     // id do item na listagem
    }

    [Fact]
    public async Task ItemEnviaOPrecoDeCompraEOsCamposNaoNulosDoSchema()
    {
        // A API real deu 500 ao gravar venda_item: "Column 'preco_compra' cannot be null". Os campos que o
        // Swagger NÃO marca como nullable vão sempre, com valor neutro quando o PDV não tem o dado.
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            corpo = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "id": 900 } }""");
        });

        await new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarVendaAsync(venda.Id, "t");

        using var doc = JsonDocument.Parse(corpo!);
        var item = doc.RootElement.GetProperty("produtos")[0];
        Assert.Equal(6.50m, item.GetProperty("preco_compra").GetDecimal());
        Assert.Equal(0m, item.GetProperty("comissao").GetDecimal());
        Assert.Equal(0m, item.GetProperty("comissao_atendente").GetDecimal());
        Assert.Equal(0m, item.GetProperty("percentual_comissao_venda").GetDecimal());
        Assert.False(item.GetProperty("composicao_automatica").GetBoolean());
        Assert.False(item.GetProperty("promocao_aplicada").GetBoolean());
    }

    [Fact]
    public async Task ProdutoSemPrecoDeCompraEnviaZeroEmVezDeNulo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            var produto = context.Produtos.Single();
            produto.PrecoCompra = null;
            await context.SaveChangesAsync();
        }
        var venda = await RegistrarAsync(fixture, b);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            corpo = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "id": 900 } }""");
        });

        await new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarVendaAsync(venda.Id, "t");

        using var doc = JsonDocument.Parse(corpo!);
        Assert.Equal(0m, doc.RootElement.GetProperty("produtos")[0].GetProperty("preco_compra").GetDecimal());
    }

    [Fact]
    public async Task PagamentoEnviaONomeEOCodigoDaFormaQueOFinanceiroDaApiLe()
    {
        // API real: 500 "Não foi possível salvar o financeiro… Undefined index: api_nome_pagamento".
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            corpo = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "id": 900 } }""");
        });

        await new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarVendaAsync(venda.Id, "t");

        using var doc = JsonDocument.Parse(corpo!);
        var pagamento = doc.RootElement.GetProperty("pagamentos")[0];
        Assert.Equal("ESPÉCIE", pagamento.GetProperty("api_nome_pagamento").GetString());
        Assert.Equal("01", pagamento.GetProperty("api_codigo_pagamento").GetString());
        Assert.Equal(5, pagamento.GetProperty("forma_pagamento_id").GetInt32());
        Assert.Equal(40m, pagamento.GetProperty("valor_pagamento").GetDecimal());

        // Parcela única (à vista): a API real deu 500 "Undefined index: valor_parcela".
        Assert.Equal(40m, pagamento.GetProperty("valor_parcela").GetDecimal());
        Assert.Equal(40m, pagamento.GetProperty("valor_recebido").GetDecimal());
        Assert.Equal(1, pagamento.GetProperty("parcelas").GetInt32());
        Assert.Equal("1", pagamento.GetProperty("numero_parcela").GetString());
    }

    [Fact]
    public async Task ProdutoSemOIdBaseDaApiNaoEnviaEDeixaPendenteSemMarcarFalha()
    {
        // Produto sincronizado antes de o app guardar o produto_id: mandar o `id` no lugar registraria
        // OUTRO produto na venda. Espera a próxima sincronização de produtos em vez de adivinhar.
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture, produtoIdApi: null);
        var venda = await RegistrarAsync(fixture, b);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Json(HttpStatusCode.OK, "{}"); });

        var resultado = await new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarVendaAsync(venda.Id, "t");

        Assert.False(resultado.Sucesso);
        Assert.Contains("produto", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.PendenteSync, leitura.Vendas.Single().SyncStatus);
        Assert.Equal(0, leitura.Vendas.Single().TentativasEnvio);
    }

    // ---- sincronização de produtos guarda o produto_id ----

    private const string ProdutoJson = """
        {"current_page":1,"data":[{"id":206,"empresa_id":1,"produto_id":77,"produto_empresa_id":154,"nome":"Refrigerante","estoque":"5.000","preco_venda":"40.00","vender":true}],
        "next_page_url":null,"total":1,"date_sync":1789924589}
        """;

    [Fact]
    public async Task SincronizarProdutosGuardaOProdutoIdDaApi()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
            await context.SaveChangesAsync();
        }
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Json(HttpStatusCode.OK, ProdutoJson));
        var catalogo = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await catalogo.SincronizarProdutosAsync("t");

        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Equal(206, produto.IdExterno);
        Assert.Equal(77, produto.ProdutoIdApi);
    }

    [Fact]
    public async Task ProdutosJaSincronizadosSemOProdutoIdForcamBuscaCompletaEDepoisVoltaAoIncremental()
    {
        // Os 200 produtos já no banco vieram antes de existir a coluna: a sincronização incremental
        // (ultima_sincronizacao) não os traria de novo, e ficariam sem produto_id pra sempre.
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = UrlApi, UltimaSincronizacaoProdutos = DateTimeOffset.FromUnixTimeSeconds(1789900000),
            });
            context.Produtos.Add(new Produto { Nome = "Antigo", IdExterno = 206, ProdutoIdApi = null });
            await context.SaveChangesAsync();
        }
        var urls = new List<string>();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r => { urls.Add(r.RequestUri!.Query); return Json(HttpStatusCode.OK, ProdutoJson); });
        var catalogo = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await catalogo.SincronizarProdutosAsync("t");   // 1ª: falta produto_id -> busca completa
        await catalogo.SincronizarProdutosAsync("t");   // 2ª: já preenchido -> incremental

        Assert.DoesNotContain("ultima_sincronizacao", urls[0]);
        Assert.Contains("ultima_sincronizacao", urls[1]);
    }
}

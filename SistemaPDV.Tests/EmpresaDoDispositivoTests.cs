using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// O dispositivo é vinculado a UMA empresa, e o link de vínculo diz qual (empresa_cnpj). A API
// devolve todas as empresas do cliente (4, no dispositivo real) — antes o app gravava as 4
// (inclusive os certificados digitais das outras) e vendia pela primeira, que só estava certa por sorte.
[SupportedOSPlatform("windows")]
public class EmpresaDoDispositivoTests
{
    private const string CnpjDoDispositivo = "11222333000181";
    private const string OutroCnpj = "45997418000153";
    private const string UrlComEmpresa = "https://exemplo.softcomshop.com.br/registrar?client_id=1&empresa_name=Loja&empresa_cnpj=11.222.333%2F0001-81&device_name=PDV";
    private const string UrlSemEmpresa = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string Empresa(int id, string cnpj, string razao) =>
        $$"""{ "empresa_id": {{id}}, "empresa_cnpj": "{{cnpj}}", "empresa_razao_social": "{{razao}}", "empresa_certificado": "certificado-{{id}}", "empresa_certificado_senha": "senha-{{id}}" }""";

    private static string Pagina(params string[] empresas) =>
        $$"""{ "current_page": 1, "data": [ {{string.Join(",", empresas)}} ], "next_page_url": null, "total": {{empresas.Length}}, "date_sync": 1789924589 }""";

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture, string urlApi)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = urlApi, ClienteConsumidorFinalIdExterno = 1 });
        await context.SaveChangesAsync();
    }

    private static CatalogSyncService CriarCatalogo(SqliteInMemoryFixture fixture, string json)
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Json(HttpStatusCode.OK, json));
        return new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));
    }

    // ---- extrair o CNPJ do link ----

    [Theory]
    [InlineData("https://x.com/registrar?client_id=1&empresa_cnpj=11.222.333%2F0001-81", "11222333000181")]
    [InlineData("https://x.com/registrar?client_id=1&empresa_cnpj=11222333000181&device_name=PDV", "11222333000181")]
    [InlineData("https://x.com/registrar?client_id=1", null)]
    [InlineData("https://x.com/registrar?empresa_cnpj=", null)]
    [InlineData("https://x.com/registrar?empresa_cnpj=abc", null)]
    [InlineData("isto-nao-e-url", null)]
    [InlineData(null, null)]
    public void ExtrairEmpresaCnpjDoLinkDeVinculo(string? link, string? esperado)
    {
        Assert.Equal(esperado, SoftcomAuthService.ExtrairEmpresaCnpj(link));
    }

    // ---- sincronização ----

    [Fact]
    public async Task GravaSoAEmpresaDoDispositivoENaoOsCertificadosDasOutras()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, UrlComEmpresa);
        var catalogo = CriarCatalogo(fixture, Pagina(
            Empresa(1, OutroCnpj, "Outra Ltda"), Empresa(2, CnpjDoDispositivo, "Minha Loja Ltda"), Empresa(3, "00000000000191", "Terceira")));

        var resultado = await catalogo.SincronizarEmpresaAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(1, resultado.Quantidade);
        using var leitura = fixture.CriarContexto();
        var empresa = Assert.Single(leitura.Empresas);
        Assert.Equal(2, empresa.IdExterno);
        Assert.Equal("Minha Loja Ltda", empresa.RazaoSocial);
    }

    [Fact]
    public async Task NenhumaEmpresaComOCnpjDoLinkEhFalhaClara()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, UrlComEmpresa);
        var catalogo = CriarCatalogo(fixture, Pagina(Empresa(1, OutroCnpj, "Outra Ltda")));

        var resultado = await catalogo.SincronizarEmpresaAsync("t");

        Assert.False(resultado.Sucesso);
        Assert.Contains("CNPJ", resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Empresas);
    }

    [Fact]
    public async Task LinkSemCnpjComUmaUnicaEmpresaUsaEssaEmpresa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, UrlSemEmpresa);
        var catalogo = CriarCatalogo(fixture, Pagina(Empresa(7, OutroCnpj, "Unica Ltda")));

        var resultado = await catalogo.SincronizarEmpresaAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(7, leitura.Empresas.Single().IdExterno);
    }

    [Fact]
    public async Task LinkSemCnpjComVariasEmpresasNaoAdivinha()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, UrlSemEmpresa);
        var catalogo = CriarCatalogo(fixture, Pagina(Empresa(1, OutroCnpj, "A"), Empresa(2, CnpjDoDispositivo, "B")));

        var resultado = await catalogo.SincronizarEmpresaAsync("t");

        Assert.False(resultado.Sucesso);
        Assert.Contains("vincul", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Empresas);
    }

    [Fact]
    public async Task RemoveEmpresasDeOutrosCnpjsQueUmaVersaoAnteriorGravouMesmoSemNadaNovoNaApi()
    {
        // Versões anteriores gravaram todas as empresas (com certificado digital e senha das outras);
        // o app agora fica só com a do dispositivo — mesmo numa sincronização incremental vazia.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, UrlComEmpresa);
        await using (var context = fixture.CriarContexto())
        {
            context.Empresas.AddRange(
                new Empresa { RazaoSocial = "Outra", Cnpj = OutroCnpj, IdExterno = 1, CertificadoProtegido = "certificado-protegido" },
                new Empresa { RazaoSocial = "Minha Loja", Cnpj = CnpjDoDispositivo, IdExterno = 2 },
                new Empresa { RazaoSocial = "Terceira", Cnpj = "00000000000191", IdExterno = 3, CertificadoProtegido = "outro" });
            await context.SaveChangesAsync();
        }
        var catalogo = CriarCatalogo(fixture, Pagina());

        var resultado = await catalogo.SincronizarEmpresaAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(2, Assert.Single(leitura.Empresas).IdExterno);
    }

    // ---- venda ----

    private static async Task<Guid> SemearVendaComDuasEmpresasAsync(SqliteInMemoryFixture fixture)
    {
        int caixaId, produtoId, formaId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 9.90m, IdExterno = 10 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
            context.AddRange(funcionario, produto, forma);
            // A "primeira" empresa NÃO é a do dispositivo.
            context.Empresas.Add(new Empresa { RazaoSocial = "Outra", Cnpj = OutroCnpj, IdExterno = 1 });
            context.Empresas.Add(new Empresa { RazaoSocial = "Minha Loja", Cnpj = CnpjDoDispositivo, IdExterno = 2 });
            await context.SaveChangesAsync();

            var caixa = new Models.Caixa
            {
                FuncionarioId = funcionario.Id, IdExterno = 24, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1,
                DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0), TrocoInicial = 10m, AberturaSincronizada = true,
            };
            context.Caixas.Add(caixa);
            await context.SaveChangesAsync();
            (caixaId, produtoId, formaId) = (caixa.Id, produto.Id, forma.Id);
        }

        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaId, 9.90m) });
        return venda.Id;
    }

    [Fact]
    public async Task VendaUsaAEmpresaDoCnpjDoLinkENaoAPrimeira()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, UrlComEmpresa);
        var vendaId = await SemearVendaComDuasEmpresasAsync(fixture);
        string? corpoEnviado = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            corpoEnviado = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "id": 501 } }""");
        });
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var doc = System.Text.Json.JsonDocument.Parse(corpoEnviado!);
        Assert.Equal(2, doc.RootElement.GetProperty("empresa_id").GetInt32());
    }

    [Fact]
    public async Task VendaComLinkQueApontaParaEmpresaNaoSincronizadaFicaPendenteSemMarcarFalha()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, "https://exemplo.softcomshop.com.br/registrar?client_id=1&empresa_cnpj=00000000000191");
        var vendaId = await SemearVendaComDuasEmpresasAsync(fixture);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Json(HttpStatusCode.OK, "{}"); });
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "t");

        Assert.False(resultado.Sucesso);
        Assert.Contains("empresa", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.PendenteSync, leitura.Vendas.Single().SyncStatus);
    }
}

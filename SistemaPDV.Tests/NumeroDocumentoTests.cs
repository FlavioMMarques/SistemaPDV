using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// numero_documento é único POR EMPRESA na API e cada PDV numera offline: sem um prefixo por dispositivo, dois PDVs
// da mesma empresa gerariam "1", "2", "3"… e a API recusaria (ou, pior, misturaria) as vendas do segundo.
[SupportedOSPlatform("windows")]
public class NumeroDocumentoTests
{
    // ---- formato ----

    [Theory]
    [InlineData(null, 45, "45")]
    [InlineData("", 45, "45")]
    [InlineData("02", 45, "02-000045")]
    [InlineData("LOJA1", 1, "LOJA1-000001")]
    [InlineData("02", 1234567, "02-1234567")]   // passou de 6 dígitos: não corta, só deixa de completar com zeros
    public void FormataCodigoMaisSequencialComSeisDigitos(string? codigo, int numero, string esperado) =>
        Assert.Equal(esperado, NumeroDocumento.Formatar(codigo, numero));

    [Fact]
    public void DoisPdvsComCodigosDiferentesNuncaGeramONumeroIgual()
    {
        var numerosPdv1 = Enumerable.Range(1, 500).Select(n => NumeroDocumento.Formatar("01", n));
        var numerosPdv2 = Enumerable.Range(1, 500).Select(n => NumeroDocumento.Formatar("02", n));

        Assert.Empty(numerosPdv1.Intersect(numerosPdv2));
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("   ", true, null)]
    [InlineData("02", true, "02")]
    [InlineData(" loja1 ", true, "LOJA1")]
    [InlineData("1234567", false, null)]   // 7 caracteres
    [InlineData("A-1", false, null)]       // hífen é o separador
    [InlineData("A 1", false, null)]
    [InlineData("AÇÃO", false, null)]      // só ASCII
    public void ValidaOCodigoDoPdv(string? texto, bool valido, string? esperado)
    {
        Assert.Equal(valido, NumeroDocumento.TentarNormalizarCodigo(texto, out var codigo));
        Assert.Equal(esperado, codigo);
    }

    // ---- envio à API ----

    private static async Task<Guid> SemearVendaAsync(SqliteInMemoryFixture fixture, string? codigoPdv)
    {
        int caixaId, produtoId, formaId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 40m, IdExterno = 206, ProdutoIdApi = 77 };
            var forma = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
            context.AddRange(funcionario, produto, forma);
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
                ClienteConsumidorFinalIdExterno = 1,
                CodigoPdv = codigoPdv,
            });
            await context.SaveChangesAsync();

            var caixa = new Models.Caixa
            {
                FuncionarioId = funcionario.Id, IdExterno = 28, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1,
                DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0), TrocoInicial = 10m, AberturaSincronizada = true,
            };
            context.Caixas.Add(caixa);
            await context.SaveChangesAsync();
            (caixaId, produtoId, formaId) = (caixa.Id, produto.Id, forma.Id);
        }

        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixaId, null, new[] { (produtoId, 1m, 40m, 0m, 0m) }, new[] { (formaId, 40m) });
        return venda.Id;
    }

    private static async Task<string> NumeroDocumentoEnviadoAsync(SqliteInMemoryFixture fixture, Guid vendaId)
    {
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            corpo = r.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": { "id": 900 } }""", Encoding.UTF8, "application/json") };
        });

        var resultado = await new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarVendaAsync(vendaId, "t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var doc = JsonDocument.Parse(corpo!);
        return doc.RootElement.GetProperty("numero_documento").GetString()!;
    }

    [Fact]
    public async Task EnvioUsaOCodigoDoPdvComoPrefixo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaAsync(fixture, "02");

        Assert.Equal("02-000001", await NumeroDocumentoEnviadoAsync(fixture, vendaId));
    }

    [Fact]
    public async Task SemCodigoConfiguradoOEnvioContinuaComONumeroPuro()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaAsync(fixture, codigoPdv: null);

        Assert.Equal("1", await NumeroDocumentoEnviadoAsync(fixture, vendaId));
    }

    [Fact]
    public async Task OSequencialLocalMostradoNaTelaNaoMudaComOPrefixo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaAsync(fixture, "02");

        using var leitura = fixture.CriarContexto();
        Assert.Equal(1, (await leitura.Vendas.SingleAsync(v => v.Id == vendaId)).NumeroPedido);
    }

    // ---- tela de Configurações ----

    private static ConfiguracoesViewModel CriarViewModel(SqliteInMemoryFixture fixture)
    {
        var segredoProtector = new SegredoProtector();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var configuracaoService = new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, segredoProtector), segredoProtector);
        return new ConfiguracoesViewModel(configuracaoService);
    }

    [Fact]
    public async Task SalvarGravaOCodigoEmMaiusculoEMostraComoFicou()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();
        viewModel.CodigoPdv = " loja1 ";

        await viewModel.SalvarCommand.Execute();

        Assert.Equal("LOJA1", viewModel.CodigoPdv);
        using var leitura = fixture.CriarContexto();
        Assert.Equal("LOJA1", leitura.ConfiguracoesSincronizacao.Single().CodigoPdv);
    }

    [Fact]
    public async Task CodigoInvalidoNaoSalvaNadaEExplicaOMotivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();
        viewModel.CodigoPdv = "A-1";
        viewModel.ExigirAberturaCaixa = false;

        await viewModel.SalvarCommand.Execute();

        Assert.Contains("código do PDV", viewModel.Mensagem);
        using var leitura = fixture.CriarContexto();
        var salvo = leitura.ConfiguracoesSincronizacao.Single();
        Assert.Null(salvo.CodigoPdv);
        Assert.True(salvo.ExigirAberturaCaixa);   // nem o resto foi salvo pela metade
    }

    [Fact]
    public async Task IniciarCarregaOCodigoSalvo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var primeira = CriarViewModel(fixture);
        await primeira.IniciarAsync();
        primeira.CodigoPdv = "07";
        await primeira.SalvarCommand.Execute();

        var reaberta = CriarViewModel(fixture);
        await reaberta.IniciarAsync();

        Assert.Equal("07", reaberta.CodigoPdv);
    }
}

using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Apuração por bandeira de cartão no fechamento do caixa: uma linha para cada bandeira que aparece nas vendas do caixa
// (esperado = soma dos pagamentos daquela bandeira), o operador informa o que a maquininha registrou e isso vai em
// digitacao_bandeiras no envio do fechamento. Vendas descartadas e pagamentos sem bandeira não entram.
[SupportedOSPlatform("windows")]
public class ApuracaoPorBandeiraTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private sealed record Base(int CaixaId, int ProdutoId, int CreditoId, int PixId, int OperadorId);

    private static async Task<Base> SemearAsync(SqliteInMemoryFixture fixture)
    {
        int operadorId;
        FormaPagamento credito, pix;
        Produto produto;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            produto = new Produto { Nome = "Refri", PrecoVenda = 10m, IdExterno = 10, ProdutoIdApi = 100 };
            credito = new FormaPagamento { Nome = "CARTÃO DE CRÉDITO", Tipo = "CARTAO", CodigoNfce = "03", IdExterno = 11 };
            pix = new FormaPagamento { Nome = "PIX", Tipo = "ESPECIE", CodigoNfce = "20", IdExterno = 28 };
            context.AddRange(operador, produto, credito, pix);
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;
        await using (var context = fixture.CriarContexto())
        {
            (await context.Caixas.SingleAsync()).AberturaSincronizada = true;
            await context.SaveChangesAsync();
        }
        return new Base(caixa.Id, produto.Id, credito.Id, pix.Id, operadorId);
    }

    private static Task<Venda> VenderAsync(SqliteInMemoryFixture fixture, Base b, params (int FormaId, decimal Valor, string? Bandeira)[] pagamentos) =>
        new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            b.CaixaId, null, new[] { (b.ProdutoId, 1m, pagamentos.Sum(p => p.Valor), 0m, 0m) }, pagamentos);

    private static async Task DefinirStatusAsync(SqliteInMemoryFixture fixture, Guid vendaId, SyncStatus status)
    {
        await using var context = fixture.CriarContexto();
        (await context.Vendas.SingleAsync(v => v.Id == vendaId)).SyncStatus = status;
        await context.SaveChangesAsync();
    }

    // ---- o esperado por bandeira ----

    [Fact]
    public async Task SomaOsPagamentosDeCadaBandeiraIgnorandoSemBandeiraEVendaDescartada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        await VenderAsync(fixture, b, (b.CreditoId, 6m, "VISA"), (b.PixId, 4m, null));   // 6 VISA + 4 PIX
        await VenderAsync(fixture, b, (b.CreditoId, 10m, "MASTERCARD"));
        await VenderAsync(fixture, b, (b.CreditoId, 2.5m, "VISA"));                        // outra venda VISA: soma
        var descartada = await VenderAsync(fixture, b, (b.CreditoId, 99m, "VISA"));
        await DefinirStatusAsync(fixture, descartada.Id, SyncStatus.Descartada);          // cancelada: não entra
        await VenderAsync(fixture, b, (b.CreditoId, 7m, null));                            // cartão sem bandeira (sem cartões sincronizados)

        var totais = await new VendaLocalService(fixture.CriarContexto).TotaisPorBandeiraAsync(b.CaixaId);

        Assert.Equal(new[] { ("MASTERCARD", 10m), ("VISA", 8.5m) }, totais.Select(t => (t.Bandeira, t.Total)));
    }

    [Fact]
    public async Task CaixaSemVendaEmCartaoComBandeiraNaoTemTotais()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        await VenderAsync(fixture, b, (b.PixId, 10m, null));

        Assert.Empty(await new VendaLocalService(fixture.CriarContexto).TotaisPorBandeiraAsync(b.CaixaId));
    }

    [Fact]
    public async Task NaoMisturaBandeirasDeOutroCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        await VenderAsync(fixture, b, (b.CreditoId, 10m, "VISA"));
        var outro = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(b.OperadorId, new DateOnly(2026, 9, 22), 1, 10m)).Valor!;

        Assert.Empty(await new VendaLocalService(fixture.CriarContexto).TotaisPorBandeiraAsync(outro.Id));
    }

    // ---- a tela de fechar caixa ----

    private static async Task<FecharCaixaViewModel> AbrirFechamentoAsync(SqliteInMemoryFixture fixture, Base b)
    {
        var viewModel = new FecharCaixaViewModel(new CaixaService(fixture.CriarContexto), new VendaLocalService(fixture.CriarContexto), b.CaixaId);
        await viewModel.IniciarAsync();
        return viewModel;
    }

    private static async Task<Base> CenarioComDuasBandeirasEVendasEnviadasAsync(SqliteInMemoryFixture fixture)
    {
        var b = await SemearAsync(fixture);
        var v1 = await VenderAsync(fixture, b, (b.CreditoId, 6m, "VISA"), (b.PixId, 4m, null));
        var v2 = await VenderAsync(fixture, b, (b.CreditoId, 10m, "MASTERCARD"));
        await DefinirStatusAsync(fixture, v1.Id, SyncStatus.Sincronizado);
        await DefinirStatusAsync(fixture, v2.Id, SyncStatus.Sincronizado);
        return b;
    }

    [Fact]
    public async Task TelaMostraUmaLinhaPorBandeiraPreenchidaComOEsperado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await CenarioComDuasBandeirasEVendasEnviadasAsync(fixture);

        var viewModel = await AbrirFechamentoAsync(fixture, b);

        Assert.True(viewModel.TemBandeiras);
        Assert.Equal(new[] { "MASTERCARD", "VISA" }, viewModel.Bandeiras.Select(l => l.Nome));
        Assert.Equal(new[] { 10m, 6m }, viewModel.Bandeiras.Select(l => l.Esperado));
        Assert.Equal(new[] { "10,00", "6,00" }, viewModel.Bandeiras.Select(l => l.Contado));   // pré-preenchido
    }

    [Fact]
    public async Task SemVendaEmCartaoComBandeiraASecaoFicaOculta()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        var v = await VenderAsync(fixture, b, (b.PixId, 10m, null));
        await DefinirStatusAsync(fixture, v.Id, SyncStatus.Sincronizado);

        var viewModel = await AbrirFechamentoAsync(fixture, b);

        Assert.False(viewModel.TemBandeiras);
        Assert.Empty(viewModel.Bandeiras);
    }

    [Fact]
    public async Task ValorInvalidoNumaBandeiraTravaOBotaoECorrigirLibera()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await CenarioComDuasBandeirasEVendasEnviadasAsync(fixture);
        var viewModel = await AbrirFechamentoAsync(fixture, b);
        var pode = false;
        using var inscricao = viewModel.ConfirmarCommand.CanExecute.Subscribe(v => pode = v);
        Assert.True(pode);

        viewModel.Bandeiras.First().Contado = "abc";
        Assert.False(pode);

        viewModel.Bandeiras.First().Contado = "9,50";
        Assert.True(pode);
    }

    [Fact]
    public async Task ConfirmarGravaAApuracaoPorBandeiraComOQueOOperadorInformou()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await CenarioComDuasBandeirasEVendasEnviadasAsync(fixture);
        var viewModel = await AbrirFechamentoAsync(fixture, b);
        viewModel.Bandeiras.Single(l => l.Nome == "VISA").Contado = "5,50";   // a maquininha registrou menos que o esperado

        var fechado = await viewModel.ConfirmarCommand.Execute();

        Assert.NotNull(fechado);
        using var leitura = fixture.CriarContexto();
        var digitacoes = leitura.DigitacoesBandeiraCaixa.ToList().OrderBy(d => d.Bandeira).ToList();
        Assert.Equal(new[] { ("MASTERCARD", 10m), ("VISA", 5.5m) }, digitacoes.Select(d => (d.Bandeira, d.Valor)));
    }

    [Fact]
    public async Task CaixaSemBandeirasContinuaFechandoComApuracaoVazia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        var v = await VenderAsync(fixture, b, (b.PixId, 10m, null));
        await DefinirStatusAsync(fixture, v.Id, SyncStatus.Sincronizado);
        var viewModel = await AbrirFechamentoAsync(fixture, b);

        var fechado = await viewModel.ConfirmarCommand.Execute();

        Assert.NotNull(fechado);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.DigitacoesBandeiraCaixa);
    }

    // ---- o envio à API ----

    [Fact]
    public async Task OFechamentoEnviadoLevaDigitacaoBandeirasComNomeEValor()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await CenarioComDuasBandeirasEVendasEnviadasAsync(fixture);
        var viewModel = await AbrirFechamentoAsync(fixture, b);
        viewModel.Bandeiras.Single(l => l.Nome == "VISA").Contado = "5,50";
        await viewModel.ConfirmarCommand.Execute();
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(requisicao =>
        {
            corpo = requisicao.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        });

        var resultado = await new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarFechamentoAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var documento = JsonDocument.Parse(corpo!);
        var bandeiras = documento.RootElement.GetProperty("digitacao_bandeiras").EnumerateArray()
            .Select(e => (e.GetProperty("bandeira").GetString()!, e.GetProperty("valor").GetDecimal()))
            .OrderBy(x => x.Item1)
            .ToList();
        Assert.Equal(new[] { ("MASTERCARD", 10m), ("VISA", 5.5m) }, bandeiras);
    }
}

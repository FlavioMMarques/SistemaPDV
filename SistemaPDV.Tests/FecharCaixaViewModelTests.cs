using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Fechar caixa (Task 51): o operador confere quanto apurou por forma de pagamento e informa o troco final.
// O que o app espera receber por forma vem das vendas do caixa (pré-preenche a apuração, que ele ajusta).
public class FecharCaixaViewModelTests
{
    private static async Task<(int CaixaId, int EspecieId, int PixId)> SemearCaixaComVendasAsync(SqliteInMemoryFixture fixture)
    {
        int funcionarioId, especieId, pixId, produtoId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos" };
            var especie = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", IdExterno = 5 };
            var pix = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 6 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 10m };
            context.AddRange(funcionario, especie, pix, produto);
            await context.SaveChangesAsync();
            (funcionarioId, especieId, pixId, produtoId) = (funcionario.Id, especie.Id, pix.Id, produto.Id);
        }

        var caixaService = new CaixaService(fixture.CriarContexto);
        var caixa = (await caixaService.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 20), 1, 10m)).Valor!;

        var vendaService = new VendaService(fixture.CriarContexto);
        await vendaService.RegistrarVendaLocalAsync(caixa.Id, null, new[] { (produtoId, 1m, 30m, 0m, 0m) }, new[] { (especieId, 30m) });
        await vendaService.RegistrarVendaLocalAsync(caixa.Id, null, new[] { (produtoId, 1m, 20m, 0m, 0m) }, new[] { (especieId, 5m), (pixId, 15m) });

        // Só fecha sem venda pendente (regra de negócio): aqui as vendas já foram enviadas.
        await using (var context = fixture.CriarContexto())
        {
            foreach (var venda in context.Vendas)
                venda.SyncStatus = SyncStatus.Sincronizado;
            await context.SaveChangesAsync();
        }
        return (caixa.Id, especieId, pixId);
    }

    private static FecharCaixaViewModel CriarViewModel(SqliteInMemoryFixture fixture, int caixaId) =>
        new(new CaixaService(fixture.CriarContexto), new VendaLocalService(fixture.CriarContexto), caixaId);

    // ---- totais esperados ----

    [Fact]
    public async Task TotaisPorFormaSomamOsPagamentosDasVendasDoCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, especieId, pixId) = await SemearCaixaComVendasAsync(fixture);

        var totais = await new VendaLocalService(fixture.CriarContexto).TotaisPorFormaPagamentoAsync(caixaId);

        Assert.Equal(2, totais.Count);
        Assert.Equal(35m, totais.Single(t => t.FormaPagamentoId == especieId).Total);
        Assert.Equal(15m, totais.Single(t => t.FormaPagamentoId == pixId).Total);
    }

    [Fact]
    public async Task TotaisIgnoramVendasDeOutroCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCaixaComVendasAsync(fixture);

        var totais = await new VendaLocalService(fixture.CriarContexto).TotaisPorFormaPagamentoAsync(caixaId + 999);

        Assert.Empty(totais);
    }

    // ---- ViewModel ----

    [Fact]
    public async Task IniciarListaAsFormasUsadasComOEsperadoJaPreenchidoNaApuracao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCaixaComVendasAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);

        await viewModel.IniciarAsync();

        Assert.Equal(2, viewModel.Formas.Count);
        var especie = viewModel.Formas.Single(f => f.Nome == "ESPÉCIE");
        Assert.Equal(35m, especie.Esperado);
        Assert.Equal("35,00", especie.Contado);   // como o operador brasileiro lê
        Assert.Equal(50m, viewModel.TotalVendido);
    }

    [Fact]
    public async Task ConfirmarFechaOCaixaGravandoATrocoFinalEAApuracaoDigitada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, especieId, pixId) = await SemearCaixaComVendasAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        viewModel.Formas.Single(f => f.Nome == "ESPÉCIE").Contado = "34.50";   // faltou 0,50 na gaveta
        viewModel.TrocoFinal = "12.00";

        var fechado = await viewModel.ConfirmarCommand.Execute();

        Assert.NotNull(fechado);
        using var leitura = fixture.CriarContexto();
        var caixa = await leitura.Caixas.Include(c => c.Digitacoes).SingleAsync();
        Assert.Equal(StatusCaixa.Fechado, caixa.Status);
        Assert.Equal(12m, caixa.TrocoFinal);
        Assert.Equal(34.50m, caixa.Digitacoes.Single(d => d.FormaPagamentoId == especieId).Valor);
        Assert.Equal(15m, caixa.Digitacoes.Single(d => d.FormaPagamentoId == pixId).Valor);
        Assert.Equal(SyncStatus.PendenteSync, caixa.SyncStatus);   // fica na fila pro fechamento ir à API
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("")]
    public async Task ApuracaoInvalidaDesabilitaOFechamento(string valor)
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCaixaComVendasAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        var pode = true;
        using var inscricao = viewModel.ConfirmarCommand.CanExecute.Subscribe(v => pode = v);

        viewModel.Formas[0].Contado = valor;

        Assert.False(pode);
    }

    [Fact]
    public async Task TrocoFinalInvalidoDesabilitaOFechamento()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCaixaComVendasAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        var pode = true;
        using var inscricao = viewModel.ConfirmarCommand.CanExecute.Subscribe(v => pode = v);

        viewModel.TrocoFinal = "dez";

        Assert.False(pode);
    }

    [Fact]
    public async Task CaixaSemVendasPodeFecharComApuracaoVazia()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Funcionarios.Add(new Funcionario { Nome = "Carlos" });
            await context.SaveChangesAsync();
        }
        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(1, new DateOnly(2026, 9, 20), 1, 10m)).Valor!;
        var viewModel = CriarViewModel(fixture, caixa.Id);
        await viewModel.IniciarAsync();

        var fechado = await viewModel.ConfirmarCommand.Execute();

        Assert.NotNull(fechado);
        Assert.Empty(viewModel.Formas);
    }

    [Fact]
    public async Task FecharUmCaixaJaFechadoMostraAMensagemEDevolveNulo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCaixaComVendasAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        await viewModel.ConfirmarCommand.Execute();

        var segunda = await viewModel.ConfirmarCommand.Execute();

        Assert.Null(segunda);
        Assert.Contains("fechado", viewModel.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelarEmiteSemFecharNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCaixaComVendasAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        var cancelou = viewModel.CancelarCommand.FirstAsync().ToTask();

        await viewModel.CancelarCommand.Execute();
        await cancelou;

        using var leitura = fixture.CriarContexto();
        Assert.Equal(StatusCaixa.Aberto, leitura.Caixas.Single().Status);
    }
}

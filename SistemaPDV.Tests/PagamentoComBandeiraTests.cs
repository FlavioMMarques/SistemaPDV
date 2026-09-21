using System.Reactive.Linq;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Pagamento em cartão: o operador escolhe a bandeira (das que vieram dos cartões do SoftcomShop) e ela fica gravada no
// pagamento da venda — é o que alimenta a apuração por bandeira no fechamento do caixa.
public class PagamentoComBandeiraTests
{
    private sealed record Base(int CaixaId, int ProdutoId, FormaPagamento Credito, FormaPagamento Pix);

    private static async Task<Base> SemearAsync(SqliteInMemoryFixture fixture, params string[] bandeiras)
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
            var idCartao = 1;
            foreach (var nome in bandeiras)
                context.Cartoes.Add(new Cartao { IdExterno = idCartao++, BandeiraNome = nome, BandeiraId = "0" + idCartao, Tipo = "CREDITO" });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;
        return new Base(caixa.Id, produto.Id, credito, pix);
    }

    private static async Task<PdvViewModel> AbrirTelaAsync(SqliteInMemoryFixture fixture, Base b)
    {
        var viewModel = new PdvViewModel(new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), b.CaixaId);
        await viewModel.IniciarAsync();
        viewModel.AdicionarItem(viewModel.ProdutosDisponiveis.Single());   // R$ 10,00 no carrinho
        return viewModel;
    }

    // ---- a lista de bandeiras ----

    [Fact]
    public async Task ListaAsBandeirasDistintasEmOrdemAlfabeticaSemVazias()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture, "VISA", "MASTERCARD", "VISA", "  ", "ELO");   // VISA repetida (crédito e débito)

        var bandeiras = await new CatalogoLocalService(fixture.CriarContexto).ListarBandeirasAsync();

        Assert.Equal(new[] { "ELO", "MASTERCARD", "VISA" }, bandeiras);
    }

    [Fact]
    public async Task SemCartoesSincronizadosAListaVemVazia()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);

        Assert.Empty(await new CatalogoLocalService(fixture.CriarContexto).ListarBandeirasAsync());
    }

    [Theory]
    [InlineData("CARTAO", true)]
    [InlineData("cartao", true)]
    [InlineData("CARTAO_CREDITO", true)]
    [InlineData("CARTEIRA_DIGITAL", false)]   // começa com "CART" mas NÃO é cartão
    [InlineData("ESPECIE", false)]
    [InlineData("DUPLICATA", false)]
    public void ReconheceAFormaQueExigeBandeira(string tipo, bool ehCartao) =>
        Assert.Equal(ehCartao, new FormaPagamento { Nome = "x", Tipo = tipo }.EhCartao);

    // ---- a tela de venda ----

    [Fact]
    public async Task CartaoSemEscolherABandeiraNaoAdicionaEPedeAEscolha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA", "MASTERCARD");
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.ValorPagamentoAdicionar = "10,00";

        viewModel.AdicionarPagamento(b.Credito);

        Assert.Empty(viewModel.Pagamentos);
        Assert.Contains("bandeira", viewModel.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CartaoComBandeiraEscolhidaAdicionaGuardaAEscolhaEVoltaAoZero()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA", "MASTERCARD");
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.ValorPagamentoAdicionar = "10,00";
        viewModel.BandeiraSelecionada = "MASTERCARD";

        viewModel.AdicionarPagamento(b.Credito);

        var pagamento = Assert.Single(viewModel.Pagamentos);
        Assert.Equal("MASTERCARD", pagamento.Bandeira);
        Assert.Equal("CARTÃO DE CRÉDITO • MASTERCARD", pagamento.Descricao);
        Assert.Null(viewModel.BandeiraSelecionada);   // vale para UM pagamento: o próximo cartão pode ser de outra bandeira
        Assert.Null(viewModel.Mensagem);
    }

    [Fact]
    public async Task FormaQueNaoEhCartaoIgnoraABandeiraSelecionada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA");
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.ValorPagamentoAdicionar = "10,00";
        viewModel.BandeiraSelecionada = "VISA";   // sobrou uma escolha; o PIX não é cartão

        viewModel.AdicionarPagamento(b.Pix);

        var pagamento = Assert.Single(viewModel.Pagamentos);
        Assert.Null(pagamento.Bandeira);
        Assert.Equal("PIX", pagamento.Descricao);
    }

    [Fact]
    public async Task SemCartoesSincronizadosOCartaoNaoTravaAVendaEFicaSemBandeira()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);   // nenhum cartão sincronizado ainda
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.ValorPagamentoAdicionar = "10,00";

        viewModel.AdicionarPagamento(b.Credito);

        Assert.False(viewModel.TemBandeiras);
        Assert.Null(Assert.Single(viewModel.Pagamentos).Bandeira);
    }

    [Fact]
    public async Task FinalizarGravaABandeiraNoPagamentoDaVenda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA", "MASTERCARD");
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.ValorPagamentoAdicionar = "6,00";
        viewModel.BandeiraSelecionada = "VISA";
        viewModel.AdicionarPagamento(b.Credito);
        viewModel.ValorPagamentoAdicionar = "4,00";
        viewModel.AdicionarPagamento(b.Pix);   // pagamento misto: cartão + PIX

        var venda = await viewModel.FinalizarVendaCommand.Execute();

        Assert.NotNull(venda);
        using var leitura = fixture.CriarContexto();
        var pagamentos = leitura.PagamentosVenda.ToList().OrderBy(p => p.Valor).ToList();
        Assert.Equal(new[] { "(nenhuma)", "VISA" }, pagamentos.Select(p => p.Bandeira ?? "(nenhuma)"));   // PIX 4,00 sem; cartão 6,00 VISA
    }

    [Fact]
    public async Task LimparAVendaZeraABandeiraEscolhida()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA");
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.BandeiraSelecionada = "VISA";

        await viewModel.CancelarCommand.Execute();

        Assert.Null(viewModel.BandeiraSelecionada);
    }

    // ---- o serviço de venda ----

    private static IReadOnlyList<(int, decimal, decimal, decimal, decimal)> UmItem(Base b) => new[] { (b.ProdutoId, 1m, 10m, 0m, 0m) };

    [Fact]
    public async Task ServicoGuardaABandeiraEAparaOsEspacos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA");

        await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            b.CaixaId, null, UmItem(b), new[] { (b.Credito.Id, 6m, (string?)"  VISA "), (b.Pix.Id, 4m, (string?)"   ") });

        using var leitura = fixture.CriarContexto();
        var pagamentos = leitura.PagamentosVenda.ToList().OrderBy(p => p.Valor).ToList();
        Assert.Null(pagamentos[0].Bandeira);          // "   " vira nulo
        Assert.Equal("VISA", pagamentos[1].Bandeira); // e o valor útil vem sem espaços
    }

    [Fact]
    public async Task ChamadaAntigaSemBandeiraContinuaFuncionandoEGravaNulo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);

        await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            b.CaixaId, null, UmItem(b), new[] { (b.Pix.Id, 10m) });

        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.PagamentosVenda.Single().Bandeira);
    }

    // ---- cartões que chegam com a tela de venda já aberta ----

    [Fact]
    public async Task CartoesQueChegamComATelaAbertaViramEscolhaSemMexerNoCarrinho()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);                       // ainda sem cartões
        var viewModel = await AbrirTelaAsync(fixture, b);         // carrinho com 1 item
        viewModel.ValorPagamentoAdicionar = "3,00";
        viewModel.AdicionarPagamento(b.Pix);                       // e 1 pagamento já lançado
        viewModel.ValorPagamentoAdicionar = "2,50";                // e um valor já digitado para o próximo
        Assert.False(viewModel.TemBandeiras);
        await using (var context = fixture.CriarContexto())        // a sincronização traz um cartão agora
        {
            context.Cartoes.Add(new Cartao { IdExterno = 15, BandeiraNome = "MASTERCARD", BandeiraId = "02", Tipo = "CREDITO" });
            await context.SaveChangesAsync();
        }

        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.True(viewModel.TemBandeiras);
        Assert.Equal(new[] { "MASTERCARD" }, viewModel.BandeirasDisponiveis);
        Assert.Single(viewModel.Itens);           // o que o operador montou continua lá
        Assert.Single(viewModel.Pagamentos);
        Assert.Equal("2,50", viewModel.ValorPagamentoAdicionar);   // a atualização não mexe no que o operador digitou
    }

    [Fact]
    public async Task BandeiraEscolhidaQueSumiuNaAtualizacaoNaoFicaComoEscolhaFantasma()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA", "ELO");
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.BandeiraSelecionada = "ELO";
        await using (var context = fixture.CriarContexto())
        {
            context.Cartoes.RemoveRange(context.Cartoes.Where(c => c.BandeiraNome == "ELO"));
            await context.SaveChangesAsync();
        }

        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.Null(viewModel.BandeiraSelecionada);
        Assert.Equal(new[] { "VISA" }, viewModel.BandeirasDisponiveis);
    }

    [Fact]
    public async Task BandeiraEscolhidaQueAindaExisteContinuaEscolhida()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture, "VISA", "ELO");
        var viewModel = await AbrirTelaAsync(fixture, b);
        viewModel.BandeiraSelecionada = "VISA";

        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.Equal("VISA", viewModel.BandeiraSelecionada);
    }
}

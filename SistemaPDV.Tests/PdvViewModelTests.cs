using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

public class PdvViewModelTests
{
    private static async Task<(int CaixaId, Produto Produto, FormaPagamento Forma)> SemearCenarioAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();

        var funcionario = new Funcionario { Nome = "Carlos Silva" };
        var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m, IdExterno = 10 };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
        context.AddRange(funcionario, produto, forma);
        await context.SaveChangesAsync();

        var caixa = new Caixa
        {
            FuncionarioId = funcionario.Id,
            DataCaixa = DateOnly.FromDateTime(DateTime.Now),
            Turno = 1,
            DataAbertura = DateTime.Now,
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        return (caixa.Id, produto, forma);
    }

    private static PdvViewModel CriarViewModel(SqliteInMemoryFixture fixture, int caixaId) =>
        new(new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), caixaId);

    [Fact]
    public async Task PodeFinalizarVendaComecaFalsoComCarrinhoVazio()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);

        Assert.False(viewModel.PodeFinalizarVenda);
    }

    [Fact]
    public async Task AdicionarItemCalculaTotalMasNaoBastaSemPagamentoSuficiente()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, _) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);

        viewModel.QuantidadeAdicionar = "2";
        viewModel.AdicionarItem(produto);

        Assert.Equal(19.80m, viewModel.Total);
        // Carrinho não vazio já não basta mais — falta pagamento cobrindo o total
        // (achado numa revisão de código, 2026-09-18): venda com pagamento parcial
        // ou zero não pode finalizar.
        Assert.False(viewModel.PodeFinalizarVenda);
    }

    [Fact]
    public async Task PagamentoCobrindoOTotalHabilitaFinalizar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.QuantidadeAdicionar = "2";
        viewModel.AdicionarItem(produto);
        Assert.False(viewModel.PodeFinalizarVenda);

        viewModel.ValorPagamentoAdicionar = "19.80";
        viewModel.AdicionarPagamento(forma);

        Assert.True(viewModel.PodeFinalizarVenda);
    }

    [Fact]
    public async Task PagamentoAMenosDoQueOTotalNaoHabilitaFinalizar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.QuantidadeAdicionar = "2";
        viewModel.AdicionarItem(produto);

        viewModel.ValorPagamentoAdicionar = "10.00";
        viewModel.AdicionarPagamento(forma);

        Assert.False(viewModel.PodeFinalizarVenda);
    }

    [Fact]
    public async Task PagamentoAMaisDoQueOTotalHabilitaFinalizar()
    {
        // Cobre "dinheiro com troco": pagar mais do que o total é válido, o troco
        // não é modelado aqui (fora de escopo por enquanto).
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.QuantidadeAdicionar = "1";
        viewModel.AdicionarItem(produto);

        viewModel.ValorPagamentoAdicionar = "50.00";
        viewModel.AdicionarPagamento(forma);

        Assert.True(viewModel.PodeFinalizarVenda);
    }

    [Fact]
    public async Task RemoverUltimoItemDesabilitaFinalizarDeNovo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, _) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.QuantidadeAdicionar = "1";
        viewModel.AdicionarItem(produto);

        viewModel.RemoverItem(viewModel.Itens[0]);

        Assert.False(viewModel.PodeFinalizarVenda);
        Assert.Equal(0m, viewModel.Total);
    }

    [Fact]
    public async Task FinalizarVendaSemClienteGravaVendaAvulsaELimpaCarrinho()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.QuantidadeAdicionar = "1";
        viewModel.AdicionarItem(produto);
        viewModel.ValorPagamentoAdicionar = "9.90";
        viewModel.AdicionarPagamento(forma);

        var venda = await viewModel.FinalizarVendaCommand.Execute();

        Assert.NotNull(venda);
        Assert.Null(venda!.ClienteId);
        Assert.Single(venda.Itens);
        Assert.Single(venda.Pagamentos);
        Assert.Empty(viewModel.Itens);
        Assert.Empty(viewModel.Pagamentos);
        Assert.False(viewModel.PodeFinalizarVenda);
    }

    [Fact]
    public async Task TemVendaEmAndamentoAcompanhaCarrinhoEPagamentos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        Assert.False(viewModel.TemVendaEmAndamento);

        viewModel.AdicionarItem(produto);
        Assert.True(viewModel.TemVendaEmAndamento);

        await viewModel.NovoCommand.Execute();
        Assert.False(viewModel.TemVendaEmAndamento);

        // Só pagamento, sem item, também conta como venda em andamento — descartar
        // isso ao navegar seria perder o que o operador já digitou.
        viewModel.ValorPagamentoAdicionar = "5.00";
        viewModel.AdicionarPagamento(forma);
        Assert.True(viewModel.TemVendaEmAndamento);
    }

    [Fact]
    public async Task AdicionarPagamentoComValorInvalidoNaoAdicionaNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.ValorPagamentoAdicionar = "";

        viewModel.AdicionarPagamento(forma);

        Assert.Empty(viewModel.Pagamentos);
        Assert.False(string.IsNullOrEmpty(viewModel.Mensagem));
    }

    [Fact]
    public async Task AdicionarPagamentoComValorZeroOuNegativoNaoAdicionaNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.ValorPagamentoAdicionar = "0";

        viewModel.AdicionarPagamento(forma);

        Assert.Empty(viewModel.Pagamentos);
    }

    [Fact]
    public async Task ClienteSemIdExternoPodeSerSelecionadoESalvoNaVenda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, forma) = await SemearCenarioAsync(fixture);
        int clienteId;
        await using (var context = fixture.CriarContexto())
        {
            var cliente = new Cliente { Nome = "Recem-criado", IdExterno = null };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            clienteId = cliente.Id;
        }
        var viewModel = CriarViewModel(fixture, caixaId);
        var clientes = await new CatalogoLocalService(fixture.CriarContexto).ListarClientesAsync();
        viewModel.ClienteSelecionado = clientes.Single(c => c.Id == clienteId);
        viewModel.QuantidadeAdicionar = "1";
        viewModel.AdicionarItem(produto);
        viewModel.ValorPagamentoAdicionar = "9.90";
        viewModel.AdicionarPagamento(forma);

        var venda = await viewModel.FinalizarVendaCommand.Execute();

        Assert.Equal(clienteId, venda!.ClienteId);
    }

    [Fact]
    public async Task FiltroProdutoRestringeProdutosFiltrados()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, _) = await SemearCenarioAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            context.Produtos.Add(new Produto { Nome = "Água Mineral", IdExterno = 20, Vender = true });
            await context.SaveChangesAsync();
        }
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();

        viewModel.FiltroProduto = "refri";

        Assert.Single(viewModel.ProdutosFiltrados);
        Assert.Equal(produto.Nome, viewModel.ProdutosFiltrados[0].Nome);
    }

    [Fact]
    public async Task NovoCommandLimpaCarrinhoEPagamentosSemSalvarNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produto, forma) = await SemearCenarioAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        viewModel.QuantidadeAdicionar = "1";
        viewModel.AdicionarItem(produto);
        viewModel.ValorPagamentoAdicionar = "9.90";
        viewModel.AdicionarPagamento(forma);

        await viewModel.NovoCommand.Execute();

        Assert.Empty(viewModel.Itens);
        Assert.Empty(viewModel.Pagamentos);
        Assert.False(viewModel.PodeFinalizarVenda);
    }
}

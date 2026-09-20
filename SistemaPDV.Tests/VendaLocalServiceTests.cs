using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Tests;

public class VendaLocalServiceTests
{
    private static async Task<(int CaixaId, int ProdutoId, int FormaId)> SemearCenarioAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();

        // IdExterno nulo de propósito: essas duas entidades servem só pra satisfazer
        // as FKs de ItemVenda/PagamentoVenda nesses testes, e o helper pode ser
        // chamado mais de uma vez no mesmo teste (ex: dois caixas) — um IdExterno
        // fixo colidiria com o índice único.
        var funcionario = new Funcionario { Nome = "Carlos Silva" };
        var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
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

        return (caixa.Id, produto.Id, forma.Id);
    }

    [Fact]
    public async Task SemVendasDevolveListaVazia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, _, _) = await SemearCenarioAsync(fixture);
        var service = new VendaLocalService(fixture.CriarContexto);

        var vendas = await service.ListarVendasDoCaixaAsync(caixaId);

        Assert.Empty(vendas);
    }

    [Fact]
    public async Task VendaComClienteEPagamentoTrazDadosResolvidos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaId) = await SemearCenarioAsync(fixture);
        Guid vendaId;
        await using (var context = fixture.CriarContexto())
        {
            var cliente = new Cliente { Nome = "Maria Souza", IdExterno = 5 };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();

            var venda = new Venda { CaixaId = caixaId, ClienteId = cliente.Id, DataHora = DateTime.Now, SyncStatus = SyncStatus.Sincronizado };
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produtoId, Quantidade = 2, PrecoUnitario = 9.90m });
            venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = formaId, Valor = 19.80m });
            context.Vendas.Add(venda);
            await context.SaveChangesAsync();
            vendaId = venda.Id;
        }
        var service = new VendaLocalService(fixture.CriarContexto);

        var vendas = await service.ListarVendasDoCaixaAsync(caixaId);

        var resumo = Assert.Single(vendas);
        Assert.Equal(vendaId, resumo.Id);
        Assert.Equal("Maria Souza", resumo.ClienteNome);
        Assert.Equal("Carlos Silva", resumo.OperadorNome);
        Assert.Equal(19.80m, resumo.Total);
        Assert.Contains("PIX", resumo.FormasPagamento);
        Assert.Equal(SyncStatus.Sincronizado, resumo.SyncStatus);
    }

    [Fact]
    public async Task VendaSemClienteMostraConsumidorFinal()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaId) = await SemearCenarioAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            var venda = new Venda { CaixaId = caixaId, ClienteId = null, DataHora = DateTime.Now };
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produtoId, Quantidade = 1, PrecoUnitario = 9.90m });
            venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = formaId, Valor = 9.90m });
            context.Vendas.Add(venda);
            await context.SaveChangesAsync();
        }
        var service = new VendaLocalService(fixture.CriarContexto);

        var vendas = await service.ListarVendasDoCaixaAsync(caixaId);

        Assert.Equal("Consumidor Final", vendas[0].ClienteNome);
    }

    [Fact]
    public async Task NaoTrazVendaDeOutroCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaId) = await SemearCenarioAsync(fixture);
        var (outroCaixaId, _, _) = await SemearCenarioAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            var venda = new Venda { CaixaId = outroCaixaId, DataHora = DateTime.Now };
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produtoId, Quantidade = 1, PrecoUnitario = 9.90m });
            venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = formaId, Valor = 9.90m });
            context.Vendas.Add(venda);
            await context.SaveChangesAsync();
        }
        var service = new VendaLocalService(fixture.CriarContexto);

        var vendas = await service.ListarVendasDoCaixaAsync(caixaId);

        Assert.Empty(vendas);
    }
}

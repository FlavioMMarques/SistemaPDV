using Microsoft.EntityFrameworkCore;
using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

public class ListaPedidosViewModelTests
{
    private static async Task<int> SemearCaixaComVendaAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
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

        var venda = new Venda { CaixaId = caixa.Id, DataHora = DateTime.Now };
        venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 9.90m });
        venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = forma.Id, Valor = 9.90m });
        context.Vendas.Add(venda);
        await context.SaveChangesAsync();

        return caixa.Id;
    }

    [Fact]
    public async Task IniciarCarregaAsVendasDoCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture);
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), caixaId);

        await viewModel.IniciarAsync();

        Assert.Single(viewModel.Vendas);
    }

    [Fact]
    public async Task AtualizarCommandRecarregaAsVendas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture);
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), caixaId);
        await viewModel.IniciarAsync();
        Assert.Single(viewModel.Vendas);

        await using (var context = fixture.CriarContexto())
        {
            var produto = await context.Produtos.FirstAsync();
            var forma = await context.FormasPagamento.FirstAsync();
            var novaVenda = new Venda { CaixaId = caixaId, DataHora = DateTime.Now };
            novaVenda.Itens.Add(new ItemVenda { VendaId = novaVenda.Id, ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 9.90m });
            novaVenda.Pagamentos.Add(new PagamentoVenda { VendaId = novaVenda.Id, FormaPagamentoId = forma.Id, Valor = 9.90m });
            context.Vendas.Add(novaVenda);
            await context.SaveChangesAsync();
        }

        await viewModel.AtualizarCommand.Execute();

        Assert.Equal(2, viewModel.Vendas.Count);
    }

    [Fact]
    public async Task AtualizarAposSincronizacaoRecarregaOStatusSemApertarNada()
    {
        // O ciclo de sincronização muda o SyncStatus da venda no banco; a lista aberta
        // tem que refletir isso sem o operador clicar em Atualizar.
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture);
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), caixaId);
        await viewModel.IniciarAsync();
        Assert.Equal(SyncStatus.PendenteSync, viewModel.Vendas.Single().SyncStatus);

        await using (var context = fixture.CriarContexto())
        {
            var venda = await context.Vendas.SingleAsync();
            venda.SyncStatus = SyncStatus.Sincronizado;
            await context.SaveChangesAsync();
        }

        await ((IAtualizavelPorSincronizacao)viewModel).AtualizarAposSincronizacaoAsync();

        Assert.Equal(SyncStatus.Sincronizado, viewModel.Vendas.Single().SyncStatus);
    }
}

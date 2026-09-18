using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class VendaConfigurationTests
{
    [Fact]
    public void VendaComItensEPagamentosFazRoundTripCompleto()
    {
        using var fixture = new SqliteInMemoryFixture();
        Guid vendaId;

        using (var escrita = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos Silva", PdvKeyHash = new string('a', 64) };
            var produto1 = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m };
            var produto2 = new Produto { Nome = "Pão Francês (kg)", PrecoVenda = 14.50m };
            var formaPagamento = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
            escrita.AddRange(funcionario, produto1, produto2, formaPagamento);
            escrita.SaveChanges();

            var caixa = new Caixa
            {
                FuncionarioId = funcionario.Id,
                DataCaixa = new DateOnly(2026, 9, 18),
                Turno = 1,
                DataAbertura = DateTime.Now,
                TrocoInicial = 10m,
            };
            escrita.Caixas.Add(caixa);
            escrita.SaveChanges();

            var venda = new Venda
            {
                DataHora = DateTime.Now,
                CaixaId = caixa.Id,
            };
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto1.Id, Quantidade = 1, PrecoUnitario = 9.90m });
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto2.Id, Quantidade = 2, PrecoUnitario = 14.50m });
            venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = formaPagamento.Id, Valor = 38.90m });

            escrita.Vendas.Add(venda);
            escrita.SaveChanges();

            vendaId = venda.Id;
        }

        using var leitura = fixture.CriarContexto();
        var vendaLida = leitura.Vendas
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .Single(v => v.Id == vendaId);

        Assert.Equal(2, vendaLida.Itens.Count);
        Assert.Single(vendaLida.Pagamentos);
        Assert.Equal(38.90m, vendaLida.Pagamentos.Single().Valor);
        Assert.Equal(SyncStatus.PendenteSync, vendaLida.SyncStatus);
        Assert.Null(vendaLida.VendaIdExterno);
    }

    [Fact]
    public void ApagarVendaApagaItensEPagamentosJunto()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var context = fixture.CriarContexto();

        var funcionario = new Funcionario { Nome = "Carlos Silva", PdvKeyHash = new string('a', 64) };
        var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m };
        var formaPagamento = new FormaPagamento { Nome = "Dinheiro", Tipo = "ESPECIE" };
        context.AddRange(funcionario, produto, formaPagamento);
        context.SaveChanges();

        var caixa = new Caixa { FuncionarioId = funcionario.Id, DataCaixa = new DateOnly(2026, 9, 18), Turno = 1, DataAbertura = DateTime.Now, TrocoInicial = 10m };
        context.Caixas.Add(caixa);
        context.SaveChanges();

        var venda = new Venda { DataHora = DateTime.Now, CaixaId = caixa.Id };
        venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 9.90m });
        venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = formaPagamento.Id, Valor = 9.90m });
        context.Vendas.Add(venda);
        context.SaveChanges();

        context.Vendas.Remove(venda);
        context.SaveChanges();

        Assert.Empty(context.ItensVenda);
        Assert.Empty(context.PagamentosVenda);
    }
}

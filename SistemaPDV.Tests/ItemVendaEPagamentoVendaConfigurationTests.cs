using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class ItemVendaEPagamentoVendaConfigurationTests
{
    private static Venda NovaVendaPersistida(AppDbContext context)
    {
        var funcionario = new Funcionario { Nome = "Carlos Silva", PdvKeyHash = new string('a', 64) };
        context.Funcionarios.Add(funcionario);
        context.SaveChanges();

        var caixa = new Caixa
        {
            FuncionarioId = funcionario.Id,
            DataCaixa = new DateOnly(2026, 9, 18),
            Turno = 1,
            DataAbertura = DateTime.Now,
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        context.SaveChanges();

        var venda = new Venda { DataHora = DateTime.Now, CaixaId = caixa.Id };
        context.Vendas.Add(venda);
        context.SaveChanges();

        return venda;
    }

    [Fact]
    public void InsereELeDeVoltaUmItemVendaValido()
    {
        using var fixture = new SqliteInMemoryFixture();
        Guid itemVendaId;

        using (var escrita = fixture.CriarContexto())
        {
            var venda = NovaVendaPersistida(escrita);
            var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m };
            escrita.Produtos.Add(produto);
            escrita.SaveChanges();

            escrita.ItensVenda.Add(new ItemVenda
            {
                VendaId = venda.Id,
                ProdutoId = produto.Id,
                Quantidade = 2,
                PrecoUnitario = 9.90m,
            });
            escrita.SaveChanges();

            itemVendaId = venda.Id;
        }

        using var leitura = fixture.CriarContexto();
        var item = leitura.ItensVenda.Single(i => i.VendaId == itemVendaId);

        Assert.Equal(2, item.Quantidade);
        Assert.Equal(9.90m, item.PrecoUnitario);
    }

    [Fact]
    public void InsereELeDeVoltaUmPagamentoVendaValido()
    {
        using var fixture = new SqliteInMemoryFixture();
        Guid vendaId;

        using (var escrita = fixture.CriarContexto())
        {
            var venda = NovaVendaPersistida(escrita);
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
            escrita.FormasPagamento.Add(forma);
            escrita.SaveChanges();

            escrita.PagamentosVenda.Add(new PagamentoVenda
            {
                VendaId = venda.Id,
                FormaPagamentoId = forma.Id,
                Valor = 19.80m,
            });
            escrita.SaveChanges();

            vendaId = venda.Id;
        }

        using var leitura = fixture.CriarContexto();
        var pagamento = leitura.PagamentosVenda.Single(p => p.VendaId == vendaId);

        Assert.Equal(19.80m, pagamento.Valor);
    }
}

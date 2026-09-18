using SistemaPDV.Models;
using SistemaPDV.Services;

namespace SistemaPDV.Tests;

public class DashboardServiceTests
{
    private static async Task<int> SemearCaixaAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos Silva" };
        context.Funcionarios.Add(funcionario);
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
        return caixa.Id;
    }

    [Fact]
    public async Task ResumoComTudoVazioDevolveZerados()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        var service = new DashboardService(fixture.CriarContexto);

        var resumo = await service.ObterResumoAsync(caixaId);

        Assert.Equal(0m, resumo.FaturamentoHoje);
        Assert.Equal(0, resumo.EstoqueTotal);
        Assert.Null(resumo.UltimaSincronizacao);
    }

    [Fact]
    public async Task FaturamentoSomaSoVendasDoCaixaAberto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        var outroCaixaId = await SemearCaixaAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            var produto = new Produto { Nome = "Refrigerante", PrecoVenda = 10m, IdExterno = 1 };
            context.Produtos.Add(produto);
            await context.SaveChangesAsync();

            var venda = new Venda { CaixaId = caixaId, DataHora = DateTime.Now };
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto.Id, Quantidade = 2, PrecoUnitario = 10m });
            context.Vendas.Add(venda);

            var vendaOutroCaixa = new Venda { CaixaId = outroCaixaId, DataHora = DateTime.Now };
            vendaOutroCaixa.Itens.Add(new ItemVenda { VendaId = vendaOutroCaixa.Id, ProdutoId = produto.Id, Quantidade = 100, PrecoUnitario = 10m });
            context.Vendas.Add(vendaOutroCaixa);

            await context.SaveChangesAsync();
        }
        var service = new DashboardService(fixture.CriarContexto);

        var resumo = await service.ObterResumoAsync(caixaId);

        Assert.Equal(20m, resumo.FaturamentoHoje);
    }

    [Fact]
    public async Task PendentesOutboxContaCaixaVendaEClientePendentes()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.Add(new Cliente { Nome = "Recem-criado", IdExterno = null, SyncStatus = SyncStatus.PendenteSync });
            var venda = new Venda { CaixaId = caixaId, DataHora = DateTime.Now, SyncStatus = SyncStatus.PendenteSync };
            context.Vendas.Add(venda);
            await context.SaveChangesAsync();
        }
        var service = new DashboardService(fixture.CriarContexto);

        var resumo = await service.ObterResumoAsync(caixaId);

        // Caixa aberto (PendenteSync por padrao) + venda + cliente = 3
        Assert.Equal(3, resumo.PendentesOutbox);
    }
}

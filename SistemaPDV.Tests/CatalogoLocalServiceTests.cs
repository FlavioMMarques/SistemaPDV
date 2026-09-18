using SistemaPDV.Models;
using SistemaPDV.Services;

namespace SistemaPDV.Tests;

public class CatalogoLocalServiceTests
{
    [Fact]
    public async Task ListarProdutosDisponiveisSoTrazSincronizadosEVendaveis()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Produtos.Add(new Produto { Nome = "Refrigerante", IdExterno = 1, Vender = true });
            context.Produtos.Add(new Produto { Nome = "Não sincronizado", IdExterno = null, Vender = true });
            context.Produtos.Add(new Produto { Nome = "Não vendável", IdExterno = 2, Vender = false });
            await context.SaveChangesAsync();
        }
        var service = new CatalogoLocalService(fixture.CriarContexto);

        var produtos = await service.ListarProdutosDisponiveisAsync();

        Assert.Single(produtos);
        Assert.Equal("Refrigerante", produtos[0].Nome);
    }

    [Fact]
    public async Task ListarClientesTrazTodosMesmoSemIdExterno()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.Add(new Cliente { Nome = "Sincronizado", IdExterno = 1 });
            context.Clientes.Add(new Cliente { Nome = "Recem-criado", IdExterno = null });
            await context.SaveChangesAsync();
        }
        var service = new CatalogoLocalService(fixture.CriarContexto);

        var clientes = await service.ListarClientesAsync();

        Assert.Equal(2, clientes.Count);
    }

    [Fact]
    public async Task ListarFormasPagamentoDisponiveisSoTrazSincronizadas()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.FormasPagamento.Add(new FormaPagamento { Nome = "Pix", Tipo = "CARTEIRA_DIGITAL", IdExterno = 1 });
            context.FormasPagamento.Add(new FormaPagamento { Nome = "Não sincronizada", Tipo = "DINHEIRO", IdExterno = null });
            await context.SaveChangesAsync();
        }
        var service = new CatalogoLocalService(fixture.CriarContexto);

        var formas = await service.ListarFormasPagamentoDisponiveisAsync();

        Assert.Single(formas);
        Assert.Equal("Pix", formas[0].Nome);
    }
}

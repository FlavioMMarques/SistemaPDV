using SistemaPDV.Models;
using SistemaPDV.Services;

namespace SistemaPDV.Tests;

// O painel da fila mostra O QUE está pendente (caixas, vendas, clientes e produtos novos), não só um número.
public class PendenciasPorTipoTests
{
    private static async Task SemearAsync(SqliteInMemoryFixture fixture, int clientes = 0, int produtos = 0, int caixasPendentes = 0, int vendasPendentes = 0)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos" };
        context.Funcionarios.Add(funcionario);
        await context.SaveChangesAsync();

        var caixa = new Caixa
        {
            FuncionarioId = funcionario.Id, DataCaixa = new DateOnly(2026, 9, 21), Turno = 1, DataAbertura = DateTime.Now, TrocoInicial = 10m,
            SyncStatus = caixasPendentes > 0 ? SyncStatus.PendenteSync : SyncStatus.Sincronizado,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        for (var i = 0; i < clientes; i++)
            context.Clientes.Add(new Cliente { Nome = $"Cliente {i}", SyncStatus = SyncStatus.PendenteSync });
        for (var i = 0; i < produtos; i++)
            context.Produtos.Add(new Produto { Nome = $"Produto {i}", SyncStatus = SyncStatus.PendenteSync });
        // Já sincronizados: não contam.
        context.Clientes.Add(new Cliente { Nome = "Da API", IdExterno = 1, SyncStatus = SyncStatus.Sincronizado });
        context.Produtos.Add(new Produto { Nome = "Da API", IdExterno = 1, SyncStatus = SyncStatus.Sincronizado });
        await context.SaveChangesAsync();

        for (var i = 0; i < vendasPendentes; i++)
        {
            context.Vendas.Add(new Venda { CaixaId = caixa.Id, DataHora = DateTime.Now, NumeroPedido = i + 1, SyncStatus = SyncStatus.PendenteSync });
        }
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task SeparaAsPendenciasPorTipoEOTotalBate()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture, clientes: 2, produtos: 3, caixasPendentes: 1, vendasPendentes: 4);
        var service = new DashboardService(fixture.CriarContexto);

        var porTipo = await service.ContarPendenciasPorTipoAsync();

        Assert.Equal(1, porTipo.Caixas);
        Assert.Equal(4, porTipo.Vendas);
        Assert.Equal(2, porTipo.Clientes);
        Assert.Equal(3, porTipo.Produtos);
        Assert.Equal(10, porTipo.Total);
        Assert.Equal(porTipo.Total, await service.ContarPendentesAsync());   // o número da pílula é o mesmo, só sem detalhe
    }

    [Fact]
    public async Task SemPendenciasTudoZero()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);

        var porTipo = await new DashboardService(fixture.CriarContexto).ContarPendenciasPorTipoAsync();

        Assert.Equal(0, porTipo.Total);
        Assert.Equal(new PendenciasPorTipo(0, 0, 0, 0), porTipo);
    }
}

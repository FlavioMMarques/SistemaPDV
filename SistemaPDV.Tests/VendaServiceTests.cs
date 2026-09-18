using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Tests;

public class VendaServiceTests
{
    private static async Task<(int CaixaId, int ProdutoId, int FormaPagamentoId)> SemearBaseAsync(SqliteInMemoryFixture fixture)
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
            DataCaixa = new DateOnly(2026, 9, 18),
            Turno = 1,
            DataAbertura = DateTime.Now,
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        return (caixa.Id, produto.Id, forma.Id);
    }

    [Fact]
    public async Task RegistrarVendaLocalSalvaSemRede()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaPagamentoId) = await SemearBaseAsync(fixture);
        var service = new VendaService(fixture.CriarContexto);

        var venda = await service.RegistrarVendaLocalAsync(
            caixaId,
            clienteId: null,
            itens: new[] { (produtoId, 2m, 9.90m, 0m, 0m) },
            pagamentos: new[] { (formaPagamentoId, 19.80m) });

        Assert.NotEqual(Guid.Empty, venda.Id);
        Assert.Equal(SyncStatus.PendenteSync, venda.SyncStatus);

        using var leitura = fixture.CriarContexto();
        var vendaLida = await leitura.Vendas
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .SingleAsync(v => v.Id == venda.Id);

        Assert.Single(vendaLida.Itens);
        Assert.Single(vendaLida.Pagamentos);
        Assert.Null(vendaLida.ClienteId);
    }

    [Fact]
    public async Task SuportaPagamentoMisto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaPagamentoId) = await SemearBaseAsync(fixture);

        int outraFormaPagamentoId;
        await using (var context = fixture.CriarContexto())
        {
            var outraForma = new FormaPagamento { Nome = "Dinheiro", Tipo = "ESPECIE" };
            context.FormasPagamento.Add(outraForma);
            await context.SaveChangesAsync();
            outraFormaPagamentoId = outraForma.Id;
        }

        var service = new VendaService(fixture.CriarContexto);

        var venda = await service.RegistrarVendaLocalAsync(
            caixaId,
            clienteId: null,
            itens: new[] { (produtoId, 1m, 20m, 0m, 0m) },
            pagamentos: new[] { (formaPagamentoId, 10m), (outraFormaPagamentoId, 10m) });

        using var leitura = fixture.CriarContexto();
        var vendaLida = await leitura.Vendas.Include(v => v.Pagamentos).SingleAsync(v => v.Id == venda.Id);
        Assert.Equal(2, vendaLida.Pagamentos.Count);
    }

    [Fact]
    public async Task CadaVendaGanhaUmGuidDiferente()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaPagamentoId) = await SemearBaseAsync(fixture);
        var service = new VendaService(fixture.CriarContexto);

        var venda1 = await service.RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaPagamentoId, 9.90m) });
        var venda2 = await service.RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaPagamentoId, 9.90m) });

        Assert.NotEqual(venda1.Id, venda2.Id);
    }
}

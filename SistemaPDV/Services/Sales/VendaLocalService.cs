using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;

namespace SistemaPDV.Services.Sales;

// Leitura pura pra ListaPedidosViewModel — resolve cliente/operador/formas de
// pagamento num lote só (evita N+1, mesmo padrão já usado em CatalogSyncService),
// nunca dispara sincronização sozinho.
public class VendaLocalService
{
    private readonly Func<AppDbContext> contextFactory;

    public VendaLocalService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<VendaResumo>> ListarVendasDoCaixaAsync(int caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var caixa = await context.Caixas.FindAsync(new object[] { caixaId }, ct);
        var operadorNome = caixa is not null
            ? (await context.Funcionarios.FindAsync(new object[] { caixa.FuncionarioId }, ct))?.Nome ?? "—"
            : "—";

        var vendas = await context.Vendas
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .Where(v => v.CaixaId == caixaId)
            .OrderByDescending(v => v.DataHora)
            .ToListAsync(ct);

        var clienteIds = vendas.Where(v => v.ClienteId.HasValue).Select(v => v.ClienteId!.Value).Distinct().ToList();
        var clientes = await context.Clientes.Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var formaIds = vendas.SelectMany(v => v.Pagamentos.Select(p => p.FormaPagamentoId)).Distinct().ToList();
        var formas = await context.FormasPagamento.Where(f => formaIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        return vendas.Select(v => new VendaResumo(
            v.Id,
            v.VendaIdExterno,
            v.DataHora,
            v.ClienteId is { } clienteId && clientes.TryGetValue(clienteId, out var cliente) ? cliente.Nome : "Consumidor Final",
            operadorNome,
            v.Itens.Sum(i => i.Quantidade * i.PrecoUnitario - i.DescontoItem + i.AcrescimoItem) - v.Desconto,
            string.Join(", ", v.Pagamentos.Select(p => formas.TryGetValue(p.FormaPagamentoId, out var forma) ? forma.Nome : "?").Distinct()),
            v.SyncStatus))
            .ToList();
    }
}

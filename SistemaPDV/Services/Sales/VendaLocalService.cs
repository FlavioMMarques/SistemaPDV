using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

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

    // Quanto as vendas deste caixa somam em cada forma de pagamento — o "esperado" que a tela de fechar caixa mostra
    // pra o operador conferir contra o que apurou. Soma em memória (o SQLite não soma decimal no servidor).
    public async Task<IReadOnlyList<TotalFormaPagamento>> TotaisPorFormaPagamentoAsync(int caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var pagamentos = await context.Vendas
            .Where(v => v.CaixaId == caixaId)
            .SelectMany(v => v.Pagamentos)
            .Select(p => new { p.FormaPagamentoId, p.Valor })
            .ToListAsync(ct);

        var formaIds = pagamentos.Select(p => p.FormaPagamentoId).Distinct().ToList();
        var nomes = await context.FormasPagamento.Where(f => formaIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Nome, ct);

        return pagamentos
            .GroupBy(p => p.FormaPagamentoId)
            .Select(g => new TotalFormaPagamento(g.Key, nomes.GetValueOrDefault(g.Key, "?"), g.Sum(p => p.Valor)))
            .OrderBy(t => t.Nome)
            .ToList();
    }

    // "Reenviar falhas": devolve à fila as vendas do caixa que falharam ou desistiram e o
    // próprio caixa, se o envio dele falhou (zera a espera crescente e o contador — ver
    // PoliticaRetentativa). O próximo ciclo de sincronização as envia. Devolve quantos itens voltaram.
    public async Task<int> ReenviarFalhasAsync(int caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var vendas = await context.Vendas
            .Where(v => v.CaixaId == caixaId && v.SyncStatus == SyncStatus.FalhaSync)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.TentativasEnvio, 0)
                .SetProperty(v => v.ProximaTentativaEm, (DateTime?)null), ct);

        var caixas = await context.Caixas
            .Where(c => c.Id == caixaId && c.SyncStatus == SyncStatus.FalhaSync)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.TentativasEnvio, 0)
                .SetProperty(c => c.ProximaTentativaEm, (DateTime?)null), ct);

        return vendas + caixas;
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
            v.NumeroPedido,
            v.DataHora,
            v.ClienteId is { } clienteId && clientes.TryGetValue(clienteId, out var cliente) ? cliente.Nome : "Consumidor Final",
            operadorNome,
            v.Itens.Sum(i => i.Quantidade * i.PrecoUnitario - i.DescontoItem + i.AcrescimoItem) - v.Desconto,
            string.Join(", ", v.Pagamentos.Select(p => formas.TryGetValue(p.FormaPagamentoId, out var forma) ? forma.Nome : "?").Distinct()),
            v.SyncStatus,
            v.UltimoErroSync))
            .ToList();
    }
}

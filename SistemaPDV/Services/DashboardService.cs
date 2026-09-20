using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Services;

// Leitura pura pro DashboardViewModel — nunca dispara sincronização sozinho (isso
// é papel só do SincronizacaoBackgroundService, Task 50).
public class DashboardService
{
    private readonly Func<AppDbContext> contextFactory;

    public DashboardService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    // caixaId nullable: com ExigirAberturaCaixa=false, o Shell pode chegar aqui sem
    // nenhum caixa aberto — nesse caso não tem faturamento nenhum pra mostrar ainda,
    // não é um erro.
    public async Task<ResumoDashboard> ObterResumoAsync(int? caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var faturamentoHoje = 0m;
        if (caixaId is { } id)
        {
            var vendasDoCaixa = await context.Vendas
                .Include(v => v.Itens)
                .Where(v => v.CaixaId == id)
                .Where(VendaFiltros.Valida)   // a descartada é tratada como cancelada
                .ToListAsync(ct);
            faturamentoHoje = vendasDoCaixa.Sum(v =>
                v.Itens.Sum(i => i.Quantidade * i.PrecoUnitario - i.DescontoItem + i.AcrescimoItem) - v.Desconto);
        }

        var estoqueTotal = await context.Produtos.SumAsync(p => p.EstoqueAtual, ct);

        var caixasPendentes = await context.Caixas.CountAsync(c => c.SyncStatus != SyncStatus.Sincronizado, ct);
        var vendasPendentes = await context.Vendas.Where(VendaFiltros.NaoEnviada).CountAsync(ct);
        var clientesPendentes = await context.Clientes.CountAsync(c => c.IdExterno == null && c.SyncStatus != SyncStatus.Sincronizado, ct);
        var pendentesOutbox = caixasPendentes + vendasPendentes + clientesPendentes;

        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct);
        var candidatosUltimaSincronizacao = new[]
        {
            configuracao?.UltimaSincronizacaoProdutos,
            configuracao?.UltimaSincronizacaoClientes,
            configuracao?.UltimaSincronizacaoFormasPagamento,
            configuracao?.UltimaSincronizacaoEmpresa,
            configuracao?.UltimaSincronizacaoFuncionarios,
        }.Where(d => d.HasValue).Select(d => d!.Value).ToList();
        var ultimaSincronizacao = candidatosUltimaSincronizacao.Count > 0 ? candidatosUltimaSincronizacao.Max() : (DateTimeOffset?)null;

        return new ResumoDashboard(faturamentoHoje, estoqueTotal, pendentesOutbox, ultimaSincronizacao);
    }
}

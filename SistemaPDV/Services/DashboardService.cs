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

    // Quantos itens ainda não chegaram à API (caixas, vendas, clientes e produtos novos) — o "Sync: N pendentes" da barra do topo.
    // Só as contagens da fila: não calcula faturamento nem estoque como o resumo do painel.
    public async Task<int> ContarPendentesAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await ContarPendentesAsync(context, ct);
    }

    private static async Task<int> ContarPendentesAsync(AppDbContext context, CancellationToken ct)
    {
        var caixasPendentes = await context.Caixas.CountAsync(c => c.SyncStatus != SyncStatus.Sincronizado, ct);
        var vendasPendentes = await context.Vendas.Where(VendaFiltros.NaoEnviada).CountAsync(ct);
        var clientesPendentes = await context.Clientes.CountAsync(c => c.IdExterno == null && c.SyncStatus != SyncStatus.Sincronizado, ct);
        var produtosPendentes = await context.Produtos.CountAsync(p => p.IdExterno == null && p.SyncStatus != SyncStatus.Sincronizado, ct);
        return caixasPendentes + vendasPendentes + clientesPendentes + produtosPendentes;
    }

    // caixaId nullable: com ExigirAberturaCaixa=false, o Shell pode chegar aqui sem
    // nenhum caixa aberto — nesse caso não tem faturamento nenhum pra mostrar ainda,
    // não é um erro.
    public async Task<ResumoDashboard> ObterResumoAsync(int? caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var faturamentoHoje = 0m;
        var vendasEmitidas = 0;
        if (caixaId is { } id)
        {
            var vendasDoCaixa = await context.Vendas
                .Include(v => v.Itens)
                .Where(v => v.CaixaId == id)
                .Where(VendaFiltros.Valida)   // a descartada é tratada como cancelada
                .ToListAsync(ct);
            vendasEmitidas = vendasDoCaixa.Count;
            faturamentoHoje = vendasDoCaixa.Sum(v =>
                v.Itens.Sum(i => i.Quantidade * i.PrecoUnitario - i.DescontoItem + i.AcrescimoItem) - v.Desconto);
        }

        var estoqueTotal = await context.Produtos.SumAsync(p => p.EstoqueAtual, ct);
        var produtosCadastrados = await context.Produtos.CountAsync(ct);

        var pendentesOutbox = await ContarPendentesAsync(context, ct);

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

        return new ResumoDashboard(faturamentoHoje, estoqueTotal, pendentesOutbox, ultimaSincronizacao, vendasEmitidas, produtosCadastrados);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Sales;

// Puramente local — nunca toca rede. A sincronização com a API é inteira do
// VendaSyncService (outbox), chamado à parte, não daqui — mesmo desenho de
// CaixaService/CaixaSyncService.
public class VendaService
{
    private readonly Func<AppDbContext> contextFactory;

    public VendaService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<Venda> RegistrarVendaLocalAsync(
        int caixaId,
        int? clienteId,
        IReadOnlyList<(int ProdutoId, decimal Quantidade, decimal PrecoUnitario, decimal DescontoItem, decimal AcrescimoItem)> itens,
        IReadOnlyList<(int FormaPagamentoId, decimal Valor)> pagamentos,
        CancellationToken ct = default)
    {
        // Guid gerado aqui, na criação — nunca depois. É a chave de idempotência
        // enviada como "guid" pra API (ver VendaSyncService), então precisa nascer
        // junto com a venda, não ser atribuído só na hora de sincronizar.
        var venda = new Venda
        {
            DataHora = DateTime.Now,
            CaixaId = caixaId,
            ClienteId = clienteId,
            SyncStatus = SyncStatus.PendenteSync,
        };

        foreach (var (produtoId, quantidade, precoUnitario, descontoItem, acrescimoItem) in itens)
        {
            venda.Itens.Add(new ItemVenda
            {
                VendaId = venda.Id,
                ProdutoId = produtoId,
                Quantidade = quantidade,
                PrecoUnitario = precoUnitario,
                DescontoItem = descontoItem,
                AcrescimoItem = acrescimoItem,
            });
        }

        foreach (var (formaPagamentoId, valor) in pagamentos)
        {
            venda.Pagamentos.Add(new PagamentoVenda
            {
                VendaId = venda.Id,
                FormaPagamentoId = formaPagamentoId,
                Valor = valor,
            });
        }

        await using var context = contextFactory();

        // Número do pedido: sequencial e único neste dispositivo (índice único no banco). Lido do banco
        // — não de um contador em memória — pra continuar de onde parou depois de reabrir o app.
        var ultimoNumero = await context.Vendas.MaxAsync(v => (int?)v.NumeroPedido, ct) ?? 0;
        venda.NumeroPedido = ultimoNumero + 1;

        context.Vendas.Add(venda);
        await context.SaveChangesAsync(ct);

        return venda;
    }
}

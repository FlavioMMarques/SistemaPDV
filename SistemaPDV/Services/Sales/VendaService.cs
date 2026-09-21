using System;
using System.Collections.Generic;
using System.Linq;
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

    // Sem bandeira de cartão (forma que não é cartão, ou ainda sem cartões sincronizados) — o caso de sempre.
    public Task<Venda> RegistrarVendaLocalAsync(
        int caixaId,
        int? clienteId,
        IReadOnlyList<(int ProdutoId, decimal Quantidade, decimal PrecoUnitario, decimal DescontoItem, decimal AcrescimoItem)> itens,
        IReadOnlyList<(int FormaPagamentoId, decimal Valor)> pagamentos,
        CancellationToken ct = default) =>
        RegistrarVendaLocalAsync(caixaId, clienteId, itens, pagamentos.Select(p => (p.FormaPagamentoId, p.Valor, (string?)null)).ToList(), ct);

    // Com a bandeira escolhida em cada pagamento (BandeiraNome de um Cartao sincronizado; nula quando não se aplica).
    public async Task<Venda> RegistrarVendaLocalAsync(
        int caixaId,
        int? clienteId,
        IReadOnlyList<(int ProdutoId, decimal Quantidade, decimal PrecoUnitario, decimal DescontoItem, decimal AcrescimoItem)> itens,
        IReadOnlyList<(int FormaPagamentoId, decimal Valor, string? Bandeira)> pagamentos,
        CancellationToken ct = default)
    {
        // Rede de segurança do domínio: a tela já pede o preço de um produto sem preço (PdvViewModel.AdicionarItem), mas uma
        // venda com item de preço zero ou negativo nunca deve ser gravada — nem por outro caminho de código que esqueça isso.
        if (itens.Any(i => i.PrecoUnitario <= 0))
            throw new ArgumentException("Um item da venda está com preço zero ou negativo.", nameof(itens));

        // Número do pedido: sequencial e único neste dispositivo (índice único no banco). Lido do banco — não de um
        // contador em memória — pra continuar de onde parou depois de reabrir o app. Se outro processo do app usando o
        // MESMO banco pegou o mesmo número entre a leitura e a gravação, o índice único recusa: tenta de novo com o
        // próximo, em vez de perder a venda que o operador acabou de finalizar.
        for (var tentativa = 1; ; tentativa++)
        {
            var venda = Montar(caixaId, clienteId, itens, pagamentos);

            await using var context = contextFactory();
            var ultimoNumero = await context.Vendas.MaxAsync(v => (int?)v.NumeroPedido, ct) ?? 0;
            venda.NumeroPedido = ultimoNumero + 1;
            context.Vendas.Add(venda);

            try
            {
                await context.SaveChangesAsync(ct);
                return venda;
            }
            catch (DbUpdateException) when (tentativa < MaximoTentativasDeNumero)
            {
                // Só recomeça se FOI colisão de número (o número já existe agora). Qualquer outra falha (banco
                // travado, disco cheio, chave estrangeira) sobe na hora: repetir não a resolveria.
                await using var conferencia = contextFactory();
                if (!await conferencia.Vendas.AnyAsync(v => v.NumeroPedido == venda.NumeroPedido, ct))
                    throw;

                Registro.Aviso("Venda", $"Número de pedido {venda.NumeroPedido} já foi usado por outro processo — tentando o próximo (tentativa {tentativa}).");
            }
        }
    }

    // O 5º erro seguido não é mais azar de corrida: sobe pro operador/log em vez de insistir pra sempre.
    private const int MaximoTentativasDeNumero = 5;

    // Uma venda NOVA por tentativa: depois de um SaveChanges que falhou, o contexto (e o que ele rastreava) não serve
    // mais. O Guid é o da venda que efetivamente for gravada — a que falhou nunca existiu.
    private static Venda Montar(
        int caixaId,
        int? clienteId,
        IReadOnlyList<(int ProdutoId, decimal Quantidade, decimal PrecoUnitario, decimal DescontoItem, decimal AcrescimoItem)> itens,
        IReadOnlyList<(int FormaPagamentoId, decimal Valor, string? Bandeira)> pagamentos)
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

        foreach (var (formaPagamentoId, valor, bandeira) in pagamentos)
        {
            venda.Pagamentos.Add(new PagamentoVenda
            {
                VendaId = venda.Id,
                FormaPagamentoId = formaPagamentoId,
                Valor = valor,
                Bandeira = string.IsNullOrWhiteSpace(bandeira) ? null : bandeira.Trim(),
            });
        }

        return venda;
    }
}

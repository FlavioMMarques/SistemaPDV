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

    // exigirChaveSupervisor: o padrão é TRUE (a regra completa); quem decide desligar é a composição do app
    // (PoliticaSupervisor), não este serviço.
    public VendaLocalService(Func<AppDbContext> contextFactory, bool exigirChaveSupervisor = true)
    {
        this.contextFactory = contextFactory;
        ExigeChaveSupervisor = exigirChaveSupervisor;
    }

    // Se descartar uma venda pede a chave de um supervisor (ver PoliticaSupervisor).
    public bool ExigeChaveSupervisor { get; }

    private const int TamanhoMaximoMotivo = 300;

    // Descarta uma venda que a API nunca aceita (erro permanente) pra ela não travar o fechamento do caixa. Decisões do
    // usuário (2026-09-20): só SUPERVISOR, com a chave dele; só venda EM FALHA (a pendente comum ainda vai sair);
    // motivo obrigatório. Fica tudo registrado (quem autorizou, quem pediu, quando, por quê) e a venda continua
    // visível na lista de pedidos como trilha de auditoria — nunca é apagada.
    public async Task<ResultadoDescarte> DescartarVendaAsync(
        Guid vendaId, string? chaveSupervisor, string? motivo, int? solicitadaPorFuncionarioId, CancellationToken ct = default)
    {
        var motivoLimpo = motivo?.Trim() ?? string.Empty;
        if (motivoLimpo.Length == 0)
            return ResultadoDescarte.Falha("Informe o motivo do descarte.");
        if (motivoLimpo.Length > TamanhoMaximoMotivo)
            return ResultadoDescarte.Falha($"O motivo pode ter no máximo {TamanhoMaximoMotivo} caracteres.");

        await using var context = contextFactory();

        var venda = await context.Vendas.FirstOrDefaultAsync(v => v.Id == vendaId, ct);
        if (venda is null)
            return ResultadoDescarte.Falha("Venda não encontrada.");

        if (venda.SyncStatus != SyncStatus.FalhaSync)
            return ResultadoDescarte.Falha("Só uma venda em falha pode ser descartada (a pendente ainda vai ser enviada).");

        // Com a exigência desligada (PoliticaSupervisor) não há supervisor autorizando: DescartadaPorId fica vazio e a
        // trilha registra só quem PEDIU. Com ela ligada, sem chave válida nada é descartado.
        Funcionario? supervisor = null;
        if (ExigeChaveSupervisor)
        {
            supervisor = await SupervisorAutenticador.AutenticarAsync(context, chaveSupervisor, ct);
            if (supervisor is null)
            {
                Registro.Aviso("Auditoria", $"Descarte da venda {venda.Id} (#{venda.NumeroPedido}) recusado: chave de supervisor inválida (pedido por funcionário {solicitadaPorFuncionarioId?.ToString() ?? "?"}).");
                return ResultadoDescarte.Falha("Chave de supervisor inválida — o descarte precisa da chave de um supervisor.");
            }
        }

        venda.SyncStatus = SyncStatus.Descartada;
        venda.DescartadaEm = DateTime.UtcNow;
        venda.DescartadaPorId = supervisor?.Id;
        venda.SolicitadaPorId = solicitadaPorFuncionarioId;
        venda.MotivoDescarte = motivoLimpo;
        await context.SaveChangesAsync(ct);
        var autorizacao = supervisor is null ? "sem chave de supervisor (exigência desligada)" : $"pelo supervisor {supervisor.Id}";
        Registro.Info("Auditoria", $"Venda {venda.Id} (#{venda.NumeroPedido}) descartada {autorizacao} a pedido do funcionário {solicitadaPorFuncionarioId?.ToString() ?? "?"}. Motivo: {motivoLimpo}");
        return ResultadoDescarte.Ok();
    }

    // Vendas do caixa que ainda não chegaram à API (pendentes, com falha ou em espera): impedem o fechamento.
    public async Task<int> ContarNaoEnviadasAsync(int caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await context.Vendas.Where(v => v.CaixaId == caixaId).Where(VendaFiltros.NaoEnviada).CountAsync(ct);
    }

    // Quanto as vendas deste caixa somam em cada forma de pagamento — o "esperado" que a tela de fechar caixa mostra
    // pra o operador conferir contra o que apurou. Soma em memória (o SQLite não soma decimal no servidor).
    public async Task<IReadOnlyList<TotalFormaPagamento>> TotaisPorFormaPagamentoAsync(int caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var pagamentos = await context.Vendas
            .Where(v => v.CaixaId == caixaId)
            .Where(VendaFiltros.Valida)   // descartada = cancelada: não entra no esperado
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

    // Sem DescartadaPorId = descartada com a exigência de chave desligada (PoliticaSupervisor): ninguém autorizou, o
    // texto não inventa um nome.
    private static string TextoDoDescarte(Venda venda, Dictionary<int, string> autorizadores)
    {
        if (venda.DescartadaPorId is not { } porId)
            return $"Descartada: {venda.MotivoDescarte}";

        return $"Descartada por {autorizadores.GetValueOrDefault(porId, "?")}: {venda.MotivoDescarte}";
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

        // Trilha de auditoria: a venda descartada continua na lista, dizendo quem autorizou e por quê.
        var autorizadoresIds = vendas.Where(v => v.DescartadaPorId.HasValue).Select(v => v.DescartadaPorId!.Value).Distinct().ToList();
        var autorizadores = await context.Funcionarios.Where(f => autorizadoresIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Nome, ct);

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
            v.SyncStatus == SyncStatus.Descartada
                ? TextoDoDescarte(v, autorizadores)
                : v.UltimoErroSync))
            .ToList();
    }
}

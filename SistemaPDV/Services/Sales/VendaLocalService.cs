using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
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

    // Quanto as vendas deste caixa somam em cada bandeira de cartão (os pagamentos que têm bandeira gravada) — o
    // "esperado" da apuração por bandeira. Pagamentos sem bandeira (não é cartão, ou não havia cartões sincronizados)
    // ficam de fora. Vendas descartadas não entram (tratadas como canceladas). Soma em memória (SQLite/decimal).
    public async Task<IReadOnlyList<TotalBandeira>> TotaisPorBandeiraAsync(int caixaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var pagamentos = await context.Vendas
            .Where(v => v.CaixaId == caixaId)
            .Where(VendaFiltros.Valida)
            .SelectMany(v => v.Pagamentos)
            .Where(p => p.Bandeira != null)
            .Select(p => new { p.Bandeira, p.Valor })
            .ToListAsync(ct);

        return pagamentos
            .GroupBy(p => p.Bandeira!)
            .Select(g => new TotalBandeira(g.Key, g.Sum(p => p.Valor)))
            .OrderBy(t => t.Bandeira, StringComparer.OrdinalIgnoreCase)
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

    public async Task<IReadOnlyList<VendaResumo>> ListarVendasDoCaixaAsync(int caixaId, int? limite = null, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        // limite: só as N mais recentes (o painel principal mostra as últimas vendas); null = todas (a listagem de pedidos).
        var consulta = context.Vendas
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .Where(v => v.CaixaId == caixaId)
            .OrderByDescending(v => v.DataHora)
            .AsQueryable();
        if (limite is { } quantas)
            consulta = consulta.Take(quantas);

        return await ResumirAsync(context, caixaId, await consulta.ToListAsync(ct), ct);
    }

    // Tudo o que o modal "Detalhes do pedido" mostra de UMA venda: cabeçalho, itens, pagamentos e a requisição que a API
    // recebe (ou receberia) — montada pelo MESMO código que faz o envio (MontadorDeRequisicaoDeVenda), com o token mascarado.
    // Null se a venda não existe mais.
    public async Task<DetalheVenda?> ObterDetalheAsync(Guid vendaId, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var venda = await context.Vendas
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .FirstOrDefaultAsync(v => v.Id == vendaId, ct);
        if (venda is null)
            return null;

        var resumo = (await ResumirAsync(context, venda.CaixaId, new List<Venda> { venda }, ct)).Single();

        var produtoIds = venda.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await context.Produtos.Where(p => produtoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var formaIds = venda.Pagamentos.Select(p => p.FormaPagamentoId).Distinct().ToList();
        var formas = await context.FormasPagamento.Where(f => formaIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Nome, ct);

        var itens = venda.Itens.Select(i =>
        {
            produtos.TryGetValue(i.ProdutoId, out var produto);
            return new ItemDetalhe(
                produto?.CodigoParaExibicao ?? "—",
                produto?.Nome ?? "?",
                i.Quantidade,
                produto?.UnidadeMedida,
                i.PrecoUnitario,
                i.Quantidade * i.PrecoUnitario - i.DescontoItem + i.AcrescimoItem);
        }).ToList();

        var pagamentos = venda.Pagamentos
            .Select(p => new PagamentoDetalhe(formas.GetValueOrDefault(p.FormaPagamentoId, "?"), p.Valor, p.Bandeira))
            .ToList();

        var (requisicao, motivo) = await MontarRequisicaoParaExibirAsync(context, venda, ct);
        return new DetalheVenda(resumo, itens, pagamentos, venda.Desconto, venda.VendaIdExterno, requisicao, motivo);
    }

    private static readonly JsonSerializerOptions OpcoesDeExibicao = new()
    {
        WriteIndented = true,
        // Só para LER na tela: acento como acento ("ESPÉCIE"), não "É". O envio real usa o serializador padrão; o
        // conteúdo é o mesmo, só a grafia dos caracteres não-ASCII muda.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // A requisição como texto (método + URL, cabeçalhos com o token MASCARADO, corpo em JSON) ou, se ainda não dá para
    // montá-la, o motivo. O token de verdade nunca passa por aqui: quem lê a tela (ou tira um print para suporte) não o vê.
    private static async Task<(string? Requisicao, string? Motivo)> MontarRequisicaoParaExibirAsync(AppDbContext context, Venda venda, CancellationToken ct)
    {
        if (venda.SyncStatus == SyncStatus.Descartada)
            return (null, "Venda descartada: esta requisição não é enviada à API.");

        try
        {
            var montagem = await MontadorDeRequisicaoDeVenda.MontarAsync(context, venda, ct);
            if (montagem.Requisicao is not { } requisicao)
                return (null, $"Ainda não dá para montar a requisição: {montagem.Espera}");

            var texto = string.Join(Environment.NewLine,
                $"POST {requisicao.Url}",
                "Api-Version: v2",
                "Authorization: Bearer ••••••••",
                "Content-Type: application/json",
                string.Empty,
                JsonSerializer.Serialize(requisicao.Corpo, OpcoesDeExibicao));
            return (texto, null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, $"Não foi possível montar a requisição: {ex.Message}");
        }
    }

    // O resumo de uma ou mais vendas do MESMO caixa: resolve operador, autorizadores de descarte, clientes, formas de
    // pagamento e nomes de produtos em lotes (sem N+1).
    private static async Task<IReadOnlyList<VendaResumo>> ResumirAsync(AppDbContext context, int caixaId, List<Venda> vendas, CancellationToken ct)
    {
        var caixa = await context.Caixas.FindAsync(new object[] { caixaId }, ct);
        var operadorNome = caixa is not null
            ? (await context.Funcionarios.FindAsync(new object[] { caixa.FuncionarioId }, ct))?.Nome ?? "—"
            : "—";

        // Trilha de auditoria: a venda descartada continua na lista, dizendo quem autorizou e por quê.
        var autorizadoresIds = vendas.Where(v => v.DescartadaPorId.HasValue).Select(v => v.DescartadaPorId!.Value).Distinct().ToList();
        var autorizadores = await context.Funcionarios.Where(f => autorizadoresIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Nome, ct);

        var clienteIds = vendas.Where(v => v.ClienteId.HasValue).Select(v => v.ClienteId!.Value).Distinct().ToList();
        var clientes = await context.Clientes.Where(c => clienteIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var formaIds = vendas.SelectMany(v => v.Pagamentos.Select(p => p.FormaPagamentoId)).Distinct().ToList();
        var formas = await context.FormasPagamento.Where(f => formaIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        var produtoIds = vendas.SelectMany(v => v.Itens.Select(i => i.ProdutoId)).Distinct().ToList();
        var nomesProdutos = await context.Produtos.Where(p => produtoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nome, ct);

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
                : v.UltimoErroSync,
            ResumirItens(v, nomesProdutos),
            DetalharItens(v, nomesProdutos)))
            .ToList();
    }

    // "3 itens: Arroz Parboilizado 1kg" — a coluna "Resumo itens" da listagem (a interface corta com reticências).
    private static string ResumirItens(Venda venda, IReadOnlyDictionary<int, string> nomes)
    {
        var quantidade = venda.Itens.Count;
        if (quantidade == 0)
            return "Sem itens";

        var primeiro = nomes.GetValueOrDefault(venda.Itens.First().ProdutoId, "?");
        return $"{quantidade} {(quantidade == 1 ? "item" : "itens")}: {primeiro}";
    }

    // Um item por linha ("4 × Queijo Mussarela Fatiado 200g"): a dica que mostra a lista inteira ao passar o mouse.
    private static string DetalharItens(Venda venda, IReadOnlyDictionary<int, string> nomes) =>
        string.Join(Environment.NewLine, venda.Itens.Select(i =>
            $"{i.Quantidade.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"))} × {nomes.GetValueOrDefault(i.ProdutoId, "?")}"));
}

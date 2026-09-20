using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Services.Caixa;

// Abrir e fechar caixa aqui são operações puramente LOCAIS — nunca fazem chamada de
// rede. A sincronização com a API fica inteira em CaixaSyncService (outbox).
public class CaixaService
{
    // Precisa bater com DigitacaoBandeiraCaixaConfiguration.HasMaxLength — validado
    // aqui também pra devolver ComFalha em vez de deixar o SaveChanges estourar.
    private const int TamanhoMaximoBandeira = 30;

    // O SoftcomShop tem até 6 turnos por dia (o app tinha só 3 fixos na tela).
    public const int TurnoMinimo = 1;
    public const int TurnoMaximo = 6;

    private readonly Func<AppDbContext> contextFactory;

    public CaixaService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    // Leitura pura, sem alterar nada — usada pelo ShellViewModel pra decidir a
    // navegação pós-login (tem caixa aberto? vai direto pro Dashboard; senão, olha
    // ExigirAberturaCaixa). Ignora data/turno de propósito: um caixa aberto continua
    // "aberto" até ser fechado, mesmo que isso atravesse a meia-noite.
    public async Task<Models.Caixa?> ObterCaixaAbertoAsync(int funcionarioId, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await context.Caixas.FirstOrDefaultAsync(
            c => c.FuncionarioId == funcionarioId && c.Status == StatusCaixa.Aberto, ct);
    }

    // Turnos que este operador já usou na data (abertos OU já fechados): cada combinação operador+data+turno só pode
    // ter UM caixa (a API tem a mesma regra), então um turno fechado não reabre — a tela sugere o próximo livre.
    public async Task<IReadOnlyList<int>> TurnosUsadosAsync(int funcionarioId, DateOnly dataCaixa, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await context.Caixas
            .Where(c => c.FuncionarioId == funcionarioId && c.DataCaixa == dataCaixa)
            .Select(c => c.Turno)
            .ToListAsync(ct);
    }

    // Usada pelo serviço, pela tela (aviso) e pelo envio do fechamento — uma redação só.
    public static string MensagemVendasNaoEnviadas(int quantidade) =>
        (quantidade == 1 ? "Há 1 venda deste caixa ainda não enviada" : $"Há {quantidade} vendas deste caixa ainda não enviadas") +
        " — aguarde a sincronização (ou use \"Reenviar falhas\" em Pedidos) antes de fechar o caixa.";

    private static string MensagemTurnoJaUsado(int turno) =>
        $"O turno {turno} de hoje já foi usado por este operador (mesmo com o caixa fechado, ele não reabre) — escolha outro turno.";

    public async Task<ResultadoOperacaoCaixa<Models.Caixa>> AbrirCaixaLocalAsync(
        int funcionarioId, DateOnly dataCaixa, int turno, decimal trocoInicial, CancellationToken ct = default)
    {
        // A API só conhece os turnos 1-6: um valor fora disso viraria 422 depois, com o caixa
        // já gravado local como pendente pra sempre.
        if (turno < TurnoMinimo || turno > TurnoMaximo)
            return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha($"Turno inválido — o SoftcomShop aceita de {TurnoMinimo} a {TurnoMaximo}.");

        await using var context = contextFactory();

        // Mesma chave natural do índice único em CaixaConfiguration — checa antes de
        // tentar salvar pra devolver uma mensagem legível em vez de deixar estourar
        // a constraint do banco. Isso NÃO é atômico sozinho (duas chamadas quase
        // simultâneas podem passar aqui antes de qualquer uma salvar) — por isso o
        // catch de DbUpdateException logo abaixo é a proteção real, essa checagem
        // aqui é só pro caminho comum (sem corrida) devolver mensagem amigável sem
        // precisar tentar salvar primeiro.
        var jaExisteLocal = await context.Caixas.AnyAsync(
            c => c.FuncionarioId == funcionarioId && c.DataCaixa == dataCaixa && c.Turno == turno, ct);

        if (jaExisteLocal)
            return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha(MensagemTurnoJaUsado(turno));

        var caixa = new Models.Caixa
        {
            FuncionarioId = funcionarioId,
            DataCaixa = dataCaixa,
            Turno = turno,
            DataAbertura = DateTime.Now,
            TrocoInicial = trocoInicial,
            Status = StatusCaixa.Aberto,
            SyncStatus = SyncStatus.PendenteSync,
        };

        context.Caixas.Add(caixa);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Revisão de código: a checagem AnyAsync acima tem uma janela de corrida
            // (TOCTOU — time-of-check to time-of-use); um duplo-clique rápido podia
            // fazer as duas chamadas passarem pela checagem antes de qualquer uma
            // salvar, e a segunda estourava essa exceção crua em vez da mensagem
            // amigável. O índice único do banco (CaixaConfiguration) continua sendo
            // a proteção de verdade; esse catch só traduz a violação dele pra mesma
            // mensagem que o caminho comum já devolve.
            return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha(MensagemTurnoJaUsado(turno));
        }

        return ResultadoOperacaoCaixa<Models.Caixa>.ComSucesso(caixa);
    }

    public async Task<ResultadoOperacaoCaixa<Models.Caixa>> FecharCaixaLocalAsync(
        int caixaId,
        decimal trocoFinal,
        IReadOnlyList<(int FormaPagamentoId, decimal Valor)> digitacoes,
        IReadOnlyList<(string Bandeira, decimal Valor)> digitacoesBandeiras,
        CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var caixa = await context.Caixas
            .Include(c => c.Digitacoes)
            .Include(c => c.DigitacoesBandeiras)
            .FirstOrDefaultAsync(c => c.Id == caixaId, ct);

        if (caixa is null)
            return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha("Caixa não encontrado.");

        if (caixa.Status == StatusCaixa.Fechado)
            return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha("Esse caixa já está fechado.");

        // Regra de negócio (usuário, 2026-09-20): só fecha se NÃO há venda pendente. O fechamento resume o caixa na API;
        // uma venda que ainda não chegou (pendente, com falha ou em espera crescente) ficaria de fora.
        var naoEnviadas = await context.Vendas.Where(v => v.CaixaId == caixaId).Where(VendaFiltros.NaoEnviada).CountAsync(ct);
        if (naoEnviadas > 0)
            return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha(MensagemVendasNaoEnviadas(naoEnviadas));

        // Validado ANTES de mudar qualquer coisa no caixa — revisão de código achou
        // que, sem isso, um formaPagamentoId inexistente localmente (FK Restrict) ou
        // uma bandeira maior que o limite da coluna estourava DbUpdateException crua
        // no SaveChanges, em vez do ComFalha que todo o resto do método usa.
        foreach (var (formaPagamentoId, _) in digitacoes)
        {
            var formaPagamentoExiste = await context.FormasPagamento.AnyAsync(f => f.Id == formaPagamentoId, ct);
            if (!formaPagamentoExiste)
                return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha($"Forma de pagamento {formaPagamentoId} não encontrada.");
        }

        foreach (var (bandeira, _) in digitacoesBandeiras)
        {
            if (bandeira.Length > TamanhoMaximoBandeira)
                return ResultadoOperacaoCaixa<Models.Caixa>.ComFalha($"Nome de bandeira muito longo (máximo {TamanhoMaximoBandeira} caracteres): \"{bandeira}\".");
        }

        caixa.DataFechamento = DateTime.Now;
        caixa.TrocoFinal = trocoFinal;
        caixa.Status = StatusCaixa.Fechado;

        // O registro mudou (fechou) — volta a precisar de sincronização, dessa vez
        // do fechamento, não da abertura. CaixaSyncService decide qual é qual olhando
        // AberturaSincronizada (não Status+IdExterno — essa combinação tinha um bug
        // pego em revisão de código, ver docs/APRENDIZADOS.md).
        caixa.SyncStatus = SyncStatus.PendenteSync;
        caixa.UltimoErroSync = null;

        foreach (var (formaPagamentoId, valor) in digitacoes)
            caixa.Digitacoes.Add(new DigitacaoCaixa { CaixaId = caixa.Id, FormaPagamentoId = formaPagamentoId, Valor = valor });

        foreach (var (bandeira, valor) in digitacoesBandeiras)
            caixa.DigitacoesBandeiras.Add(new DigitacaoBandeiraCaixa { CaixaId = caixa.Id, Bandeira = bandeira, Valor = valor });

        await context.SaveChangesAsync(ct);

        return ResultadoOperacaoCaixa<Models.Caixa>.ComSucesso(caixa);
    }
}

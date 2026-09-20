using System;
using System.Collections.Generic;

namespace SistemaPDV.Models;

public class Caixa : ISincronizavel<int>, IOutboxRetentavel
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendenteSync;

    public int FuncionarioId { get; set; }

    // DateOnly (não DateTime) porque "caixa do dia 2026-09-18" é um conceito de DATA,
    // sem hora — usar DateTime aqui deixaria a comparação/índice sujeitos a diferenças
    // de horário que não deveriam importar (duas aberturas às 08:00 e às 08:00:01 do
    // mesmo dia são o mesmo "caixa do dia", não caixas diferentes).
    public DateOnly DataCaixa { get; set; }
    public int Turno { get; set; }

    public DateTime DataAbertura { get; set; }
    public DateTime? DataFechamento { get; set; }
    public decimal TrocoInicial { get; set; }
    public decimal? TrocoFinal { get; set; }
    public StatusCaixa Status { get; set; } = StatusCaixa.Aberto;

    // Mesmo padrão de Venda.UltimoErroSync: guarda o motivo de uma sincronização de
    // abertura/fechamento que falhou (409 tratado à parte, 422 cai aqui), sem travar
    // o operador nem perder o porquê.
    public string? UltimoErroSync { get; set; }

    // Espera crescente/teto de retentativas (ver PoliticaRetentativa). Uma contagem só pros
    // dois envios do caixa (abertura e fechamento): zera quando cada um é confirmado.
    public int TentativasEnvio { get; set; }
    public DateTime? ProximaTentativaEm { get; set; }

    // Sinaliza especificamente que a ABERTURA já foi confirmada com o servidor (200
    // ou 409 — os dois significam "o servidor já sabe desse caixa"), independente do
    // SyncStatus atual. Existe porque um único SyncStatus não dá conta de representar
    // duas ações que acontecem em momentos diferentes (abrir, depois fechar): sem
    // esse campo separado, fechar o caixa reseta SyncStatus pra PendenteSync e o
    // sistema não tem mais como saber se isso quer dizer "abertura pendente" ou
    // "fechamento pendente" — ver docs/APRENDIZADOS.md.
    public bool AberturaSincronizada { get; set; }

    public ICollection<DigitacaoCaixa> Digitacoes { get; set; } = new List<DigitacaoCaixa>();
    public ICollection<DigitacaoBandeiraCaixa> DigitacoesBandeiras { get; set; } = new List<DigitacaoBandeiraCaixa>();
}

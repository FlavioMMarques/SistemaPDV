using System;

namespace SistemaPDV.Models;

public class Caixa : ISincronizavel<int>
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
}

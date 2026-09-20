using System;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Sales;

public record VendaResumo(
    Guid Id,
    int? Numero,
    DateTime DataHora,
    string ClienteNome,
    string OperadorNome,
    decimal Total,
    string FormasPagamento,
    SyncStatus SyncStatus,
    string? UltimoErroSync = null);

// O que as vendas do caixa somam em cada forma de pagamento — o "esperado" do fechamento de caixa.
public record TotalFormaPagamento(int FormaPagamentoId, string Nome, decimal Total);

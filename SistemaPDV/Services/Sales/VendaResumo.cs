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

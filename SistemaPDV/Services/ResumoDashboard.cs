using System;

namespace SistemaPDV.Services;

public record ResumoDashboard(
    decimal FaturamentoHoje,
    int EstoqueTotal,
    int PendentesOutbox,
    DateTimeOffset? UltimaSincronizacao);

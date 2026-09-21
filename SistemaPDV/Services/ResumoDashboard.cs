using System;

namespace SistemaPDV.Services;

public record ResumoDashboard(
    decimal FaturamentoHoje,
    int EstoqueTotal,
    int PendentesOutbox,
    DateTimeOffset? UltimaSincronizacao,
    int VendasEmitidas = 0,
    int ProdutosCadastrados = 0);

using System;
using System.Linq.Expressions;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Sales;

// Definições ÚNICAS de "qual venda conta" — cada consulta sobre vendas que reescrevesse `!= Sincronizado` à mão
// esqueceria a venda DESCARTADA (e ela voltaria a travar o fechamento do caixa ou a entrar no faturamento).
public static class VendaFiltros
{
    // Ainda precisa chegar à API: pendente, em falha ou em espera. A descartada NÃO precisa.
    public static readonly Expression<Func<Venda, bool>> NaoEnviada =
        v => v.SyncStatus != SyncStatus.Sincronizado && v.SyncStatus != SyncStatus.Descartada;

    // Vale como venda (soma no faturamento e no esperado): tudo, menos a descartada (tratada como cancelada).
    public static readonly Expression<Func<Venda, bool>> Valida =
        v => v.SyncStatus != SyncStatus.Descartada;
}

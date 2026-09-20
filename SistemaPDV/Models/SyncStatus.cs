namespace SistemaPDV.Models;

public enum SyncStatus
{
    PendenteSync,
    Sincronizado,
    FalhaSync,

    // Só Venda usa: venda em falha que um supervisor descartou (com motivo e auditoria) porque a API nunca vai
    // aceitá-la. Fica no banco e na lista de pedidos (trilha de auditoria), mas NÃO é enviada, NÃO conta como
    // "não enviada" (não trava o fechamento do caixa) e NÃO entra no faturamento nem no esperado do fechamento.
    Descartada,
}

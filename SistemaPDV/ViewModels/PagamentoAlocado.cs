using SistemaPDV.Models;

namespace SistemaPDV.ViewModels;

// Uma forma de pagamento alocada na venda em andamento — suporta pagamento misto
// (mais de um PagamentoAlocado na mesma venda), cada um com seu próprio valor.
public class PagamentoAlocado
{
    public required FormaPagamento FormaPagamento { get; init; }
    public decimal Valor { get; set; }
}

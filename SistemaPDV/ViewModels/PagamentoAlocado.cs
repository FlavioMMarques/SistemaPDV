using SistemaPDV.Models;

namespace SistemaPDV.ViewModels;

// Uma forma de pagamento alocada na venda em andamento — suporta pagamento misto
// (mais de um PagamentoAlocado na mesma venda), cada um com seu próprio valor.
public class PagamentoAlocado
{
    public required FormaPagamento FormaPagamento { get; init; }
    public decimal Valor { get; set; }

    // Bandeira escolhida quando a forma é cartão (e há bandeiras sincronizadas); nula nos demais casos.
    public string? Bandeira { get; init; }

    // O que a lista de pagamentos mostra: "CARTÃO DE CRÉDITO • MASTERCARD".
    public string Descricao => string.IsNullOrEmpty(Bandeira) ? FormaPagamento.Nome : $"{FormaPagamento.Nome} • {Bandeira}";
}

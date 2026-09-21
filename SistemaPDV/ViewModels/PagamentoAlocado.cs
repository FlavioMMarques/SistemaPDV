using SistemaPDV.Models;

namespace SistemaPDV.ViewModels;

// Uma forma de pagamento alocada na venda em andamento — suporta pagamento misto
// (mais de um PagamentoAlocado na mesma venda), cada um com seu próprio valor.
public class PagamentoAlocado
{
    public required FormaPagamento FormaPagamento { get; init; }
    public decimal Valor { get; set; }

    // Dinheiro: o que o cliente ENTREGOU (pode ser mais que o Valor lançado — a diferença é o troco). Nulo nas outras formas.
    // Só na tela: a venda grava o Valor; o troco não é enviado à API (ver todo.md).
    public decimal? ValorRecebido { get; init; }

    public decimal Troco => ValorRecebido is { } recebido ? System.Math.Max(recebido - Valor, 0m) : 0m;

    // "recebido R$ 50,00 • troco R$ 17,71" — só no dinheiro que gerou troco; vazio nos demais.
    public string Detalhe => Troco > 0
        ? $"recebido R$ {Services.ValorMonetario.Formatar(ValorRecebido!.Value)} • troco R$ {Services.ValorMonetario.Formatar(Troco)}"
        : string.Empty;

    public bool TemDetalhe => Troco > 0;

    // Bandeira escolhida quando a forma é cartão (e há bandeiras sincronizadas); nula nos demais casos.
    public string? Bandeira { get; init; }

    // O que a lista de pagamentos mostra: "CARTÃO DE CRÉDITO • MASTERCARD".
    public string Descricao => string.IsNullOrEmpty(Bandeira) ? FormaPagamento.Nome : $"{FormaPagamento.Nome} • {Bandeira}";
}

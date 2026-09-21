using System;

namespace SistemaPDV.Models;

public class PagamentoVenda
{
    public int Id { get; set; }
    public Guid VendaId { get; set; }
    public int FormaPagamentoId { get; set; }
    public decimal Valor { get; set; }

    // Bandeira do cartão escolhida no pagamento (BandeiraNome de um Cartao sincronizado, ex: "MASTERCARD"); nula nas
    // formas que não são cartão e quando ainda não há cartões sincronizados para escolher. É o que alimenta a apuração
    // por bandeira no fechamento do caixa. Guardado como TEXTO (sem chave estrangeira): a venda é um registro histórico
    // e não pode mudar nem quebrar se o cadastro de cartões mudar depois.
    public string? Bandeira { get; set; }
}

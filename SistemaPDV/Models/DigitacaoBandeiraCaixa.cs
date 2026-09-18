namespace SistemaPDV.Models;

// Quanto o operador apurou por bandeira de cartão (VISA, MASTER...) no fechamento.
public class DigitacaoBandeiraCaixa
{
    public int Id { get; set; }
    public int CaixaId { get; set; }
    public required string Bandeira { get; set; }
    public decimal Valor { get; set; }
}

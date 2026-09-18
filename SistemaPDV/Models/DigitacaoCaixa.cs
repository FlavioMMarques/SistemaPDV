namespace SistemaPDV.Models;

// Quanto o operador apurou por forma de pagamento no fechamento — precisa existir
// localmente porque o fechamento é offline-first (fica guardado até sincronizar).
public class DigitacaoCaixa
{
    public int Id { get; set; }
    public int CaixaId { get; set; }
    public int FormaPagamentoId { get; set; }
    public decimal Valor { get; set; }
}

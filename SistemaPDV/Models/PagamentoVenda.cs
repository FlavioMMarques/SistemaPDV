using System;

namespace SistemaPDV.Models;

public class PagamentoVenda
{
    public int Id { get; set; }
    public Guid VendaId { get; set; }
    public int FormaPagamentoId { get; set; }
    public decimal Valor { get; set; }
}

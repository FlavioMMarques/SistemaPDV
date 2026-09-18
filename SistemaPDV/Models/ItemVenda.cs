using System;

namespace SistemaPDV.Models;

public class ItemVenda
{
    public int Id { get; set; }
    public Guid VendaId { get; set; }
    public int ProdutoId { get; set; }
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal DescontoItem { get; set; }
    public decimal AcrescimoItem { get; set; }
}

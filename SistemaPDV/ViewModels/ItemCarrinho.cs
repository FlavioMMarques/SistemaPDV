using SistemaPDV.Models;

namespace SistemaPDV.ViewModels;

// Item do carrinho em memória, antes de virar ItemVenda de verdade — não é uma
// entidade EF, só um objeto de apresentação pro PdvViewModel/PdvView.
public class ItemCarrinho
{
    public required Produto Produto { get; init; }
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal DescontoItem { get; set; }
    public decimal AcrescimoItem { get; set; }

    public decimal Total => Quantidade * PrecoUnitario - DescontoItem + AcrescimoItem;
}

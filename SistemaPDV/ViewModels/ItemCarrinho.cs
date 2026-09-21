using System.Globalization;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// Item do carrinho em memória, antes de virar ItemVenda de verdade — não é uma
// entidade EF, só um objeto de apresentação pro PdvViewModel/PdvView.
public class ItemCarrinho : ReactiveObject
{
    private int numero;

    public required Produto Produto { get; init; }
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal DescontoItem { get; set; }
    public decimal AcrescimoItem { get; set; }

    public decimal Total => Quantidade * PrecoUnitario - DescontoItem + AcrescimoItem;

    // Posição no cupom (1, 2, 3…): o PdvViewModel renumera quando um item sai — por isso notifica a mudança.
    public int Numero
    {
        get => numero;
        set => this.RaiseAndSetIfChanged(ref numero, value);
    }

    // "1 un x R$ 16,50 (789100030)" — quantidade (com fração quando houver), unidade, preço unitário e código.
    public string Descricao
    {
        get
        {
            var quantidade = Quantidade.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"));
            var unidade = string.IsNullOrWhiteSpace(Produto.UnidadeMedida) ? "un" : Produto.UnidadeMedida.Trim().ToLowerInvariant();
            var codigo = Produto.CodigoParaExibicao is { Length: > 0 } c ? $" ({c})" : string.Empty;
            return $"{quantidade} {unidade} x R$ {ValorMonetario.Formatar(PrecoUnitario)}{codigo}";
        }
    }
}

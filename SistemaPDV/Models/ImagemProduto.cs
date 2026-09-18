namespace SistemaPDV.Models;

// Owned type (coleção): imagens só existem em função do produto, sem consulta
// independente — mesmo princípio de composição do TabelaPreco em Cliente, mas em
// lista (um produto pode ter mais de uma imagem, ex: "PRINCIPAL" + outras).
public class ImagemProduto
{
    public string? Descricao { get; set; }
    public required string ArquivoOriginal { get; set; }
    public string? ArquivoThumbnail { get; set; }
    public string? Tipo { get; set; }
}

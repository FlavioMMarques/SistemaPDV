using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// Só os campos do escopo aprovado (varejo genérico + fiscal essencial + imagens) —
// o schema completo da API tem ~50 campos, boa parte de verticais fora de escopo
// (combustível, restaurante). Ver softcomshop-api-contract na memória do agente e
// specs/SPEC-catalog-sync.md.
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class ProdutoApiDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("sku")] public string? Sku { get; set; }
    [JsonPropertyName("codigo_barras")] public string? CodigoBarras { get; set; }
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    [JsonPropertyName("nome_original")] public string? NomeOriginal { get; set; }
    [JsonPropertyName("fabricante")] public string? Fabricante { get; set; }
    [JsonPropertyName("referencia")] public string? Referencia { get; set; }
    [JsonPropertyName("grupo_id")] public int? GrupoId { get; set; }

    [JsonPropertyName("estoque")] public decimal Estoque { get; set; }
    [JsonPropertyName("unidade_medida")] public string? UnidadeMedida { get; set; }
    [JsonPropertyName("peso")] public decimal? Peso { get; set; }

    [JsonPropertyName("preco_venda")] public decimal PrecoVenda { get; set; }
    [JsonPropertyName("preco_compra")] public decimal? PrecoCompra { get; set; }
    [JsonPropertyName("margem_lucro")] public decimal? MargemLucro { get; set; }

    [JsonPropertyName("ncm")] public string? Ncm { get; set; }
    [JsonPropertyName("cest")] public string? Cest { get; set; }
    [JsonPropertyName("codigo_beneficio_fiscal")] public string? CodigoBeneficioFiscal { get; set; }
    [JsonPropertyName("status_fiscal")] public int StatusFiscal { get; set; }
    [JsonPropertyName("codigo_nfe")] public string? CodigoNfe { get; set; }

    // "vender": true/false na API real (0/1 na documentação) — BooleanoFlexivelConverter aceita as duas formas.
    [JsonPropertyName("vender")] [JsonConverter(typeof(BooleanoFlexivelConverter))] public bool? Vender { get; set; }
    [JsonPropertyName("restricao_idade")] public bool RestricaoIdade { get; set; }
    [JsonPropertyName("hortifruit")] public bool Hortifruit { get; set; }
    [JsonPropertyName("observacao")] public string? Observacao { get; set; }

    [JsonPropertyName("promocao_preco")] public decimal? PromocaoPreco { get; set; }
    [JsonPropertyName("promocao_validade")] public string? PromocaoValidade { get; set; }
    [JsonPropertyName("promocao_quantidade")] public int? PromocaoQuantidade { get; set; }

    [JsonPropertyName("produto_imagem")] public List<ImagemProdutoApiDto> ProdutoImagem { get; set; } = new();
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class ImagemProdutoApiDto
{
    [JsonPropertyName("descricao")] public string? Descricao { get; set; }
    [JsonPropertyName("arquivo_original")] public string? ArquivoOriginal { get; set; }
    [JsonPropertyName("arquivo_thumbnail")] public string? ArquivoThumbnail { get; set; }
    [JsonPropertyName("tipo")] public string? Tipo { get; set; }
}

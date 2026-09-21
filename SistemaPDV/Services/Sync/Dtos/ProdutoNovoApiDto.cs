using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// POST softauth/api/v2/produtos/produtos — "cadastro múltiplo": de 1 a 100 produtos por requisição, no corpo { "produtos": [...] },
// tudo numa transação (uma falha desfaz o lote inteiro). O app envia UM produto por requisição, para um produto ruim não
// derrubar os outros.
public class ProdutoNovoLoteRequestDto
{
    [JsonPropertyName("produtos")] public List<ProdutoNovoRequestDto> Produtos { get; set; } = new();
}

// Só os campos que o cadastro do PDV preenche (nome, categoria, preço, código). Os demais do Swagger ficam de fora — a API
// completa o que falta. Os que têm padrão no Swagger vão explícitos: a API real grava todas as colunas e não dá para confiar
// que o "default" da documentação seja aplicado quando o campo some (aprendizado do cliente, docs/APRENDIZADOS.md #84).
public class ProdutoNovoRequestDto
{
    [JsonPropertyName("nome")] public string Nome { get; set; } = string.Empty;

    // ID de um grupo ATIVO que já existe na API (o IdExterno do Grupo sincronizado); a API não aceita 0.
    [JsonPropertyName("grupo_id")] public int GrupoId { get; set; }

    [JsonPropertyName("preco_venda")] public decimal PrecoVenda { get; set; }

    // Únicos entre os produtos ativos quando informados (a API recusa duplicado).
    [JsonPropertyName("codigo_barras")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CodigoBarras { get; set; }

    // No máximo 20 caracteres.
    [JsonPropertyName("referencia")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Referencia { get; set; }

    // O exemplo do Swagger manda estes três com 0.
    [JsonPropertyName("preco_compra")] public decimal PrecoCompra { get; set; }
    [JsonPropertyName("margem_lucro")] public decimal MargemLucro { get; set; }
    [JsonPropertyName("percentual_comissao_produto")] public decimal PercentualComissaoProduto { get; set; }

    [JsonPropertyName("vender")] public bool Vender { get; set; } = true;
    [JsonPropertyName("controlar_estoque")] public bool ControlarEstoque { get; set; } = true;
    [JsonPropertyName("desativado")] public bool Desativado { get; set; }
    [JsonPropertyName("habilitar_grade")] public bool HabilitarGrade { get; set; }
    [JsonPropertyName("agrupar_pedido")] public bool AgruparPedido { get; set; } = true;
    [JsonPropertyName("tipo_produto")] public string TipoProduto { get; set; } = "PRODUTO";
}

// Resposta 200: { "data": { "total": 1, "created": [ { "id": 1049, ..., "produto_empresas": [ { "id": 53231, "empresa_id": "44",
// "produto_empresa_grade": { "id": 122848 } } ] } ] } }. Só o que o app usa: os três ids (o PDV precisa deles para vender o
// produto). Números chegam ora como número, ora como texto ("44").
public class ProdutoNovoRespostaDto
{
    [JsonPropertyName("data")] public ProdutoNovoRespostaDataDto? Data { get; set; }
}

public class ProdutoNovoRespostaDataDto
{
    [JsonPropertyName("created")] public List<ProdutoNovoCriadoDto> Created { get; set; } = new();
}

public class ProdutoNovoCriadoDto
{
    // O produto-base: é o "produto_id" da venda (Produto.ProdutoIdApi).
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }

    [JsonPropertyName("produto_empresas")] public List<ProdutoNovoEmpresaDto> ProdutoEmpresas { get; set; } = new();
}

public class ProdutoNovoEmpresaDto
{
    [JsonPropertyName("empresa_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int EmpresaId { get; set; }

    [JsonPropertyName("produto_empresa_grade")] public ProdutoNovoGradeDto? ProdutoEmpresaGrade { get; set; }
}

public class ProdutoNovoGradeDto
{
    // O "produto_empresa_grade_id" da venda (Produto.IdExterno).
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }
}

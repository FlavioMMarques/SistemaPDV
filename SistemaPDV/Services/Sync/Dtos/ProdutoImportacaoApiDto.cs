using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// POST softauth/api/produtos/importacao/produto — **experimental**, rota v1 (sem "/v2/", como softauth/api/financeiros/cartoes).
// Ao contrário do cadastro em lote (POST .../v2/produtos/produtos), este endpoint aceita um bloco "produto_grade" — o
// registro da EMPRESA (produto_empresa_grade), que é onde o preço chega zerado quando o produto é criado pelo lote
// (ver APRENDIZADOS #86/#94). Hipótese a confirmar em teste real: que "produto_grade[].preco_venda" grava o preço aí.
// A forma da RESPOSTA não está documentada — só reaproveita o formato conhecido do lote (id do produto-base +
// produto_empresas[].produto_empresa_grade.id); se vier diferente, o produto fica em falha com a resposta crua à mostra
// (nunca se confia cegamente: sem os dois ids o produto não serve pra vender — mesma regra de sempre).
public class ProdutoImportacaoRequestDto
{
    [JsonPropertyName("nome")] public string Nome { get; set; } = string.Empty;

    // ID de um grupo ATIVO que já existe na API; a API não aceita 0.
    [JsonPropertyName("grupo_id")] public int GrupoId { get; set; }

    [JsonPropertyName("preco_venda")] public decimal PrecoVenda { get; set; }

    // Só vai quando o operador informou o custo — mesma cautela do lote (custo zerado pode anular o preço de venda).
    [JsonPropertyName("preco_compra")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PrecoCompra { get; set; }

    [JsonPropertyName("codigo_barras")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CodigoBarras { get; set; }

    [JsonPropertyName("referencia")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Referencia { get; set; }

    // Neste endpoint o Swagger documenta os booleanos como inteiro (0/1), diferente do lote v2 (JSON true/false).
    [JsonPropertyName("vender")] public int Vender { get; set; } = 1;
    [JsonPropertyName("controlar_estoque")] public int ControlarEstoque { get; set; } = 1;
    [JsonPropertyName("desativado")] public int Desativado { get; set; }
    [JsonPropertyName("habilitar_grade")] public int HabilitarGrade { get; set; }
    [JsonPropertyName("agrupar_pedido")] public int AgruparPedido { get; set; } = 1;

    // A peça que o lote não tem: o preço no registro da empresa.
    [JsonPropertyName("produto_grade")] public List<ProdutoImportacaoGradeItemDto> ProdutoGrade { get; set; } = new();
}

public class ProdutoImportacaoGradeItemDto
{
    [JsonPropertyName("preco_venda")] public decimal PrecoVenda { get; set; }
}

// Resposta — hipótese: mesmo formato do item criado pelo lote, só que direto em "data" (sem "created[]", por ser um produto só).
public class ProdutoImportacaoRespostaDto
{
    [JsonPropertyName("data")] public ProdutoImportacaoCriadoDto? Data { get; set; }
}

public class ProdutoImportacaoCriadoDto
{
    // O produto-base: é o "produto_id" da venda (Produto.ProdutoIdApi).
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }

    [JsonPropertyName("produto_empresas")] public List<ProdutoImportacaoEmpresaDto> ProdutoEmpresas { get; set; } = new();
}

public class ProdutoImportacaoEmpresaDto
{
    [JsonPropertyName("empresa_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int EmpresaId { get; set; }

    [JsonPropertyName("produto_empresa_grade")] public ProdutoImportacaoGradeDto? ProdutoEmpresaGrade { get; set; }
}

public class ProdutoImportacaoGradeDto
{
    // O "produto_empresa_grade_id" da venda (Produto.IdExterno).
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }
}

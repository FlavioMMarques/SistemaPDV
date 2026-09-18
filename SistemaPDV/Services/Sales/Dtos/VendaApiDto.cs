using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sales.Dtos;

// Só o subconjunto essencial do schema completo de POST /api/v2/vendas — campos
// claramente gerados pelo servidor (xml, numero_nfe, numero_documento, codigo_status)
// ficam de fora por ora (ver SPEC-sales.md).
public class VendaRequestDto
{
    [JsonPropertyName("guid")] public string Guid { get; set; } = string.Empty;

    // Unix timestamp (inteiro), não string ISO — confirmado no schema real da API.
    [JsonPropertyName("data_hora")] public long DataHora { get; set; }

    [JsonPropertyName("empresa_id")] public int EmpresaId { get; set; }
    [JsonPropertyName("usuario_id")] public int UsuarioId { get; set; }
    [JsonPropertyName("funcionario_id")] public int FuncionarioId { get; set; }
    [JsonPropertyName("cliente_id")] public int ClienteId { get; set; }

    // Chave natural do caixa — caixa_funcoes_id é opcional (nullable no schema real),
    // é o que permite caixa 100% offline (ver SPEC-caixa.md).
    [JsonPropertyName("caixa_data")] public string? CaixaData { get; set; }
    [JsonPropertyName("caixa_turno")] public int? CaixaTurno { get; set; }
    [JsonPropertyName("caixa_funcoes_id")] public int? CaixaFuncoesId { get; set; }

    [JsonPropertyName("produtos")] public List<VendaProdutoRequestDto> Produtos { get; set; } = new();
    [JsonPropertyName("pagamentos")] public List<VendaPagamentoRequestDto> Pagamentos { get; set; } = new();
}

public class VendaProdutoRequestDto
{
    [JsonPropertyName("produto_id")] public int ProdutoId { get; set; }
    [JsonPropertyName("preco")] public decimal Preco { get; set; }
    [JsonPropertyName("quantidade")] public decimal Quantidade { get; set; }
    [JsonPropertyName("desconto_valor_item")] public decimal DescontoValorItem { get; set; }
    [JsonPropertyName("acrescimo_valor_item")] public decimal AcrescimoValorItem { get; set; }
}

public class VendaPagamentoRequestDto
{
    [JsonPropertyName("forma_pagamento_id")] public int FormaPagamentoId { get; set; }
    [JsonPropertyName("valor_pagamento")] public decimal ValorPagamento { get; set; }
}

public class VendaRespostaDto
{
    [JsonPropertyName("data")] public VendaRespostaDataDto? Data { get; set; }
}

public class VendaRespostaDataDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sales.Dtos;

// Subconjunto do schema de POST softauth/api/v2/vendas. Validado contra a API REAL (422): além do
// mínimo, ela EXIGE numero_documento, cancelada, bloqueada e produtos[].produto_empresa_grade_id
// (o resto que o Swagger lista é opcional/gerado pelo servidor: xml, numero_nfe, codigo_status…).
public class VendaRequestDto
{
    [JsonPropertyName("guid")] public string Guid { get; set; } = string.Empty;

    // Unix timestamp (inteiro), não string ISO — confirmado no schema real da API.
    [JsonPropertyName("data_hora")] public long DataHora { get; set; }

    [JsonPropertyName("empresa_id")] public int EmpresaId { get; set; }
    [JsonPropertyName("usuario_id")] public int UsuarioId { get; set; }
    [JsonPropertyName("funcionario_id")] public int FuncionarioId { get; set; }
    [JsonPropertyName("cliente_id")] public int ClienteId { get; set; }

    // "Número do pedido" (string no Swagger): gerado pelo PDV — ver NumeroDocumento (código do PDV + Venda.NumeroPedido).
    [JsonPropertyName("numero_documento")] public string NumeroDocumento { get; set; } = string.Empty;

    // Obrigatórios na API mesmo numa venda nova: uma venda que o PDV envia nasce não cancelada e
    // não bloqueada.
    [JsonPropertyName("cancelada")] public bool Cancelada { get; set; }
    [JsonPropertyName("bloqueada")] public bool Bloqueada { get; set; }

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
    // Dois ids distintos do mesmo produto (ex: 77 e 206) — ver Produto.ProdutoIdApi. Hipótese de
    // mapeamento a confirmar na primeira venda real: grade = `id` da listagem, produto = `produto_id`.
    [JsonPropertyName("produto_id")] public int ProdutoId { get; set; }
    [JsonPropertyName("produto_empresa_grade_id")] public int ProdutoEmpresaGradeId { get; set; }
    [JsonPropertyName("preco")] public decimal Preco { get; set; }

    // A API real deu 500 ao gravar o item ("Column 'preco_compra' cannot be null"): os campos que o
    // Swagger não marca como nullable precisam ir sempre — com valor neutro quando o PDV não tem o dado.
    [JsonPropertyName("preco_compra")] public decimal PrecoCompra { get; set; }
    [JsonPropertyName("comissao")] public decimal Comissao { get; set; }
    [JsonPropertyName("comissao_atendente")] public decimal ComissaoAtendente { get; set; }
    [JsonPropertyName("percentual_comissao_venda")] public decimal PercentualComissaoVenda { get; set; }
    [JsonPropertyName("composicao_automatica")] public bool ComposicaoAutomatica { get; set; }
    [JsonPropertyName("promocao_aplicada")] public bool PromocaoAplicada { get; set; }

    [JsonPropertyName("quantidade")] public decimal Quantidade { get; set; }
    [JsonPropertyName("desconto_valor_item")] public decimal DescontoValorItem { get; set; }
    [JsonPropertyName("acrescimo_valor_item")] public decimal AcrescimoValorItem { get; set; }
}

public class VendaPagamentoRequestDto
{
    // A API real deu 500 no financeiro ("Undefined index: api_nome_pagamento"): ela lê nome e código da
    // forma de pagamento no próprio item, além do id.
    [JsonPropertyName("api_nome_pagamento")] public string ApiNomePagamento { get; set; } = string.Empty;
    [JsonPropertyName("api_codigo_pagamento")] public string ApiCodigoPagamento { get; set; } = string.Empty;
    [JsonPropertyName("forma_pagamento_id")] public int FormaPagamentoId { get; set; }
    [JsonPropertyName("valor_pagamento")] public decimal ValorPagamento { get; set; }

    // Pagamento à vista = uma parcela com o valor total. A API real lê estes campos ao gravar a parcela
    // ("Undefined index: valor_parcela"); os demais campos de parcela/cartão do Swagger ficam de fora até
    // ela pedir.
    [JsonPropertyName("valor_parcela")] public decimal ValorParcela { get; set; }
    [JsonPropertyName("valor_recebido")] public decimal ValorRecebido { get; set; }
    [JsonPropertyName("parcelas")] public int Parcelas { get; set; } = 1;
    [JsonPropertyName("numero_parcela")] public string NumeroParcela { get; set; } = "1";
}

public class VendaRespostaDto
{
    [JsonPropertyName("data")] public VendaRespostaDataDto? Data { get; set; }
}

public class VendaRespostaDataDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

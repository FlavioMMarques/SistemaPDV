using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class ClienteApiDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    [JsonPropertyName("razao_social")] public string? RazaoSocial { get; set; }
    [JsonPropertyName("pessoa")] public string? Pessoa { get; set; }
    [JsonPropertyName("cpf_cnpj")] public string? CpfCnpj { get; set; }
    [JsonPropertyName("rg")] public string? Rg { get; set; }
    [JsonPropertyName("inscricao_estadual")] public string? InscricaoEstadual { get; set; }
    [JsonPropertyName("inscricao_municipal")] public string? InscricaoMunicipal { get; set; }
    [JsonPropertyName("contribuinte_icms")] public string? ContribuinteIcms { get; set; }
    [JsonPropertyName("indicador_finalidade")] public int IndicadorFinalidade { get; set; }
    [JsonPropertyName("bloqueado")] [JsonConverter(typeof(BooleanoFlexivelConverter))] public bool Bloqueado { get; set; }
    [JsonPropertyName("observacao")] public string? Observacao { get; set; }

    [JsonPropertyName("contato_nome")] public string? ContatoNome { get; set; }
    [JsonPropertyName("contato_ddd")] public string? ContatoDdd { get; set; }
    [JsonPropertyName("contato_telefone")] public string? ContatoTelefone { get; set; }
    [JsonPropertyName("contato_email")] public string? ContatoEmail { get; set; }

    [JsonPropertyName("cep")] public string? Cep { get; set; }
    [JsonPropertyName("endereco")] public string? Endereco { get; set; }
    [JsonPropertyName("numero")] public string? Numero { get; set; }
    [JsonPropertyName("complemento")] public string? Complemento { get; set; }
    [JsonPropertyName("bairro")] public string? Bairro { get; set; }
    [JsonPropertyName("ponto_referencia")] public string? PontoReferencia { get; set; }
    [JsonPropertyName("cidade")] public string? Cidade { get; set; }
    [JsonPropertyName("cidade_id")] public string? CidadeId { get; set; }
    [JsonPropertyName("uf")] public string? Uf { get; set; }

    [JsonPropertyName("tipo_cliente_id")] public string? TipoClienteId { get; set; }
    [JsonPropertyName("tipo_cliente_nome")] public string? TipoClienteNome { get; set; }
    [JsonPropertyName("funcionario_id")] public int? FuncionarioId { get; set; }
    [JsonPropertyName("funcionario_nome")] public string? FuncionarioNome { get; set; }

    [JsonPropertyName("tabela_preco")] public TabelaPrecoApiDto? TabelaPreco { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class TabelaPrecoApiDto
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("descricao")] public string? Descricao { get; set; }
}

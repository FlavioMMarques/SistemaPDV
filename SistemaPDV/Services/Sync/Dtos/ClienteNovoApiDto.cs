using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// Request de POST /softauth/api/v2/clientes/clientes — só o subconjunto que o
// cadastro simples do PDV preenche (nome + documento). Os blocos aninhados
// "endereco" e "contato" existem no Swagger mas não têm campos obrigatórios
// confirmados, então ficam de fora. Campos nulos são omitidos (não enviados como
// null): a API distingue "não informado" de "nulo" em alguns validadores.
//
// Este DTO é uma allowlist de propósito: só o que está declarado aqui sai do
// dispositivo. Nenhum campo interno da entidade Cliente (SyncStatus, Id local,
// UltimoErroSync...) tem como vazar por acidente.
public class ClienteNovoRequestDto
{
    [JsonPropertyName("pessoa")] public string Pessoa { get; set; } = "FISICA";
    [JsonPropertyName("nome")] public string Nome { get; set; } = string.Empty;

    [JsonPropertyName("cpf_cnpj")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CpfCnpj { get; set; }

    // O Swagger diz "obrigatório quando JURIDICA", mas a API real exige SEMPRE (coluna sem nulo): quem monta o corpo envia
    // o nome quando não há razão social.
    [JsonPropertyName("razao_social")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RazaoSocial { get; set; }

    // 9 = Não Contribuinte (caso comum de cliente de balcão) — default decidido na spec.
    [JsonPropertyName("contribuinte_icms")] public int ContribuinteIcms { get; set; } = 9;

    // 0 = Normal (1 é reservado pro placeholder "Consumidor Final").
    [JsonPropertyName("indicador_finalidade")] public int IndicadorFinalidade { get; set; }

    // Campos com valor padrão no Swagger, enviados explicitamente: a API real insere TODAS as colunas do cliente (mandando
    // nulo no que falta — foi assim que razao_social estourou o erro 1048), então não dá para confiar que o "default" do
    // Swagger seja aplicado quando o campo some.
    [JsonPropertyName("bloqueado")] public bool Bloqueado { get; set; }
    [JsonPropertyName("desativado")] public bool Desativado { get; set; }
    [JsonPropertyName("permitir_excluir")] public bool PermitirExcluir { get; set; } = true;
    [JsonPropertyName("limite_credito")] public decimal LimiteCredito { get; set; }

    // Só vai quando há telefone (a API exige nome + DDD + telefone dentro do "contato"). Cidade/UF e endereço não vão: a API
    // pede o CÓDIGO da cidade (c_cidade), que o app não tem.
    [JsonPropertyName("contato")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClienteNovoContatoDto? Contato { get; set; }
}

public class ClienteNovoContatoDto
{
    [JsonPropertyName("nome")] public string Nome { get; set; } = string.Empty;
    [JsonPropertyName("ddd")] public string Ddd { get; set; } = string.Empty;
    [JsonPropertyName("telefone")] public string Telefone { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }
}

public class ClienteNovoRespostaDto
{
    [JsonPropertyName("data")] public ClienteNovoRespostaDataDto? Data { get; set; }
}

public class ClienteNovoRespostaDataDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

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

    // Obrigatório pela API quando pessoa = JURIDICA.
    [JsonPropertyName("razao_social")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RazaoSocial { get; set; }

    // 9 = Não Contribuinte (caso comum de cliente de balcão) — default decidido na spec.
    [JsonPropertyName("contribuinte_icms")] public int ContribuinteIcms { get; set; } = 9;

    // 0 = Normal (1 é reservado pro placeholder "Consumidor Final").
    [JsonPropertyName("indicador_finalidade")] public int IndicadorFinalidade { get; set; }
}

public class ClienteNovoRespostaDto
{
    [JsonPropertyName("data")] public ClienteNovoRespostaDataDto? Data { get; set; }
}

public class ClienteNovoRespostaDataDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

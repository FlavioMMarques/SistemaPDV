using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class FuncionarioApiDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("nome")] public string? Nome { get; set; }
    [JsonPropertyName("cpf")] public string? Cpf { get; set; }
    [JsonPropertyName("desativado")] public bool Desativado { get; set; }
    [JsonPropertyName("supervisor")] public bool Supervisor { get; set; }
    [JsonPropertyName("observacao")] public string? Observacao { get; set; }

    [JsonPropertyName("usuario")] public UsuarioFuncionarioApiDto? Usuario { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class UsuarioFuncionarioApiDto
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }

    // Só entra na memória o tempo suficiente pra ser hasheado — nunca é persistido
    // como veio (ver CatalogSyncService.SincronizarFuncionariosAsync).
    [JsonPropertyName("pdv_key")] public string? PdvKey { get; set; }
}

using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// Formato REAL de GET .../softauth/api/v2/produtos/grupos (conferido em 2026-09-21, só leitura): envelope padrão de página
// (current_page/data/next_page_url), 50 por página; cada item tem dezenas de campos (restaurante, marketplace…) e o app só
// usa o id e o nome. Todos os grupos vistos tinham nome e nenhum tinha pai (parent_id nulo).
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class GrupoApiDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("nome")]
    public string? Nome { get; set; }
}

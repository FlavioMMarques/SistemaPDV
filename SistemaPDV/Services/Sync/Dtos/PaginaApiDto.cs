using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// Envelope de paginação comum aos endpoints de leitura da API (clientes, produtos,
// formas de pagamento, empresa, funcionários) — genérico no tipo do item da página.
public class PaginaApiDto<T>
{
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();

    [JsonPropertyName("next_page_url")]
    public string? NextPageUrl { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    // Nem todo endpoint devolve isso (ex: a variante não-paginada de forma-pagamento
    // não tem, mas a paginada tem) — por isso nullable.
    [JsonPropertyName("date_sync")]
    public long? DateSync { get; set; }
}

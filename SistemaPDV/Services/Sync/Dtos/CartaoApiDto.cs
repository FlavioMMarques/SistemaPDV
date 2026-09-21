using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// Formato REAL de GET .../financeiros/cartoes/page/N (conferido em 2026-09-20): TODOS os campos vêm como texto
// ("id":"15", "bandeira_id":"02", "taxa_administrativa":"0.01"), ao contrário do Swagger (integer/number). Os
// numéricos aceitam número OU texto; os códigos (bandeira_id) ficam como texto para não perder o zero à esquerda.
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class CartaoApiDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("credenciadora")]
    public string? Credenciadora { get; set; }

    [JsonPropertyName("nome")]
    public string? Nome { get; set; }

    [JsonPropertyName("bandeira_id")]
    [JsonConverter(typeof(TextoFlexivelConverter))]
    public string? BandeiraId { get; set; }

    [JsonPropertyName("bandeira_nome")]
    public string? BandeiraNome { get; set; }

    [JsonPropertyName("tipo")]
    public string? Tipo { get; set; }

    [JsonPropertyName("alias_cartao")]
    public string? AliasCartao { get; set; }

    [JsonPropertyName("dia")]
    public int? Dia { get; set; }

    [JsonPropertyName("parcelas")]
    public int? Parcelas { get; set; }

    [JsonPropertyName("taxa_administrativa")]
    public decimal? TaxaAdministrativa { get; set; }
}

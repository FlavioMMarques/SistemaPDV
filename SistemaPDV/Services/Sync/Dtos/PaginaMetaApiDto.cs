using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// Envelope de paginação de OUTRO estilo, usado pela rota de cartões: {code, message, human, data[], meta:{page:{current,
// prev, next, count}}, date_sync} — sem `current_page`/`next_page_url` como o de PaginaApiDto. `next` é o NÚMERO da
// próxima página (null na última).
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class PaginaMetaApiDto<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public MetaApiDto? Meta { get; set; }

    public class MetaApiDto
    {
        [JsonPropertyName("page")]
        public PaginaInfoApiDto? Page { get; set; }
    }

    public class PaginaInfoApiDto
    {
        [JsonPropertyName("current")]
        public int Current { get; set; }

        [JsonPropertyName("next")]
        public int? Next { get; set; }
    }
}

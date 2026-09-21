using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Caixa.Dtos;

// Resposta de GET .../financeiro/caixa-funcoes (lista de caixas, últimos 7 dias ou o intervalo pedido):
// { "data": [ { "id": 24, "operador_id": 2, "turno": 2, "data_caixa": "2025-09-29", "data_abertura": "2025-09-29 08:00:00",
//   "data_fechamento": null, ... } ], "current_page": 1, "last_page": 1, "per_page": 20, "total": 1 }.
// Os números podem chegar como texto em outras rotas da API, então todos aceitam as duas formas.
public class CaixaListaApiDto
{
    [JsonPropertyName("data")] public List<CaixaListaItemApiDto> Data { get; set; } = new();

    [JsonPropertyName("current_page")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("last_page")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? LastPage { get; set; }

    [JsonPropertyName("total")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Total { get; set; }
}

public class CaixaListaItemApiDto
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }

    // Id do funcionário na API (Funcionario.IdExterno): quem o caixa pertence.
    [JsonPropertyName("operador_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? OperadorId { get; set; }

    [JsonPropertyName("turno")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Turno { get; set; }

    // "2025-09-29" e "2025-09-29 08:00:00": texto, lido com a cultura invariante na hora de mostrar.
    [JsonPropertyName("data_caixa")] public string? DataCaixa { get; set; }
    [JsonPropertyName("data_abertura")] public string? DataAbertura { get; set; }
    [JsonPropertyName("data_fechamento")] public string? DataFechamento { get; set; }

    [JsonPropertyName("api_device_id")] public string? ApiDeviceId { get; set; }
}

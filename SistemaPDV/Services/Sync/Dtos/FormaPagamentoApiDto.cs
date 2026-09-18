using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// A API costuma devolver números como string em alguns campos, então aceita ler
// tanto número quanto string (mesma tolerância usada no projeto de referência).
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class FormaPagamentoApiDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("nome")]
    public string? Nome { get; set; }

    [JsonPropertyName("tipo")]
    public string? Tipo { get; set; }

    [JsonPropertyName("padrao")]
    public bool Padrao { get; set; }

    [JsonPropertyName("codigo_nfce")]
    public string? CodigoNfce { get; set; }

    [JsonPropertyName("codigo_transacao_sitef")]
    public string? CodigoTransacaoSitef { get; set; }

    [JsonPropertyName("carteira_digital")]
    public bool CarteiraDigital { get; set; }

    [JsonPropertyName("ordem")]
    public int Ordem { get; set; }

    [JsonPropertyName("pdv_pos")]
    public bool PdvPos { get; set; }

    [JsonPropertyName("pre_venda")]
    public bool PreVenda { get; set; }

    [JsonPropertyName("atalho_numero")]
    public string? AtalhoNumero { get; set; }

    [JsonPropertyName("permissao_supervisor")]
    public bool PermissaoSupervisor { get; set; }
}

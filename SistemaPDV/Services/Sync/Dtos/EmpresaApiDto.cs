using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class EmpresaApiDto
{
    [JsonPropertyName("empresa_id")] public int EmpresaId { get; set; }
    [JsonPropertyName("empresa_razao_social")] public string? EmpresaRazaoSocial { get; set; }
    [JsonPropertyName("empresa_fantasia")] public string? EmpresaFantasia { get; set; }
    [JsonPropertyName("empresa_cnpj")] public string? EmpresaCnpj { get; set; }
    [JsonPropertyName("empresa_email")] public string? EmpresaEmail { get; set; }

    [JsonPropertyName("empresa_cep")] public string? EmpresaCep { get; set; }
    [JsonPropertyName("empresa_endereco")] public string? EmpresaEndereco { get; set; }
    [JsonPropertyName("empresa_numero")] public string? EmpresaNumero { get; set; }
    [JsonPropertyName("empresa_complemento")] public string? EmpresaComplemento { get; set; }
    [JsonPropertyName("empresa_bairro")] public string? EmpresaBairro { get; set; }
    [JsonPropertyName("empresa_cidade")] public string? EmpresaCidade { get; set; }
    [JsonPropertyName("empresa_uf")] public string? EmpresaUf { get; set; }

    [JsonPropertyName("empresa_modulo_fiscal")] public bool EmpresaModuloFiscal { get; set; }
    [JsonPropertyName("empresa_nfce_serie")] public int EmpresaNfceSerie { get; set; }
    [JsonPropertyName("empresa_nfce_numero_caixa")] public int EmpresaNfceNumeroCaixa { get; set; }
    [JsonPropertyName("empresa_nfce_ambiente")] public int EmpresaNfceAmbiente { get; set; }
    [JsonPropertyName("empresa_nfce_modelo")] public int EmpresaNfceModelo { get; set; }
    [JsonPropertyName("empresa_nfce_proximo_numero")] public int EmpresaNfceProximoNumero { get; set; }

    // Sensíveis — nunca chegam a ser gravados como vieram (ver
    // CatalogSyncService.SincronizarEmpresaAsync e docs/APRENDIZADOS.md).
    [JsonPropertyName("empresa_certificado")] public string? EmpresaCertificado { get; set; }
    [JsonPropertyName("empresa_certificado_senha")] public string? EmpresaCertificadoSenha { get; set; }
}

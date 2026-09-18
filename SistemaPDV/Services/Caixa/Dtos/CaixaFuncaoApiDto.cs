using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Caixa.Dtos;

// Envelope de sucesso de POST .../caixa-funcoes/abrir e /fechar — só o que
// precisamos (o id remoto) fica modelado; o resto do objeto "success" (device_client_id
// etc.) não tem uso no PDV ainda.
public class CaixaFuncaoRespostaDto
{
    [JsonPropertyName("data")] public CaixaFuncaoDataDto? Data { get; set; }
}

public class CaixaFuncaoDataDto
{
    [JsonPropertyName("msg")] public string? Msg { get; set; }
    [JsonPropertyName("success")] public CaixaFuncaoSuccessDto? Success { get; set; }
}

public class CaixaFuncaoSuccessDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
}

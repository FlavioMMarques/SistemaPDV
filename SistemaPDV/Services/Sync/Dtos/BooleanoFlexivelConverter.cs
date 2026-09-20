using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// A API SoftcomShop não é consistente com booleanos: `bloqueado` e `vender` chegam como
// true/false na API real, mas a documentação e versões anteriores mostravam 0/1 ou "0"/"1".
// Aceita as quatro formas em vez de quebrar a sincronização inteira por um flag.
public class BooleanoFlexivelConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Number => reader.GetDouble() != 0,
        JsonTokenType.String => LerTexto(reader.GetString()),
        _ => throw new JsonException($"Valor booleano inesperado ({reader.TokenType})."),
    };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);

    private static bool LerTexto(string? texto) => texto?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "sim" or "s" => true,
        "0" or "false" or "nao" or "não" or "n" or "" or null => false,
        _ => throw new JsonException($"Valor booleano inesperado (\"{texto}\")."),
    };
}

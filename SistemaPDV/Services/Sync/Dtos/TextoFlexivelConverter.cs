using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SistemaPDV.Services.Sync.Dtos;

// Campos que são CÓDIGO/texto mas que o Swagger descreve como número: `bandeira_id` chega como "02" (com zero à
// esquerda, que um inteiro perderia) mas pode chegar como número em outra empresa/versão. Lê os dois como texto: o
// texto vem exatamente como veio; um número vira o seu texto invariante (2 -> "2"); null vira null.
public class TextoFlexivelConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var inteiro)
            ? inteiro.ToString(CultureInfo.InvariantCulture)
            : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
        JsonTokenType.Null => null,
        _ => throw new JsonException($"Texto inesperado ({reader.TokenType})."),
    };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

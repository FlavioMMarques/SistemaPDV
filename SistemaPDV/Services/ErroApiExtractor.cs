using System.Collections.Generic;
using System.Text.Json;

namespace SistemaPDV.Services;

// Formato de erro consistente na maior parte da API SoftcomShop:
// { "errors": { "campo": ["mensagem"], "message"?: ["mensagem geral"] } }.
// Compartilhado por CaixaSyncService, VendaSyncService e a sincronização de
// cliente — mesmo parsing, sem risco de tradução de LINQ (é string pura).
//
// A resposta da API é dado NÃO confiável: o formato pode mudar, um proxy pode
// devolver HTML, um erro de servidor pode despejar um stack trace. Por isso este
// método nunca lança e nunca devolve mais que TamanhoMaximo caracteres — o texto
// acaba gravado em UltimoErroSync no banco local (revisão de segurança, Task 48).
public static class ErroApiExtractor
{
    public const int TamanhoMaximo = 300;

    public static string Extrair(string conteudo)
    {
        try
        {
            using var json = JsonDocument.Parse(conteudo);
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("errors", out var errors))
            {
                var mensagens = new List<string>();
                ColetarMensagens(errors, mensagens, null);

                if (mensagens.Count > 0)
                    return Limitar(string.Join(" ", mensagens));
            }
        }
        catch (JsonException)
        {
            // cai no fallback abaixo
        }

        return Limitar(conteudo);
    }

    // Aceita os formatos que a API pode devolver em "errors": objeto de arrays
    // (o esperado), objeto com texto solto, array direto ou texto — qualquer coisa
    // fora disso é simplesmente ignorada, nunca vira exceção.
    private static void ColetarMensagens(JsonElement elemento, List<string> mensagens, string? campo)
    {
        switch (elemento.ValueKind)
        {
            case JsonValueKind.String:
                if (elemento.GetString() is { Length: > 0 } texto)
                    mensagens.Add(campo is null ? texto : $"{campo}: {texto}");
                break;
            case JsonValueKind.Array:
                foreach (var item in elemento.EnumerateArray())
                    ColetarMensagens(item, mensagens, campo);
                break;
            case JsonValueKind.Object:
                foreach (var propriedade in elemento.EnumerateObject())
                    ColetarMensagens(propriedade.Value, mensagens, propriedade.Name == "message" ? campo : propriedade.Name);
                break;
        }
    }

    private static string Limitar(string texto) =>
        texto.Length <= TamanhoMaximo ? texto : texto[..TamanhoMaximo];
}

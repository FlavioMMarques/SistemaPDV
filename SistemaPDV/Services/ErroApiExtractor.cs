using System.Collections.Generic;
using System.Text.Json;

namespace SistemaPDV.Services;

// Formato de erro consistente na maior parte da API SoftcomShop:
// { "errors": { "campo": ["mensagem"], "message"?: ["mensagem geral"] } }.
// Compartilhado por CaixaSyncService e VendaSyncService — terceiro uso do mesmo
// parsing, e sem risco de tradução de LINQ (é string pura), diferente do upsert
// genérico que decidimos não compartilhar no catalog-sync.
public static class ErroApiExtractor
{
    public static string Extrair(string conteudo)
    {
        try
        {
            using var json = JsonDocument.Parse(conteudo);
            if (json.RootElement.TryGetProperty("errors", out var errors))
            {
                var mensagens = new List<string>();
                foreach (var campo in errors.EnumerateObject())
                {
                    foreach (var item in campo.Value.EnumerateArray())
                    {
                        if (item.GetString() is { } texto)
                            mensagens.Add(texto);
                    }
                }

                if (mensagens.Count > 0)
                    return string.Join(" ", mensagens);
            }
        }
        catch (JsonException)
        {
            // cai no fallback abaixo
        }

        return conteudo;
    }
}

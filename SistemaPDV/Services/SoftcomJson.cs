using System.Text.Json;

namespace SistemaPDV.Services;

// Opções de (de)serialização compartilhadas por todo mundo que fala com a API
// SoftcomShop — antes da limpeza, três classes declaravam a mesma instância
// separadamente (SoftcomApiClient, CaixaSyncService, VendaSyncService).
public static class SoftcomJson
{
    public static readonly JsonSerializerOptions Opcoes = new() { PropertyNameCaseInsensitive = true };

    // A resposta da API é dado não confiável: um portal cativo de wi-fi, um proxy ou
    // um erro de gateway devolve 200 com HTML, e Deserialize lançaria JsonException
    // no meio de uma sincronização (deixando a entidade sem marcar e, no lote,
    // travando o resto). Aqui "não é o JSON esperado" vira null — quem chama já
    // trata null como "a resposta não trouxe o que precisava".
    public static T? TentarDesserializar<T>(string conteudo) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(conteudo, Opcoes);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

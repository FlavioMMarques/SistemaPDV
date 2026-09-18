using System.Text.Json;

namespace SistemaPDV.Services;

// Opções de (de)serialização compartilhadas por todo mundo que fala com a API
// SoftcomShop — antes da limpeza, três classes declaravam a mesma instância
// separadamente (SoftcomApiClient, CaixaSyncService, VendaSyncService).
public static class SoftcomJson
{
    public static readonly JsonSerializerOptions Opcoes = new() { PropertyNameCaseInsensitive = true };
}

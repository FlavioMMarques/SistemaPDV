using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Sync;

public class SoftcomAuthService
{
    private readonly HttpClient httpClient;
    private readonly SegredoProtector segredoProtector;

    public SoftcomAuthService(HttpClient httpClient, SegredoProtector segredoProtector)
    {
        this.httpClient = httpClient;
        this.segredoProtector = segredoProtector;
    }

    // O link cadastrado no SoftcomShop já traz client_id, empresa_name e empresa_cnpj
    // na query string; falta só informar qual dispositivo está pedindo o cadastro
    // (&device_id=) pra API devolver o client_secret dele.
    public async Task<(bool Sucesso, string Mensagem, string? ClienteSecret)> ObterClienteSecretAsync(
        string link, string nomeDispositivo, CancellationToken ct = default)
    {
        try
        {
            var url = $"{link}&device_id={Uri.EscapeDataString(nomeDispositivo)}";
            using var resposta = await httpClient.GetAsync(url, ct);
            var conteudo = await resposta.Content.ReadAsStringAsync(ct);

            if (!resposta.IsSuccessStatusCode)
                return (false, $"Falha na conexão: {(int)resposta.StatusCode} {resposta.ReasonPhrase}. {conteudo}", null);

            using var json = JsonDocument.Parse(conteudo);
            if (!json.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("client_secret", out var clienteSecretElemento) ||
                clienteSecretElemento.GetString() is not { Length: > 0 } clienteSecret)
            {
                return (false, $"A resposta da API não trouxe o client_secret: {conteudo}", null);
            }

            return (true, "Cliente secret obtido com sucesso.", clienteSecret);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, $"Falha na conexão: {ex.Message}", null);
        }
    }

    // Só esse método toca SegredoProtector (DPAPI) — a restrição de plataforma fica
    // aqui, não na classe inteira, pra não propagar Windows-only pra quem só chama
    // ObterClienteSecretAsync ou ExtrairDominio (puro parsing de URI, sem DPAPI).
    [SupportedOSPlatform("windows")]
    public async Task<(bool Sucesso, string Mensagem, string? AccessToken)> ObterTokenAsync(
        ConfiguracaoSincronizacao configuracao, CancellationToken ct = default)
    {
        try
        {
            var clienteSecret = segredoProtector.Desproteger(configuracao.ApiClienteSecretProtegido);

            var corpo = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = configuracao.ApiClienteId ?? string.Empty,
                ["client_secret"] = clienteSecret ?? string.Empty,
            });

            using var resposta = await httpClient.PostAsync(MontarUrlToken(configuracao.UrlApi), corpo, ct);
            var conteudo = await resposta.Content.ReadAsStringAsync(ct);

            if (!resposta.IsSuccessStatusCode)
                return (false, $"Falha ao obter token: {(int)resposta.StatusCode} {resposta.ReasonPhrase}. {conteudo}", null);

            using var json = JsonDocument.Parse(conteudo);
            var elemento = json.RootElement.TryGetProperty("data", out var data) ? data : json.RootElement;

            if ((!elemento.TryGetProperty("token", out var tokenElemento) &&
                 !elemento.TryGetProperty("access_token", out tokenElemento)) ||
                tokenElemento.GetString() is not { Length: > 0 } accessToken)
            {
                return (false, $"A resposta não trouxe o token: {conteudo}", null);
            }

            return (true, "Token obtido com sucesso.", accessToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, $"Falha ao obter token: {ex.Message}", null);
        }
    }

    // O domínio muda de cliente pra cliente (é o mesmo domínio da UrlApi configurada);
    // só o caminho de cada rota é fixo.
    public static string ExtrairDominio(string? urlApi) => new Uri(urlApi ?? string.Empty).GetLeftPart(UriPartial.Authority);

    private static string MontarUrlToken(string? urlApi) => $"{ExtrairDominio(urlApi)}/softauth/authentication/token";
}

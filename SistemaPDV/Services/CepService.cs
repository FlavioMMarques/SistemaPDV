using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SistemaPDV.Services;

// O endereço que um CEP devolve. O "complemento" do ViaCEP é uma faixa de numeração ("de 5242 ao fim - lado par"), não o complemento
// do cliente (sala, apto, bloco): não é lido. CodigoCidade é o código da cidade no IBGE (7 dígitos) — o "c_cidade" que a API do
// SoftcomShop pede no endereço do cliente novo. Logradouro/Bairro vêm vazios em CEP "geral" (cidade pequena, com um CEP só).
public record EnderecoCep(string Cep, string Logradouro, string Bairro, string Cidade, string Uf, string CodigoCidade);

public class ResultadoCep
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public EnderecoCep? Endereco { get; private init; }

    public static ResultadoCep ComSucesso(EnderecoCep endereco) => new() { Sucesso = true, Endereco = endereco };
    public static ResultadoCep ComFalha(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

// Consulta de CEP no ViaCEP (https://viacep.com.br — serviço público e gratuito, sem chave). Só serve de CONVENIÊNCIA: o app é
// offline-first e o cadastro de cliente funciona sem isto (o operador digita o endereço; sem consulta não há código de cidade, então
// a cidade fica só local). O único dado que sai do aparelho é o CEP (dado público, sem nome nem documento), por HTTPS.
//
// A resposta é dado NÃO confiável: nunca lança, e cada campo é aparado e limitado (vai para a tela e para o banco).
public class CepService
{
    private const string Endereco = "https://viacep.com.br/ws";
    private const int TamanhoMaximoCampo = 150;
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(8);

    private readonly HttpClient httpClient;

    public CepService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    // "58039-000", "58039000" ou " 58.039-000 " → "58039000". Qualquer outra coisa (letras, tamanho errado) = falso.
    public static bool TentarNormalizar(string? texto, out string cep)
    {
        cep = string.Empty;
        if (string.IsNullOrWhiteSpace(texto) || !texto.All(c => char.IsAsciiDigit(c) || "-. ".Contains(c)))
            return false;

        var digitos = DocumentoValidator.SoDigitos(texto);
        if (digitos.Length != 8)
            return false;

        cep = digitos;
        return true;
    }

    public async Task<ResultadoCep> BuscarAsync(string? cepDigitado, CancellationToken ct = default)
    {
        if (!TentarNormalizar(cepDigitado, out var cep))
            return ResultadoCep.ComFalha("CEP inválido — informe os 8 dígitos (ex: 58039-000).");

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(Prazo);

        string conteudo;
        try
        {
            using var resposta = await httpClient.GetAsync($"{Endereco}/{cep}/json/", limite.Token);
            if (!resposta.IsSuccessStatusCode)
                return ResultadoCep.ComFalha($"A consulta de CEP falhou ({(int)resposta.StatusCode}). Preencha o endereço manualmente.");

            conteudo = await resposta.Content.ReadAsStringAsync(limite.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Sem internet, prazo estourado ou serviço fora do ar: o cadastro segue possível sem a consulta.
            return ResultadoCep.ComFalha("Não foi possível consultar o CEP (sem conexão?). Preencha o endereço manualmente.");
        }

        return Interpretar(cep, conteudo);
    }

    private static ResultadoCep Interpretar(string cep, string conteudo)
    {
        try
        {
            using var documento = JsonDocument.Parse(conteudo);
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return ResultadoCep.ComFalha("Resposta inesperada da consulta de CEP. Preencha o endereço manualmente.");

            // CEP que não existe: 200 com { "erro": true } (ou "true", em versões antigas).
            if (raiz.TryGetProperty("erro", out var erro) && (erro.ValueKind == JsonValueKind.True || erro.ToString().Equals("true", StringComparison.OrdinalIgnoreCase)))
                return ResultadoCep.ComFalha("CEP não encontrado — confira os dígitos ou preencha o endereço manualmente.");

            var cidade = Campo(raiz, "localidade");
            var uf = Campo(raiz, "uf").ToUpperInvariant();
            if (cidade.Length == 0)
                return ResultadoCep.ComFalha("O CEP não trouxe a cidade. Preencha o endereço manualmente.");

            // O código do IBGE só vale com 7 dígitos; qualquer outra coisa é descartada (a cidade segue, sem código).
            var ibge = DocumentoValidator.SoDigitos(Campo(raiz, "ibge"));
            return ResultadoCep.ComSucesso(new EnderecoCep(
                cep, Campo(raiz, "logradouro"), Campo(raiz, "bairro"),
                cidade, uf.Length == 2 ? uf : string.Empty, ibge.Length == 7 ? ibge : string.Empty));
        }
        catch (JsonException)
        {
            return ResultadoCep.ComFalha("Resposta inesperada da consulta de CEP. Preencha o endereço manualmente.");
        }
    }

    private static string Campo(JsonElement objeto, string nome)
    {
        if (!objeto.TryGetProperty(nome, out var valor) || valor.ValueKind != JsonValueKind.String)
            return string.Empty;

        var texto = valor.GetString()?.Trim() ?? string.Empty;
        return texto.Length <= TamanhoMaximoCampo ? texto : texto[..TamanhoMaximoCampo];
    }
}

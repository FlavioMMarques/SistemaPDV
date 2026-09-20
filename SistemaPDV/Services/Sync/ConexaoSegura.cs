using System;

namespace SistemaPDV.Services.Sync;

// Guarda única de "pra onde é seguro mandar isso": HTTPS sempre, exceto loopback
// (localhost/127.0.0.1/::1 — a API de desenvolvimento roda em http://localhost:73).
// Vale pra tudo que sai com credencial ou dado pessoal: o client_secret vai no corpo
// do pedido de token, o access token vai em todo request e o cadastro de cliente leva
// CPF/CNPJ — em http:// qualquer um na mesma rede lê os três (revisão de segurança,
// Task 48). Decidido pelo Uri.IsLoopback, nunca por "a string começa com localhost":
// http://localhost.evil.com e http://localhost@evil.com NÃO são loopback.
public static class ConexaoSegura
{
    public const string MensagemRecusa =
        "A URL da API não usa HTTPS — requisição não enviada para não expor credenciais e dados pessoais.";

    public static bool Permitida(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}

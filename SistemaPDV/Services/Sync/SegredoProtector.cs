using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace SistemaPDV.Services.Sync;

// DPAPI (Windows-only): criptografa amarrado ao usuário do Windows logado, sem
// precisar gerenciar chave própria. Usado pro client_secret e, mais adiante, pelo
// certificado da empresa — segredos que precisam ser recuperados de volta (ao
// contrário do pdv_key, que só precisa ser comparado — ver docs/APRENDIZADOS.md).
//
// [SupportedOSPlatform] não roda nenhuma checagem em tempo de execução — é só uma
// anotação que declara a restrição pro analisador de compatibilidade de plataforma
// do .NET (CA1416), que senão reclamaria em toda chamada a ProtectedData.
[SupportedOSPlatform("windows")]
public class SegredoProtector
{
    public string? Proteger(string? valorPuro)
    {
        if (string.IsNullOrEmpty(valorPuro))
            return valorPuro;

        var protegido = ProtectedData.Protect(Encoding.UTF8.GetBytes(valorPuro), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protegido);
    }

    // Aceita um valor que nunca foi protegido (ex: dado gravado antes desse esquema
    // existir) devolvendo-o como veio, em vez de lançar exceção.
    public string? Desproteger(string? valorProtegido)
    {
        if (string.IsNullOrEmpty(valorProtegido))
            return valorProtegido;

        try
        {
            var puro = ProtectedData.Unprotect(Convert.FromBase64String(valorProtegido), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(puro);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return valorProtegido;
        }
    }
}

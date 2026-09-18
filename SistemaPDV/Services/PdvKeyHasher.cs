using System;
using System.Security.Cryptography;
using System.Text;

namespace SistemaPDV.Services;

// SHA-256: suficiente pra uma chave curta de operação local que só precisa ser
// COMPARADA, nunca recuperada (ver docs/APRENDIZADOS.md e SPEC-caixa.md). Usado por
// catalog-sync (grava o hash ao sincronizar o funcionário) e por caixa (compara o
// hash no login) — extraído pra um lugar só assim que apareceu o segundo consumidor,
// porque duplicar lógica de hash de segurança é arriscado: se um dia mudar num lugar
// só, login e sincronização ficam dessincronizados sem erro nenhum na hora.
public static class PdvKeyHasher
{
    public static string? Hash(string? pdvKey)
    {
        if (string.IsNullOrEmpty(pdvKey))
            return null;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(pdvKey));
        return Convert.ToHexString(hash);
    }
}

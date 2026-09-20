using System.Text.RegularExpressions;

namespace SistemaPDV.Services;

// A API SoftcomShop NÃO devolve a pdv_key do operador: devolve um hash bcrypt dela
// ("$2y$10$…", 60 caracteres — descoberto ao sincronizar contra a API real, 2026-09-20).
// Antes o app achava que recebia a chave em claro e guardava SHA-256 dela; comparar
// SHA-256(digitado) com SHA-256(hash bcrypt) nunca casa, então nenhuma chave passava no
// login. Agora o hash bcrypt é guardado COMO VEM (já é um verificador: não dá pra recuperar
// a chave dele) e a chave digitada é conferida contra ele com bcrypt.
//
// Usado por catalog-sync (decide o que gravar) e por caixa (confere no login) — um lugar
// só, pra os dois nunca discordarem sobre o formato.
public static partial class PdvKeyHasher
{
    // $2a$/$2b$/$2x$/$2y$ + custo de 2 dígitos + $ + 53 caracteres (salt+hash em base64 do bcrypt).
    [GeneratedRegex(@"^\$2[abxy]\$\d{2}\$[./A-Za-z0-9]{53}$")]
    private static partial Regex FormatoBcrypt();

    public static bool EhHashBcrypt(string? valor) => valor is not null && FormatoBcrypt().IsMatch(valor);

    // Nunca lança: hash nulo, em claro ou no formato antigo (SHA-256) simplesmente não confere.
    public static bool Verificar(string? pdvKeyDigitada, string? hashBcrypt)
    {
        if (string.IsNullOrEmpty(pdvKeyDigitada) || !EhHashBcrypt(hashBcrypt))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(pdvKeyDigitada, hashBcrypt);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}

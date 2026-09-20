using System.Globalization;

namespace SistemaPDV.Services;

// Leitura dos valores que o operador digita (troco, pagamento, apuração, quantidade). O app lia com
// NumberStyles.Number + cultura invariante — modo em que a VÍRGULA é separador de milhar: "10,50" virava 1050
// e "0,5" (kg) virava 5. Aqui vírgula OU ponto valem como decimal, e o que for ambíguo é RECUSADO em vez de
// adivinhado (dinheiro errado em silêncio é pior que pedir pra digitar de novo).
public static class ValorMonetario
{
    private static readonly CultureInfo Brasil = CultureInfo.GetCultureInfo("pt-BR");

    // casasDecimais: 2 pra dinheiro, 3 pra quantidade (venda por peso). "1.234" com 3 casas é ambíguo
    // (milhar ou fração?) — em dinheiro é recusado; em quantidade vale 1,234.
    public static bool TentarLer(string? texto, out decimal valor, int casasDecimais = 2)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        var t = texto.Trim();
        var temVirgula = t.Contains(',');
        var temPonto = t.Contains('.');

        if (temVirgula && temPonto)
        {
            // "1.234,56" (pt-BR) ou "1,234.56" (en): o ÚLTIMO separador é o decimal, o outro é milhar.
            t = t.LastIndexOf(',') > t.LastIndexOf('.')
                ? t.Replace(".", string.Empty).Replace(',', '.')
                : t.Replace(",", string.Empty);
        }
        else if (temVirgula)
        {
            t = t.Replace(',', '.');
        }

        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out valor))
            return false;

        // Mais casas do que o permitido = provavelmente milhar ("1.234") ou erro de digitação.
        if (valor != decimal.Round(valor, casasDecimais))
        {
            valor = 0;
            return false;
        }

        return true;
    }

    // Como o operador brasileiro lê: "35,00".
    public static string Formatar(decimal valor) => valor.ToString("F2", Brasil);
}

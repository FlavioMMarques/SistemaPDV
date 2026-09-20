using System.Globalization;
using System.Linq;

namespace SistemaPDV.Services.Sales;

// numero_documento da API é único POR EMPRESA, e uma empresa pode ter vários PDVs gerando número offline (sem
// combinar entre si). Decisão do usuário (2026-09-20): cada dispositivo tem um código curto e o número enviado
// vira "<código>-<sequencial com 6 dígitos>" (ex: "02-000045"). O sequencial local (#N da tela) não muda.
public static class NumeroDocumento
{
    public const int TamanhoMaximoCodigo = 6;

    // Sem código configurado o número segue puro ("45"): é o comportamento de antes, correto para uma empresa com
    // um PDV só, e não muda os números já enviados.
    public static string Formatar(string? codigoPdv, int numeroPedido)
    {
        var numero = numeroPedido.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(codigoPdv)
            ? numero
            : $"{codigoPdv}-{numeroPedido.ToString("D6", CultureInfo.InvariantCulture)}";
    }

    // Aceita até 6 letras/dígitos ASCII (vira maiúsculo); vazio = sem código. Recusa hífen e espaço: o hífen é o
    // separador, e assim "A-1" + "-000001" nunca gera o mesmo texto que outro par (código, número).
    public static bool TentarNormalizarCodigo(string? texto, out string? codigo)
    {
        var limpo = texto?.Trim() ?? string.Empty;
        if (limpo.Length == 0)
        {
            codigo = null;
            return true;
        }

        if (limpo.Length > TamanhoMaximoCodigo || !limpo.All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')))
        {
            codigo = null;
            return false;
        }

        codigo = limpo.ToUpperInvariant();
        return true;
    }
}

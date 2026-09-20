using System.Linq;

namespace SistemaPDV.Services;

// Validação de CPF/CNPJ pelos dígitos verificadores, antes de mandar o documento
// pra fora. Sem isso, um documento digitado errado vira 422 da API a cada ciclo do
// timer (o cliente fica em FalhaSync e é elegível pra reenvio) — barulho pra API e
// um cliente que nunca sincroniza sem ninguém saber por quê.
public static class DocumentoValidator
{
    public static string SoDigitos(string? texto) =>
        texto is null ? string.Empty : new string(texto.Where(char.IsAsciiDigit).ToArray());

    // Só para exibir: 11 dígitos -> 000.000.000-00, 14 -> 00.000.000/0000-00. Qualquer
    // outra coisa (vazio, tamanho estranho vindo da API) volta como está.
    public static string? Formatar(string? documento)
    {
        var digitos = SoDigitos(documento);
        return digitos.Length switch
        {
            11 => $"{digitos[..3]}.{digitos[3..6]}.{digitos[6..9]}-{digitos[9..]}",
            14 => $"{digitos[..2]}.{digitos[2..5]}.{digitos[5..8]}/{digitos[8..12]}-{digitos[12..]}",
            _ => documento,
        };
    }

    public static bool CpfValido(string? cpf)
    {
        var digitos = SoDigitos(cpf);
        if (digitos.Length != 11 || TodosIguais(digitos))
            return false;

        return DigitoVerificador(digitos, 9, 10) == digitos[9] - '0'
            && DigitoVerificador(digitos, 10, 11) == digitos[10] - '0';
    }

    public static bool CnpjValido(string? cnpj)
    {
        var digitos = SoDigitos(cnpj);
        if (digitos.Length != 14 || TodosIguais(digitos))
            return false;

        int[] pesos1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        int[] pesos2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        return DigitoCnpj(digitos, pesos1) == digitos[12] - '0'
            && DigitoCnpj(digitos, pesos2) == digitos[13] - '0';
    }

    // CPF: pesos decrescentes a partir de (quantidade + 1) sobre os primeiros dígitos.
    private static int DigitoVerificador(string digitos, int quantidade, int pesoInicial)
    {
        var soma = 0;
        for (var i = 0; i < quantidade; i++)
            soma += (digitos[i] - '0') * (pesoInicial - i);

        var resto = soma * 10 % 11;
        return resto == 10 ? 0 : resto;
    }

    private static int DigitoCnpj(string digitos, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++)
            soma += (digitos[i] - '0') * pesos[i];

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    // 111.111.111-11 etc. passam na conta dos dígitos mas não são documentos reais.
    private static bool TodosIguais(string digitos) => digitos.All(c => c == digitos[0]);
}

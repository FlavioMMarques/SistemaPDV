namespace SistemaPDV.Tests;

// A API SoftcomShop devolve a pdv_key JÁ como hash bcrypt (nunca a chave em claro); os
// testes geram o mesmo formato. Custo 4 (o mínimo) só pra a suíte não ficar lenta — em
// produção o custo é o que a API mandar (10).
public static class PdvKeyTeste
{
    public static string Hash(string pdvKey) => BCrypt.Net.BCrypt.HashPassword(pdvKey, 4);
}

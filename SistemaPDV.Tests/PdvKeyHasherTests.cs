using SistemaPDV.Services;

namespace SistemaPDV.Tests;

public class PdvKeyHasherTests
{
    [Fact]
    public void ChaveCertaVerificaContraOHashBcrypt()
    {
        var hash = PdvKeyTeste.Hash("1234");

        Assert.True(PdvKeyHasher.Verificar("1234", hash));
    }

    [Fact]
    public void ChaveErradaNaoVerifica()
    {
        var hash = PdvKeyTeste.Hash("1234");

        Assert.False(PdvKeyHasher.Verificar("4321", hash));
        Assert.False(PdvKeyHasher.Verificar("1234 ", hash));
    }

    [Fact]
    public void VerificaContraUmHashRealNoFormatoDaApi()
    {
        // Formato observado na API real: $2y$10$ + 53 caracteres (60 no total) — o prefixo
        // 2y (do PHP/Laravel) tem que ser aceito, não só o 2a/2b do .NET.
        var hashPhp = "$2y$" + PdvKeyTeste.Hash("1234")[4..];   // mesmo hash, prefixo 2y (ASCII: 2a e 2y são compatíveis)
        Assert.True(PdvKeyHasher.EhHashBcrypt(hashPhp));
        Assert.True(PdvKeyHasher.Verificar("1234", hashPhp));
        Assert.False(PdvKeyHasher.Verificar("4321", hashPhp));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ChaveNulaOuVaziaNuncaVerifica(string? digitada)
    {
        Assert.False(PdvKeyHasher.Verificar(digitada, PdvKeyTeste.Hash("1234")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]                                    // chave em claro
    [InlineData("A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2")]   // SHA-256 hex (formato antigo)
    public void HashInvalidoOuAntigoNuncaVerificaENaoLanca(string? hash)
    {
        Assert.False(PdvKeyHasher.Verificar("1234", hash));
    }

    [Theory]
    [InlineData("$2y$10$abcdefghijklmnopqrstuuOwl3qW5YbHkC6Fq9O2dJv1kLhX3pN8e", true)]
    [InlineData("$2a$04$abcdefghijklmnopqrstuuOwl3qW5YbHkC6Fq9O2dJv1kLhX3pN8e", true)]
    [InlineData("$2b$12$abcdefghijklmnopqrstuuOwl3qW5YbHkC6Fq9O2dJv1kLhX3pN8e", true)]
    [InlineData("$1$abc$xyz", false)]      // outro algoritmo
    [InlineData("1234", false)]
    [InlineData(null, false)]
    public void ReconheceOFormatoBcrypt(string? valor, bool esperado)
    {
        Assert.Equal(esperado, PdvKeyHasher.EhHashBcrypt(valor));
    }
}

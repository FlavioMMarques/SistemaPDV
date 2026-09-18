using SistemaPDV.Services;

namespace SistemaPDV.Tests;

public class PdvKeyHasherTests
{
    [Fact]
    public void MesmaChaveGeraOMesmoHash()
    {
        Assert.Equal(PdvKeyHasher.Hash("1234"), PdvKeyHasher.Hash("1234"));
    }

    [Fact]
    public void ChavesDiferentesGeramHashesDiferentes()
    {
        Assert.NotEqual(PdvKeyHasher.Hash("1234"), PdvKeyHasher.Hash("4321"));
    }

    [Fact]
    public void HashNuncaEIgualAChaveOriginal()
    {
        Assert.NotEqual("1234", PdvKeyHasher.Hash("1234"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NuloOuVazioDevolveNulo(string? valor)
    {
        Assert.Null(PdvKeyHasher.Hash(valor));
    }
}

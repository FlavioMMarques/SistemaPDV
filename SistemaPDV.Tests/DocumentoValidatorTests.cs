using SistemaPDV.Services;

namespace SistemaPDV.Tests;

public class DocumentoValidatorTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void CpfValidoComOuSemMascara(string cpf)
    {
        Assert.True(DocumentoValidator.CpfValido(cpf));
    }

    [Theory]
    [InlineData("529.982.247-24")] // dígito verificador errado
    [InlineData("111.111.111-11")] // repetido: passa na conta mas não é CPF real
    [InlineData("123")]
    [InlineData("")]
    [InlineData(null)]
    public void CpfInvalido(string? cpf)
    {
        Assert.False(DocumentoValidator.CpfValido(cpf));
    }

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void CnpjValidoComOuSemMascara(string cnpj)
    {
        Assert.True(DocumentoValidator.CnpjValido(cnpj));
    }

    [Theory]
    [InlineData("11.222.333/0001-82")] // dígito verificador errado
    [InlineData("00000000000000")]     // repetido
    [InlineData("1122233300018")]      // 13 dígitos
    [InlineData(null)]
    public void CnpjInvalido(string? cnpj)
    {
        Assert.False(DocumentoValidator.CnpjValido(cnpj));
    }

    [Fact]
    public void SoDigitosRemoveMascaraELetras()
    {
        Assert.Equal("52998224725", DocumentoValidator.SoDigitos("529.982.247-25"));
        Assert.Equal("123", DocumentoValidator.SoDigitos("1a2b3c"));
        Assert.Equal(string.Empty, DocumentoValidator.SoDigitos(null));
    }
}

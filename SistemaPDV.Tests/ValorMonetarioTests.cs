using SistemaPDV.Services;

namespace SistemaPDV.Tests;

// O app lia valores com NumberStyles.Number + cultura invariante: nesse modo a VÍRGULA é separador de milhar,
// então um brasileiro digitando "10,50" tinha 1050 lido (e "0,5" kg virava 5 kg). O leitor único aceita
// vírgula OU ponto como decimal e recusa o que for ambíguo em vez de adivinhar.
public class ValorMonetarioTests
{
    [Theory]
    [InlineData("10,50", 10.50)]
    [InlineData("10.50", 10.50)]
    [InlineData("35", 35)]
    [InlineData("  12 ", 12)]
    [InlineData("0,5", 0.5)]
    [InlineData("1.234,56", 1234.56)]   // pt-BR com milhar
    [InlineData("1,234.56", 1234.56)]   // en com milhar
    [InlineData("1234,5", 1234.5)]
    public void LeVirgulaOuPontoComoDecimal(string texto, double esperado)
    {
        Assert.True(ValorMonetario.TentarLer(texto, out var valor));
        Assert.Equal((decimal)esperado, valor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("10,5,0")]
    [InlineData("1.234")]      // 3 casas: milhar ou fração? ambíguo -> recusa
    [InlineData("1,234")]
    [InlineData("10,505")]     // mais de 2 casas em dinheiro
    [InlineData("R$ 10,00")]
    public void RecusaVazioLixoEAmbiguidade(string? texto)
    {
        Assert.False(ValorMonetario.TentarLer(texto, out _));
    }

    [Theory]
    [InlineData("0,5", 0.5)]
    [InlineData("1,250", 1.25)]
    [InlineData("0.125", 0.125)]
    [InlineData("2", 2)]
    public void QuantidadeAceitaAteTresCasas(string texto, double esperado)
    {
        Assert.True(ValorMonetario.TentarLer(texto, out var valor, casasDecimais: 3));
        Assert.Equal((decimal)esperado, valor);
    }

    [Fact]
    public void QuantidadeComQuatroCasasEhRecusada()
    {
        Assert.False(ValorMonetario.TentarLer("0,1255", out _, casasDecimais: 3));
    }

    [Fact]
    public void FormatarUsaVirgulaEDuasCasas()
    {
        Assert.Equal("35,00", ValorMonetario.Formatar(35m));
        Assert.Equal("1234,50", ValorMonetario.Formatar(1234.5m));
    }
}

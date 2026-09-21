using SistemaPDV.Services.Sales;

namespace SistemaPDV.Tests;

// A API só aceita desconto POR ITEM (desconto_valor_item); o operador dá um desconto na venda toda. O rateio reparte o valor
// entre os itens, proporcional ao que cada um vale, em centavos — e a soma das partes tem de dar EXATAMENTE o desconto.
public class RateioDeDescontoTests
{
    [Fact]
    public void ItemUnicoLevaODescontoInteiro()
    {
        Assert.Equal(new[] { 5m }, RateioDeDesconto.Ratear(5m, new[] { 20m }));
    }

    [Fact]
    public void ReparteProporcionalAoValorDeCadaItem()
    {
        // 10 de desconto sobre 60 + 40: 6 e 4.
        Assert.Equal(new[] { 6m, 4m }, RateioDeDesconto.Ratear(10m, new[] { 60m, 40m }));
    }

    [Fact]
    public void ASomaDasPartesFechaExatamenteNoDescontoMesmoQuandoNaoDivideCerto()
    {
        // 1,00 em três itens iguais: 0,33 + 0,33 + 0,34 — nenhum centavo se perde nem se inventa.
        var partes = RateioDeDesconto.Ratear(1m, new[] { 10m, 10m, 10m });

        Assert.Equal(1m, partes.Sum());
        Assert.Equal(new[] { 0.33m, 0.33m, 0.34m }.OrderBy(x => x), partes.OrderBy(x => x));
    }

    [Theory]
    [InlineData(0.01, 3)]
    [InlineData(0.99, 7)]
    [InlineData(12.34, 5)]
    [InlineData(33.58, 4)]      // o subtotal inteiro (casos extremos: cada item leva exatamente o que vale)
    public void ASomaSempreEIgualAoDescontoEmQualquerCombinacao(double desconto, int itens)
    {
        var bases = Enumerable.Range(1, itens).Select(i => 3.33m * i + 0.07m).ToList();

        var partes = RateioDeDesconto.Ratear((decimal)desconto, bases);

        Assert.Equal((decimal)desconto, partes.Sum());
        Assert.All(partes, p => Assert.Equal(p, decimal.Round(p, 2)));                 // só centavos
        Assert.All(partes.Zip(bases), par => Assert.InRange(par.First, 0m, par.Second));   // nenhum item fica negativo
    }

    [Fact]
    public void ItemPequenoNaoLevaMaisQueVale()
    {
        // 0,02 e 99,98: o desconto de 99,99 leva quase tudo do grande e nunca passa do valor do pequeno.
        var partes = RateioDeDesconto.Ratear(99.99m, new[] { 0.02m, 99.98m });

        Assert.Equal(99.99m, partes.Sum());
        Assert.True(partes[0] <= 0.02m);
    }

    [Fact]
    public void DescontoZeroNaoDescontaNada()
    {
        Assert.Equal(new[] { 0m, 0m }, RateioDeDesconto.Ratear(0m, new[] { 10m, 20m }));
    }

    [Fact]
    public void DescontoQueNaoCabeNoSubtotalERecusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RateioDeDesconto.Ratear(30.01m, new[] { 10m, 20m }));
    }

    [Fact]
    public void SemItensNaoHaOQueRatear()
    {
        Assert.Empty(RateioDeDesconto.Ratear(0m, Array.Empty<decimal>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => RateioDeDesconto.Ratear(1m, Array.Empty<decimal>()));
    }
}

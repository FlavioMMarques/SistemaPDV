using System;
using System.Collections.Generic;
using System.Linq;

namespace SistemaPDV.Services.Sales;

// A API do SoftcomShop só aceita desconto POR ITEM (desconto_valor_item), não no cabeçalho da venda. O operador dá o desconto
// na venda toda; aqui ele é repartido entre os itens, proporcional ao valor de cada um, e o que vai para a API já é a parte
// de cada item. Tudo em centavos: a soma das partes é EXATAMENTE o desconto (o resto dos centavos vai para os itens de maior
// fração), sem R$ 0,01 perdido nem inventado.
public static class RateioDeDesconto
{
    // bases: o valor de cada item ANTES do desconto (quantidade × preço + acréscimo). Devolve a parte de cada item, na mesma ordem.
    public static IReadOnlyList<decimal> Ratear(decimal desconto, IReadOnlyList<decimal> bases)
    {
        var centavos = (long)decimal.Round(desconto * 100m, 0, MidpointRounding.AwayFromZero);
        if (centavos < 0)
            throw new ArgumentOutOfRangeException(nameof(desconto), "O desconto não pode ser negativo.");

        var totalDaBase = bases.Sum();
        if (centavos > (long)decimal.Floor(totalDaBase * 100m))
            throw new ArgumentOutOfRangeException(nameof(desconto), "O desconto não pode passar do subtotal.");

        if (centavos == 0)
            return bases.Select(_ => 0m).ToList();

        // Parte exata de cada item, em centavos (com fração); cada um leva o "piso" e a sobra (menor que o número de itens)
        // vai, um centavo por vez, para quem teve a maior fração.
        var exatos = bases.Select(b => centavos * b / totalDaBase).ToList();
        var pisos = exatos.Select(e => (long)decimal.Floor(e)).ToArray();
        var sobra = centavos - pisos.Sum();

        var porFracao = Enumerable.Range(0, bases.Count)
            .OrderByDescending(i => exatos[i] - pisos[i])
            .ThenBy(i => i)
            .Take((int)sobra);
        foreach (var i in porFracao)
            pisos[i]++;

        return pisos.Select(p => p / 100m).ToList();
    }
}

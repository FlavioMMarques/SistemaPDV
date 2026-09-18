using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class FormaPagamentoConfigurationTests
{
    [Fact]
    public void InsereELeDeVoltaUmaFormaPagamentoValida()
    {
        using var fixture = new SqliteInMemoryFixture();

        using (var escrita = fixture.CriarContexto())
        {
            escrita.FormasPagamento.Add(new FormaPagamento
            {
                Nome = "PIX",
                Tipo = "CARTEIRA_DIGITAL",
                Padrao = true,
                CodigoNfce = "17",
                CarteiraDigital = true,
                Ordem = 9,
            });
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        var forma = leitura.FormasPagamento.Single();

        Assert.Equal("PIX", forma.Nome);
        Assert.True(forma.Padrao);
        Assert.True(forma.CarteiraDigital);
        Assert.Equal(SyncStatus.PendenteSync, forma.SyncStatus);
    }

    [Fact]
    public void IdExternoDuplicadoViolaIndiceUnico()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var context = fixture.CriarContexto();

        context.FormasPagamento.Add(new FormaPagamento { Nome = "Dinheiro", Tipo = "ESPECIE", IdExterno = 1 });
        context.SaveChanges();

        context.FormasPagamento.Add(new FormaPagamento { Nome = "Duplicata", Tipo = "DUPLICATA", IdExterno = 1 });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }
}

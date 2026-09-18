using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class ProdutoConfigurationTests
{
    [Fact]
    public void InsereELeDeVoltaUmProdutoValido()
    {
        using var fixture = new SqliteInMemoryFixture();

        using (var escrita = fixture.CriarContexto())
        {
            var produto = new Produto
            {
                Nome = "Refrigerante 2L",
                PrecoVenda = 9.90m,
                EstoqueAtual = 10,
                Ncm = "22021000",
                UnidadeMedida = "UN",
            };
            produto.Imagens.Add(new ImagemProduto { ArquivoOriginal = "refrigerante-2l.jpg", Tipo = "PRINCIPAL" });
            escrita.Produtos.Add(produto);
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        var produtoLido = leitura.Produtos.Include(p => p.Imagens).Single();

        Assert.Equal("Refrigerante 2L", produtoLido.Nome);
        Assert.Equal(9.90m, produtoLido.PrecoVenda);
        Assert.Equal(10, produtoLido.EstoqueAtual);
        Assert.Equal("22021000", produtoLido.Ncm);
        Assert.Single(produtoLido.Imagens);
        Assert.Equal("PRINCIPAL", produtoLido.Imagens.Single().Tipo);
        Assert.Equal(SyncStatus.PendenteSync, produtoLido.SyncStatus);
    }

    [Fact]
    public void IdExternoDuplicadoViolaIndiceUnico()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var context = fixture.CriarContexto();

        context.Produtos.Add(new Produto { Nome = "Produto A", IdExterno = 1 });
        context.SaveChanges();

        context.Produtos.Add(new Produto { Nome = "Produto B", IdExterno = 1 });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }
}

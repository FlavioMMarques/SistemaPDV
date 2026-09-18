using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class ClienteConfigurationTests
{
    [Fact]
    public void InsereELeDeVoltaUmClienteValido()
    {
        using var fixture = new SqliteInMemoryFixture();

        using (var escrita = fixture.CriarContexto())
        {
            escrita.Clientes.Add(new Cliente
            {
                Nome = "Padaria & Confeitaria Pão de Ouro",
                RazaoSocial = "C. CRIATIVO COMERCIO DE PRESENTES LTDA - ME",
                Pessoa = TipoPessoa.Juridica,
                CpfCnpj = "26849839000199",
                Cidade = "CURITIBA",
                Uf = "PR",
                TabelaPreco = new TabelaPreco { Descricao = "PADRAO" },
            });
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();

        Assert.Equal("Padaria & Confeitaria Pão de Ouro", cliente.Nome);
        Assert.Equal(TipoPessoa.Juridica, cliente.Pessoa);
        Assert.Equal("26849839000199", cliente.CpfCnpj);
        Assert.Equal("PADRAO", cliente.TabelaPreco?.Descricao);
        Assert.Equal(SyncStatus.PendenteSync, cliente.SyncStatus);
    }

    [Fact]
    public void IdExternoDuplicadoViolaIndiceUnico()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var context = fixture.CriarContexto();

        context.Clientes.Add(new Cliente { Nome = "Cliente A", IdExterno = 1 });
        context.SaveChanges();

        context.Clientes.Add(new Cliente { Nome = "Cliente B", IdExterno = 1 });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }
}

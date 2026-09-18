using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class FuncionarioConfigurationTests
{
    [Fact]
    public void InsereELeDeVoltaUmFuncionarioValido()
    {
        using var fixture = new SqliteInMemoryFixture();

        using (var escrita = fixture.CriarContexto())
        {
            escrita.Funcionarios.Add(new Funcionario { Nome = "Carlos Silva", Supervisor = false, PdvKeyHash = new string('a', 64) });
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        var funcionario = leitura.Funcionarios.Single();

        Assert.Equal("Carlos Silva", funcionario.Nome);
        Assert.False(funcionario.Supervisor);
        Assert.Equal(SyncStatus.PendenteSync, funcionario.SyncStatus);
    }

    [Fact]
    public void NaoExisteNenhumaPropriedadeGuardandoOPdvKeyEmClaro()
    {
        var propriedades = typeof(Funcionario).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(propriedades, p => p.Name.Equals("PdvKey", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(propriedades, p => p.Name == "PdvKeyHash");
    }
}

using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class CaixaConfigurationTests
{
    private static Funcionario NovoFuncionario() => new()
    {
        Nome = "Carlos Silva",
        PdvKeyHash = new string('a', 64),
    };

    [Fact]
    public void InsereELeDeVoltaUmCaixaValido()
    {
        using var fixture = new SqliteInMemoryFixture();
        var dataCaixa = new DateOnly(2026, 9, 18);

        using (var escrita = fixture.CriarContexto())
        {
            var funcionario = NovoFuncionario();
            escrita.Funcionarios.Add(funcionario);
            escrita.SaveChanges();

            escrita.Caixas.Add(new Caixa
            {
                FuncionarioId = funcionario.Id,
                DataCaixa = dataCaixa,
                Turno = 1,
                DataAbertura = new DateTime(2026, 9, 18, 8, 0, 0),
                TrocoInicial = 10m,
            });
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();

        Assert.Equal(dataCaixa, caixa.DataCaixa);
        Assert.Equal(1, caixa.Turno);
        Assert.Equal(StatusCaixa.Aberto, caixa.Status);
        Assert.Equal(SyncStatus.PendenteSync, caixa.SyncStatus);
    }

    [Fact]
    public void ChaveNaturalDuplicadaViolaIndiceUnico()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var context = fixture.CriarContexto();

        var funcionario = NovoFuncionario();
        context.Funcionarios.Add(funcionario);
        context.SaveChanges();

        var dataCaixa = new DateOnly(2026, 9, 18);

        context.Caixas.Add(new Caixa { FuncionarioId = funcionario.Id, DataCaixa = dataCaixa, Turno = 1, DataAbertura = DateTime.Now, TrocoInicial = 10m });
        context.SaveChanges();

        context.Caixas.Add(new Caixa { FuncionarioId = funcionario.Id, DataCaixa = dataCaixa, Turno = 1, DataAbertura = DateTime.Now, TrocoInicial = 10m });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }
}

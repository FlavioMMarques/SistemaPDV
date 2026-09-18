using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class EmpresaConfigurationTests
{
    [Fact]
    public void InsereELeDeVoltaUmaEmpresaValida()
    {
        using var fixture = new SqliteInMemoryFixture();

        using (var escrita = fixture.CriarContexto())
        {
            escrita.Empresas.Add(new Empresa { RazaoSocial = "Softcom Tecnologia LTDA", Cnpj = "12345678000199" });
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        var empresa = leitura.Empresas.Single();

        Assert.Equal("Softcom Tecnologia LTDA", empresa.RazaoSocial);
        Assert.Equal(SyncStatus.PendenteSync, empresa.SyncStatus);
    }

    [Fact]
    public void CertificadoProtegidoNuncaApareceNoToString()
    {
        var empresa = new Empresa
        {
            RazaoSocial = "Softcom Tecnologia LTDA",
            Cnpj = "12345678000199",
            CertificadoProtegido = "valor-supostamente-secreto",
        };

        Assert.DoesNotContain("valor-supostamente-secreto", empresa.ToString());
        Assert.DoesNotContain("CertificadoProtegido", empresa.ToString());
    }
}

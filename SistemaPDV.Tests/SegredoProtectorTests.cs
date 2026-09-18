using System.Runtime.Versioning;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Propaga a mesma anotação de SegredoProtector: esses testes só fazem sentido rodando
// no Windows (DPAPI), então o analisador de compatibilidade de plataforma (CA1416)
// só fica satisfeito se essa restrição também for declarada aqui.
[SupportedOSPlatform("windows")]
public class SegredoProtectorTests
{
    private readonly SegredoProtector protector = new();

    [Fact]
    public void ProtegerEDesprotegerDevolveOValorOriginal()
    {
        var protegido = protector.Proteger("meu-client-secret");
        var desprotegido = protector.Desproteger(protegido);

        Assert.Equal("meu-client-secret", desprotegido);
    }

    [Fact]
    public void ValorProtegidoEDiferenteDoOriginal()
    {
        var protegido = protector.Proteger("meu-client-secret");

        Assert.NotEqual("meu-client-secret", protegido);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NuloOuVazioPassaDireto(string? valor)
    {
        Assert.Equal(valor, protector.Proteger(valor));
        Assert.Equal(valor, protector.Desproteger(valor));
    }

    [Fact]
    public void DesprotegerValorNuncaProtegidoDevolveOMesmoValor()
    {
        var resultado = protector.Desproteger("valor-gravado-antes-desse-esquema-existir");

        Assert.Equal("valor-gravado-antes-desse-esquema-existir", resultado);
    }
}

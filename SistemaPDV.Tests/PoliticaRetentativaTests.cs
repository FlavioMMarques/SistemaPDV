using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Espera crescente + teto de tentativas dos outboxes (caixa, venda, cliente): um erro
// permanente (422) não pode ser reenviado a cada 30 s pra sempre.
public class PoliticaRetentativaTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 600)]   // teto de 10 min
    [InlineData(7, 600)]
    public void EsperaDobraEAteOTeto(int tentativas, int segundosEsperados)
    {
        Assert.Equal(TimeSpan.FromSeconds(segundosEsperados), PoliticaRetentativa.Espera(tentativas));
    }

    [Fact]
    public void RegistrarFalhaContaAgendaAProximaEDevolveAMensagemIntacta()
    {
        var cliente = new Cliente { Nome = "Maria" };
        var agora = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

        var mensagem = PoliticaRetentativa.RegistrarFalha(cliente, agora, "cpf invalido");

        Assert.Equal(1, cliente.TentativasEnvio);
        Assert.Equal(agora.AddSeconds(30), cliente.ProximaTentativaEm);
        Assert.Equal("cpf invalido", mensagem);
    }

    [Fact]
    public void NaUltimaTentativaDesisteEAvisaComoRetomar()
    {
        var cliente = new Cliente { Nome = "Maria", TentativasEnvio = PoliticaRetentativa.MaximoTentativas - 1 };

        var mensagem = PoliticaRetentativa.RegistrarFalha(cliente, DateTime.UtcNow, "cpf invalido");

        Assert.Equal(PoliticaRetentativa.MaximoTentativas, cliente.TentativasEnvio);
        Assert.Null(cliente.ProximaTentativaEm);
        Assert.StartsWith("cpf invalido", mensagem);
        Assert.Contains("parou de tentar", mensagem);
        Assert.Contains("Reenviar", mensagem);
        Assert.True(PoliticaRetentativa.Esgotou(cliente));
    }

    [Fact]
    public void ZerarVoltaAoEstadoInicial()
    {
        var venda = new Venda { TentativasEnvio = 5, ProximaTentativaEm = DateTime.UtcNow.AddMinutes(5) };

        PoliticaRetentativa.Zerar(venda);

        Assert.Equal(0, venda.TentativasEnvio);
        Assert.Null(venda.ProximaTentativaEm);
        Assert.False(PoliticaRetentativa.Esgotou(venda));
    }

    [Fact]
    public async Task ElegivelFiltraNoBancoNovosVencidosSimEmEsperaEEsgotadosNao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var agora = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.AddRange(
                new Cliente { Nome = "novo" },
                new Cliente { Nome = "vencido", TentativasEnvio = 2, ProximaTentativaEm = agora.AddSeconds(-1) },
                new Cliente { Nome = "em-espera", TentativasEnvio = 2, ProximaTentativaEm = agora.AddMinutes(1) },
                new Cliente { Nome = "esgotado", TentativasEnvio = PoliticaRetentativa.MaximoTentativas, ProximaTentativaEm = null });
            await context.SaveChangesAsync();
        }

        await using var leitura = fixture.CriarContexto();
        var nomes = leitura.Clientes.Where(PoliticaRetentativa.Elegivel<Cliente>(agora)).Select(c => c.Nome).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "novo", "vencido" }, nomes);
    }
}

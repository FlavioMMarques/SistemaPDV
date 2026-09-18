namespace SistemaPDV.Tests;

using SistemaPDV.Models;

public class ConfiguracaoSincronizacaoConfigurationTests
{
    [Fact]
    public void InsereELeDeVoltaUmaConfiguracaoValida()
    {
        using var fixture = new SqliteInMemoryFixture();
        var agora = DateTimeOffset.UtcNow;

        using (var escrita = fixture.CriarContexto())
        {
            escrita.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = "https://exemplo.softcomshop.com.br/softauth/registrar?client_id=abc",
                ApiClienteId = "abc",
                ApiClienteSecretProtegido = "protegido-base64",
                NomeDispositivo = "PDV-01",
                UltimaSincronizacaoProdutos = agora,
            });
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        var configuracao = leitura.ConfiguracoesSincronizacao.Single();

        Assert.Equal("PDV-01", configuracao.NomeDispositivo);
        Assert.Equal(agora, configuracao.UltimaSincronizacaoProdutos);
        Assert.Null(configuracao.UltimaSincronizacaoClientes);
    }

    [Fact]
    public void ExigirAberturaCaixaComecaTrueQuandoNaoInformado()
    {
        using var fixture = new SqliteInMemoryFixture();

        using (var escrita = fixture.CriarContexto())
        {
            escrita.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao());
            escrita.SaveChanges();
        }

        using var leitura = fixture.CriarContexto();
        Assert.True(leitura.ConfiguracoesSincronizacao.Single().ExigirAberturaCaixa);
    }
}

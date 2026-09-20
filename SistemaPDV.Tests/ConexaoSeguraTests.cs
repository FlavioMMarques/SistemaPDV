using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

public class ConexaoSeguraTests
{
    [Theory]
    [InlineData("https://exemplo.softcomshop.com.br")]
    [InlineData("https://exemplo.softcomshop.com.br/registrar?client_id=1")]
    [InlineData("http://localhost:73")]
    [InlineData("http://localhost:73/registrar?client_id=1")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void Permitida(string url)
    {
        Assert.True(ConexaoSegura.Permitida(url));
    }

    [Theory]
    [InlineData("http://exemplo.softcomshop.com.br")]
    [InlineData("http://exemplo.softcomshop.com.br/registrar?client_id=1")]
    // Tentativas de se passar por loopback — o host de verdade é outro.
    [InlineData("http://localhost.evil.com")]
    [InlineData("http://localhost@evil.com/")]
    [InlineData("http://127.0.0.1.evil.com")]
    [InlineData("ftp://exemplo.softcomshop.com.br")]
    [InlineData("exemplo.softcomshop.com.br")]
    [InlineData("nao é uma url")]
    [InlineData("")]
    [InlineData(null)]
    public void Recusada(string? url)
    {
        Assert.False(ConexaoSegura.Permitida(url));
    }
}

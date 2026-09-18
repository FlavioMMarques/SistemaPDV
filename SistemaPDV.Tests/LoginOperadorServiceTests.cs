using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class LoginOperadorServiceTests
{
    private static async Task SemearFuncionarioAsync(SqliteInMemoryFixture fixture, string nome, string pdvKey, bool desativado = false)
    {
        await using var context = fixture.CriarContexto();
        context.Funcionarios.Add(new Funcionario { Nome = nome, PdvKeyHash = PdvKeyHasher.Hash(pdvKey), Desativado = desativado });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task PdvKeyCorretaAutentica()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync("1234");

        Assert.NotNull(funcionario);
        Assert.Equal("Carlos Silva", funcionario!.Nome);
    }

    [Fact]
    public async Task PdvKeyErradaNaoAutentica()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync("9999");

        Assert.Null(funcionario);
    }

    [Fact]
    public async Task FuncionarioDesativadoNaoAutenticaMesmoComPdvKeyCorreta()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Ex-funcionario", "1234", desativado: true);
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync("1234");

        Assert.Null(funcionario);
    }

    [Fact]
    public async Task PdvKeyVaziaNaoAutentica()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync("");

        Assert.Null(funcionario);
    }
}

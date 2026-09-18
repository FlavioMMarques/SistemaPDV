using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class CaixaServiceObterCaixaAbertoTests
{
    private static async Task<int> SemearFuncionarioAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos Silva" };
        context.Funcionarios.Add(funcionario);
        await context.SaveChangesAsync();
        return funcionario.Id;
    }

    [Fact]
    public async Task SemNenhumCaixaDevolveNull()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var caixa = await service.ObterCaixaAbertoAsync(funcionarioId);

        Assert.Null(caixa);
    }

    [Fact]
    public async Task ComCaixaAbertoDevolveEle()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);
        await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 19), 1, 10m);

        var caixa = await service.ObterCaixaAbertoAsync(funcionarioId);

        Assert.NotNull(caixa);
        Assert.Equal(StatusCaixa.Aberto, caixa!.Status);
    }

    [Fact]
    public async Task ComCaixaJaFechadoDevolveNull()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);
        var abertura = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 19), 1, 10m);
        await service.FecharCaixaLocalAsync(abertura.Valor!.Id, 10m, [], []);

        var caixa = await service.ObterCaixaAbertoAsync(funcionarioId);

        Assert.Null(caixa);
    }

    [Fact]
    public async Task NaoDevolveCaixaAbertoDeOutroFuncionario()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var outroFuncionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);
        await service.AbrirCaixaLocalAsync(outroFuncionarioId, new DateOnly(2026, 9, 19), 1, 10m);

        var caixa = await service.ObterCaixaAbertoAsync(funcionarioId);

        Assert.Null(caixa);
    }
}

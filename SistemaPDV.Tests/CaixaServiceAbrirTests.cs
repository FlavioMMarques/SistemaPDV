using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class CaixaServiceAbrirTests
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
    public async Task AbrirCaixaLocalCriaOCaixaSemRede()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var resultado = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);

        Assert.True(resultado.Sucesso);
        Assert.NotNull(resultado.Valor);
        Assert.Equal(StatusCaixa.Aberto, resultado.Valor!.Status);
        Assert.Equal(SyncStatus.PendenteSync, resultado.Valor.SyncStatus);
        Assert.Null(resultado.Valor.IdExterno);
    }

    [Fact]
    public async Task AbrirDoisCaixasComMesmaChaveNaturalFalha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);
        var segundaTentativa = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);

        Assert.False(segundaTentativa.Sucesso);
        Assert.Contains("já existe", segundaTentativa.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AbrirCaixasComTurnosDiferentesFuncionaNormalmente()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var turno1 = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);
        var turno2 = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 2, 10m);

        Assert.True(turno1.Sucesso);
        Assert.True(turno2.Sucesso);
    }
}

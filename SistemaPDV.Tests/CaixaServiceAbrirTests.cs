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
        Assert.Contains("já foi usado", segundaTentativa.Mensagem, StringComparison.OrdinalIgnoreCase);
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task AceitaOsSeisTurnosDoSoftcomShop(int turno)
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var resultado = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), turno, 10m);

        Assert.True(resultado.Sucesso);
        Assert.Equal(turno, resultado.Valor!.Turno);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    public async Task TurnoForaDeUmASeisNaoGravaNada(int turno)
    {
        // A API só conhece 1-6; um turno fora disso viraria 422 depois, com o caixa já
        // gravado como pendente pra sempre.
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var resultado = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), turno, 10m);

        Assert.False(resultado.Sucesso);
        Assert.Contains("turno", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Caixas);
    }

    [Fact]
    public async Task MesmoFuncionarioPodeTerCaixaEmTurnosDiferentesNoMesmoDia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);
        var dia = new DateOnly(2026, 9, 18);

        var turno4 = await service.AbrirCaixaLocalAsync(funcionarioId, dia, 4, 10m);
        await service.FecharCaixaLocalAsync(turno4.Valor!.Id, 10m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());
        var turno6 = await service.AbrirCaixaLocalAsync(funcionarioId, dia, 6, 10m);

        Assert.True(turno6.Sucesso);
    }

    [Fact]
    public async Task TurnosUsadosListaOsDoOperadorNaquelaDataInclusiveOsJaFechados()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        int outroId;
        await using (var context = fixture.CriarContexto())
        {
            var outro = new Funcionario { Nome = "Outro" };
            context.Funcionarios.Add(outro);
            await context.SaveChangesAsync();
            outroId = outro.Id;
        }
        var service = new CaixaService(fixture.CriarContexto);
        var dia = new DateOnly(2026, 9, 20);
        var turno1 = await service.AbrirCaixaLocalAsync(funcionarioId, dia, 1, 10m);
        await service.FecharCaixaLocalAsync(turno1.Valor!.Id, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());
        await service.AbrirCaixaLocalAsync(funcionarioId, dia, 3, 10m);
        await service.AbrirCaixaLocalAsync(funcionarioId, dia.AddDays(1), 2, 10m);   // outro dia: não conta
        await service.AbrirCaixaLocalAsync(outroId, dia, 5, 10m);                     // outro operador: não conta

        var usados = await service.TurnosUsadosAsync(funcionarioId, dia);

        Assert.Equal(new[] { 1, 3 }, usados.OrderBy(t => t).ToArray());
    }

    [Fact]
    public async Task ReabrirOMesmoTurnoExplicaQueEleJaFoiUsadoEPedeOutro()
    {
        // "Já existe um caixa local para esse funcionário, data e turno." não dizia o que fazer.
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);
        var dia = new DateOnly(2026, 9, 20);
        var primeiro = await service.AbrirCaixaLocalAsync(funcionarioId, dia, 1, 10m);
        await service.FecharCaixaLocalAsync(primeiro.Valor!.Id, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        var segundo = await service.AbrirCaixaLocalAsync(funcionarioId, dia, 1, 10m);

        Assert.False(segundo.Sucesso);
        Assert.Contains("turno 1", segundo.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outro turno", segundo.Mensagem, StringComparison.OrdinalIgnoreCase);
    }
}

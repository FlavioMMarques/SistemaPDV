using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class LoginOperadorServiceTests
{
    private static async Task<int> SemearFuncionarioAsync(SqliteInMemoryFixture fixture, string nome, string? pdvKey, bool desativado = false, bool supervisor = false)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario
        {
            Nome = nome,
            PdvKeyHash = pdvKey is null ? null : PdvKeyTeste.Hash(pdvKey),
            Desativado = desativado,
            Supervisor = supervisor,
        };
        context.Funcionarios.Add(funcionario);
        await context.SaveChangesAsync();
        return funcionario.Id;
    }

    [Fact]
    public async Task PdvKeyCorretaDoOperadorEscolhidoAutentica()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync(id, "1234");

        Assert.NotNull(funcionario);
        Assert.Equal("Carlos Silva", funcionario!.Nome);
    }

    [Fact]
    public async Task PdvKeyErradaNaoAutentica()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync(id, "9999");

        Assert.Null(funcionario);
    }

    [Fact]
    public async Task FuncionarioDesativadoNaoAutenticaMesmoComPdvKeyCorreta()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearFuncionarioAsync(fixture, "Ex-funcionario", "1234", desativado: true);
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync(id, "1234");

        Assert.Null(funcionario);
    }

    [Fact]
    public async Task PdvKeyVaziaNaoAutentica()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        var funcionario = await service.AutenticarAsync(id, "");

        Assert.Null(funcionario);
    }

    // O motivo de a tela pedir o operador: a API não garante chave única.
    [Fact]
    public async Task DoisOperadoresComAMesmaChaveEntramCadaUmComoSiMesmo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var ana = await SemearFuncionarioAsync(fixture, "Ana", "1234");
        var bruno = await SemearFuncionarioAsync(fixture, "Bruno", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        Assert.Equal("Bruno", (await service.AutenticarAsync(bruno, "1234"))!.Nome);
        Assert.Equal("Ana", (await service.AutenticarAsync(ana, "1234"))!.Nome);
    }

    [Fact]
    public async Task ChaveDeOutroOperadorNaoAutenticaOEscolhido()
    {
        using var fixture = new SqliteInMemoryFixture();
        var ana = await SemearFuncionarioAsync(fixture, "Ana", "1111");
        await SemearFuncionarioAsync(fixture, "Bruno", "2222");
        var service = new LoginOperadorService(fixture.CriarContexto);

        // "2222" é uma chave válida do sistema, mas não é a da Ana.
        Assert.Null(await service.AutenticarAsync(ana, "2222"));
    }

    [Fact]
    public async Task OperadorInexistenteNaoAutentica()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var service = new LoginOperadorService(fixture.CriarContexto);

        Assert.Null(await service.AutenticarAsync(999, "1234"));
    }

    [Fact]
    public async Task OperadorInexistenteGastaUmaTentativaComoQualquerErro()
    {
        using var fixture = new SqliteInMemoryFixture();
        var servico = new LoginOperadorService(fixture.CriarContexto);

        for (var i = 0; i < 4; i++)
            Assert.Null(await servico.AutenticarAsync(999, "1234"));

        Assert.True(servico.EsperaRestante > TimeSpan.Zero);   // a tela não distingue "operador sumiu" de "chave errada"
    }

    [Fact]
    public async Task ListaSoTemOperadoresAtivosComChaveEmOrdemDeNome()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Zeca", "1111");
        await SemearFuncionarioAsync(fixture, "ana", "2222", supervisor: true);
        await SemearFuncionarioAsync(fixture, "Bruno", "3333");
        await SemearFuncionarioAsync(fixture, "Ex-funcionario", "4444", desativado: true);
        await SemearFuncionarioAsync(fixture, "Sem chave", null);
        var service = new LoginOperadorService(fixture.CriarContexto);

        var operadores = await service.ListarOperadoresAsync();

        Assert.Equal(new[] { "ana", "Bruno", "Zeca" }, operadores.Select(o => o.Nome));
        Assert.True(operadores[0].Supervisor);
        Assert.Equal("Supervisor", operadores[0].Perfil);
        Assert.Equal("Operador", operadores[1].Perfil);
        Assert.Equal("A", operadores[0].Inicial);
    }

    [Fact]
    public async Task ListaVaziaQuandoNaoHaFuncionarios()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new LoginOperadorService(fixture.CriarContexto);

        Assert.Empty(await service.ListarOperadoresAsync());
    }
}

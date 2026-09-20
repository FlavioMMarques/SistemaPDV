using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

public class AbrirCaixaViewModelTests
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
    public async Task AbrirCommandDesabilitadoComTrocoInvalido()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var viewModel = new AbrirCaixaViewModel(new CaixaService(fixture.CriarContexto), funcionarioId);

        var podeExecutar = false;
        viewModel.AbrirCommand.CanExecute.Subscribe(v => podeExecutar = v);
        Assert.False(podeExecutar);

        viewModel.TrocoInicial = "abc";
        Assert.False(podeExecutar);

        viewModel.TrocoInicial = "10.50";
        Assert.True(podeExecutar);
    }

    [Fact]
    public async Task AbrirComSucessoDevolveOCaixaSemMensagemDeErro()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var viewModel = new AbrirCaixaViewModel(new CaixaService(fixture.CriarContexto), funcionarioId)
        {
            TrocoInicial = "10.00",
            Turno = 1,
        };

        var caixa = await viewModel.AbrirCommand.Execute();

        Assert.NotNull(caixa);
        Assert.Equal(StatusCaixa.Aberto, caixa!.Status);
        Assert.Null(viewModel.Mensagem);
    }

    [Fact]
    public async Task AbrirDuasVezesNoMesmoTurnoFalhaNaSegunda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var caixaService = new CaixaService(fixture.CriarContexto);
        var viewModel1 = new AbrirCaixaViewModel(caixaService, funcionarioId) { TrocoInicial = "10.00", Turno = 1 };
        await viewModel1.AbrirCommand.Execute();

        var viewModel2 = new AbrirCaixaViewModel(caixaService, funcionarioId) { TrocoInicial = "10.00", Turno = 1 };
        var caixa = await viewModel2.AbrirCommand.Execute();

        Assert.Null(caixa);
        Assert.False(string.IsNullOrEmpty(viewModel2.Mensagem));
    }

    [Fact]
    public async Task OferecePeloMenosOsSeisTurnosDoSoftcomShop()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var viewModel = new AbrirCaixaViewModel(new CaixaService(fixture.CriarContexto), funcionarioId);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, viewModel.Turnos);
    }

    [Fact]
    public async Task AbrirNoTurnoSeisGravaTurnoSeis()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioAsync(fixture);
        var viewModel = new AbrirCaixaViewModel(new CaixaService(fixture.CriarContexto), funcionarioId)
        {
            TrocoInicial = "10.00",
            Turno = 6,
        };

        var caixa = await viewModel.AbrirCommand.Execute();

        Assert.Equal(6, caixa!.Turno);
    }
}

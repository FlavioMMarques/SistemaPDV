using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

public class LoginViewModelTests
{
    private static async Task SemearFuncionarioAsync(SqliteInMemoryFixture fixture, string nome, string pdvKey, bool desativado = false)
    {
        await using var context = fixture.CriarContexto();
        context.Funcionarios.Add(new Funcionario { Nome = nome, PdvKeyHash = PdvKeyHasher.Hash(pdvKey), Desativado = desativado });
        await context.SaveChangesAsync();
    }

    [Fact]
    public void EntrarCommandDesabilitadoQuandoChaveVazia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new LoginViewModel(new LoginOperadorService(fixture.CriarContexto));

        var podeExecutar = false;
        viewModel.EntrarCommand.CanExecute.Subscribe(v => podeExecutar = v);

        Assert.False(podeExecutar);
    }

    [Fact]
    public void EntrarCommandHabilitadoQuandoChavePreenchida()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new LoginViewModel(new LoginOperadorService(fixture.CriarContexto));

        var podeExecutar = false;
        viewModel.EntrarCommand.CanExecute.Subscribe(v => podeExecutar = v);
        viewModel.PdvKeyDigitada = "1234";

        Assert.True(podeExecutar);
    }

    [Fact]
    public async Task LoginComChaveCorretaRetornaFuncionarioSemErro()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = new LoginViewModel(new LoginOperadorService(fixture.CriarContexto))
        {
            PdvKeyDigitada = "1234",
        };

        var funcionario = await viewModel.EntrarCommand.Execute();

        Assert.NotNull(funcionario);
        Assert.Equal("Carlos Silva", funcionario!.Nome);
        Assert.Null(viewModel.MensagemErro);
    }

    [Fact]
    public async Task LoginComChaveErradaMostraMensagemDeErroSemLancar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = new LoginViewModel(new LoginOperadorService(fixture.CriarContexto))
        {
            PdvKeyDigitada = "9999",
        };

        var funcionario = await viewModel.EntrarCommand.Execute();

        Assert.Null(funcionario);
        Assert.False(string.IsNullOrEmpty(viewModel.MensagemErro));
    }

    [Fact]
    public async Task TentarNovoLoginLimpaMensagemDeErroAnterior()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = new LoginViewModel(new LoginOperadorService(fixture.CriarContexto))
        {
            PdvKeyDigitada = "9999",
        };
        await viewModel.EntrarCommand.Execute();
        Assert.False(string.IsNullOrEmpty(viewModel.MensagemErro));

        viewModel.PdvKeyDigitada = "1234";
        await viewModel.EntrarCommand.Execute();

        Assert.Null(viewModel.MensagemErro);
    }
}

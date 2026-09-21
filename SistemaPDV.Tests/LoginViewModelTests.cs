using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

public class LoginViewModelTests
{
    private static async Task<int> SemearFuncionarioAsync(SqliteInMemoryFixture fixture, string nome, string pdvKey, bool desativado = false)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = nome, PdvKeyHash = PdvKeyTeste.Hash(pdvKey), Desativado = desativado };
        context.Funcionarios.Add(funcionario);
        await context.SaveChangesAsync();
        return funcionario.Id;
    }

    private static async Task<LoginViewModel> CriarCarregadoAsync(SqliteInMemoryFixture fixture)
    {
        var viewModel = new LoginViewModel(new LoginOperadorService(fixture.CriarContexto));
        await viewModel.CarregarOperadoresAsync();
        return viewModel;
    }

    private static bool PodeEntrar(LoginViewModel viewModel)
    {
        var podeExecutar = false;
        viewModel.EntrarCommand.CanExecute.Subscribe(v => podeExecutar = v);
        return podeExecutar;
    }

    [Fact]
    public async Task EntrarDesabilitadoSemOperadorEscolhidoMesmoComChaveDigitada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = await CriarCarregadoAsync(fixture);

        viewModel.PdvKeyDigitada = "1234";

        Assert.False(PodeEntrar(viewModel));
    }

    [Fact]
    public async Task EntrarDesabilitadoComOperadorMasSemChave()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = await CriarCarregadoAsync(fixture);

        viewModel.OperadorSelecionado = viewModel.Operadores[0];

        Assert.False(PodeEntrar(viewModel));
    }

    [Fact]
    public async Task EntrarHabilitadoComOperadorEChave()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = await CriarCarregadoAsync(fixture);

        viewModel.OperadorSelecionado = viewModel.Operadores[0];
        viewModel.PdvKeyDigitada = "1234";

        Assert.True(PodeEntrar(viewModel));
    }

    [Fact]
    public async Task LoginComChaveCorretaRetornaOOperadorEscolhidoSemErro()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = await CriarCarregadoAsync(fixture);
        viewModel.OperadorSelecionado = viewModel.Operadores[0];
        viewModel.PdvKeyDigitada = "1234";

        var funcionario = await viewModel.EntrarCommand.Execute();

        Assert.NotNull(funcionario);
        Assert.Equal("Carlos Silva", funcionario!.Nome);
        Assert.Null(viewModel.MensagemErro);
    }

    [Fact]
    public async Task ChaveIgualDeDoisOperadoresEntraComOQueFoiEscolhido()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Ana", "1234");
        await SemearFuncionarioAsync(fixture, "Bruno", "1234");
        var viewModel = await CriarCarregadoAsync(fixture);
        viewModel.OperadorSelecionado = viewModel.Operadores.Single(o => o.Nome == "Bruno");
        viewModel.PdvKeyDigitada = "1234";

        var funcionario = await viewModel.EntrarCommand.Execute();

        Assert.Equal("Bruno", funcionario!.Nome);
    }

    [Fact]
    public async Task LoginComChaveErradaMostraMensagemDeErroSemLancar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = await CriarCarregadoAsync(fixture);
        viewModel.OperadorSelecionado = viewModel.Operadores[0];
        viewModel.PdvKeyDigitada = "9999";

        var funcionario = await viewModel.EntrarCommand.Execute();

        Assert.Null(funcionario);
        Assert.False(string.IsNullOrEmpty(viewModel.MensagemErro));
    }

    [Fact]
    public async Task TentarNovoLoginLimpaMensagemDeErroAnterior()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Carlos Silva", "1234");
        var viewModel = await CriarCarregadoAsync(fixture);
        viewModel.OperadorSelecionado = viewModel.Operadores[0];
        viewModel.PdvKeyDigitada = "9999";
        await viewModel.EntrarCommand.Execute();
        Assert.False(string.IsNullOrEmpty(viewModel.MensagemErro));

        viewModel.PdvKeyDigitada = "1234";
        await viewModel.EntrarCommand.Execute();

        Assert.Null(viewModel.MensagemErro);
    }

    [Fact]
    public async Task TrocarDeOperadorApagaAChaveEOErroDoAnterior()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Ana", "1111");
        await SemearFuncionarioAsync(fixture, "Bruno", "2222");
        var viewModel = await CriarCarregadoAsync(fixture);
        viewModel.OperadorSelecionado = viewModel.Operadores[0];
        viewModel.PdvKeyDigitada = "9999";
        await viewModel.EntrarCommand.Execute();

        viewModel.OperadorSelecionado = viewModel.Operadores[1];

        Assert.Equal(string.Empty, viewModel.PdvKeyDigitada);
        Assert.Null(viewModel.MensagemErro);
    }

    [Fact]
    public async Task ListaTemSoQuemPodeEntrarEComecaSemNinguemEscolhido()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Ana", "1111");
        await SemearFuncionarioAsync(fixture, "Ex-funcionario", "2222", desativado: true);

        var viewModel = await CriarCarregadoAsync(fixture);

        Assert.Equal(new[] { "Ana" }, viewModel.Operadores.Select(o => o.Nome));
        Assert.Null(viewModel.OperadorSelecionado);
        Assert.False(viewModel.SemOperadores);
    }

    [Fact]
    public async Task SemOperadoresSoVaiParaTrueDepoisDeLerAListaEDesfazQuandoElaChega()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new LoginViewModel(new LoginOperadorService(fixture.CriarContexto));
        Assert.False(viewModel.SemOperadores);   // ainda não olhou: não pode afirmar que está vazio

        await viewModel.CarregarOperadoresAsync();
        Assert.True(viewModel.SemOperadores);

        await SemearFuncionarioAsync(fixture, "Ana", "1111");   // o catálogo chegou
        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.False(viewModel.SemOperadores);
        Assert.Single(viewModel.Operadores);
    }

    [Fact]
    public async Task RecarregarComAListaIgualNaoMexeNaEscolhaNemNaChaveEmDigitacao()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Ana", "1111");
        var viewModel = await CriarCarregadoAsync(fixture);
        var listaAntes = viewModel.Operadores;
        viewModel.OperadorSelecionado = viewModel.Operadores[0];
        viewModel.PdvKeyDigitada = "11";

        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.Same(listaAntes, viewModel.Operadores);   // nem trocou os itens (o ListBox zeraria a escolha)
        Assert.Equal("Ana", viewModel.OperadorSelecionado!.Nome);
        Assert.Equal("11", viewModel.PdvKeyDigitada);
    }

    [Fact]
    public async Task RecarregarComOperadorNovoMantemQuemJaEstavaEscolhidoEAChaveDigitada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearFuncionarioAsync(fixture, "Bruno", "2222");
        var viewModel = await CriarCarregadoAsync(fixture);
        viewModel.OperadorSelecionado = viewModel.Operadores[0];
        viewModel.PdvKeyDigitada = "22";

        await SemearFuncionarioAsync(fixture, "Ana", "1111");   // chegou pela sincronização, no meio da digitação
        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.Equal(2, viewModel.Operadores.Count);
        Assert.Equal("Bruno", viewModel.OperadorSelecionado!.Nome);
        Assert.Equal("22", viewModel.PdvKeyDigitada);
    }

    [Fact]
    public async Task OperadorDesativadoDepoisDeEscolhidoSaiDaListaEDesfazAEscolha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearFuncionarioAsync(fixture, "Ana", "1111");
        await SemearFuncionarioAsync(fixture, "Bruno", "2222");
        var viewModel = await CriarCarregadoAsync(fixture);
        viewModel.OperadorSelecionado = viewModel.Operadores.Single(o => o.Id == id);
        viewModel.PdvKeyDigitada = "1111";

        await using (var context = fixture.CriarContexto())
        {
            context.Funcionarios.Single(f => f.Id == id).Desativado = true;
            await context.SaveChangesAsync();
        }
        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.Equal(new[] { "Bruno" }, viewModel.Operadores.Select(o => o.Nome));
        Assert.Null(viewModel.OperadorSelecionado);
        Assert.Equal(string.Empty, viewModel.PdvKeyDigitada);
    }
}

using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using ReactiveUI;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Painel Principal no visual do protótipo: cartões (faturamento, produtos, fila, conexão) e a tabela "Últimas vendas realizadas".
[SupportedOSPlatform("windows")]
public class PainelPrincipalTests
{
    private static async Task<int> SemearCaixaAsync(SqliteInMemoryFixture fixture, int produtos = 0, DateTimeOffset? ultimaSincronizacao = null)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos Silva" };
        context.Funcionarios.Add(funcionario);
        for (var i = 1; i <= produtos; i++)
            context.Produtos.Add(new Produto { Nome = $"Produto {i}", PrecoVenda = 10m, EstoqueAtual = 5, IdExterno = i });
        if (ultimaSincronizacao is { } quando)
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UltimaSincronizacaoProdutos = quando });
        await context.SaveChangesAsync();

        var caixa = new Caixa
        {
            FuncionarioId = funcionario.Id,
            DataCaixa = DateOnly.FromDateTime(DateTime.Now),
            Turno = 1,
            DataAbertura = DateTime.Now,
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();
        return caixa.Id;
    }

    private static async Task SemearVendasAsync(SqliteInMemoryFixture fixture, int caixaId, int quantidade, SyncStatus status = SyncStatus.Sincronizado)
    {
        await using var context = fixture.CriarContexto();
        var existentes = await context.Vendas.CountAsync();   // o número do pedido é único: segue de onde parou
        for (var i = existentes; i < existentes + quantidade; i++)
        {
            context.Vendas.Add(new Venda
            {
                CaixaId = caixaId,
                DataHora = new DateTime(2026, 9, 21, 9, 0, 0).AddMinutes(i * 10),   // cada venda 10 min depois da anterior
                NumeroPedido = 1000 + i,
                SyncStatus = status,
            });
        }
        await context.SaveChangesAsync();
    }

    private static DashboardViewModel CriarViewModel(SqliteInMemoryFixture fixture, int? caixaId) =>
        new(new DashboardService(fixture.CriarContexto), new VendaLocalService(fixture.CriarContexto), caixaId);

    // ---- cartões ----

    [Fact]
    public async Task CartoesDizemQuantasVendasEProdutosHaComSingularEPlural()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture, produtos: 1);
        var viewModel = CriarViewModel(fixture, caixaId);

        await viewModel.IniciarAsync();
        Assert.Equal("Nenhuma venda emitida hoje", viewModel.TextoVendasEmitidas);
        Assert.Equal("1 item cadastrado", viewModel.TextoProdutosCadastrados);

        await SemearVendasAsync(fixture, caixaId, 1);
        await viewModel.IniciarAsync();
        Assert.Equal("1 venda emitida hoje", viewModel.TextoVendasEmitidas);

        await SemearVendasAsync(fixture, caixaId, 2);
        await viewModel.IniciarAsync();
        Assert.Equal("3 vendas emitidas hoje", viewModel.TextoVendasEmitidas);
    }

    [Fact]
    public async Task ProdutosCadastradosContaOsItensNaoAsUnidadesEmEstoque()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture, produtos: 3);   // 3 produtos × 5 un
        var viewModel = CriarViewModel(fixture, caixaId);

        await viewModel.IniciarAsync();

        Assert.Equal("3 itens cadastrados", viewModel.TextoProdutosCadastrados);
        Assert.Equal("15 un em estoque local", viewModel.DetalheEstoque);
    }

    [Fact]
    public async Task VendaDescartadaNaoContaComoEmitidaMasContinuaNaTabela()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        await SemearVendasAsync(fixture, caixaId, 2);
        await SemearVendasAsync(fixture, caixaId, 1, SyncStatus.Descartada);
        var viewModel = CriarViewModel(fixture, caixaId);

        await viewModel.IniciarAsync();

        Assert.Equal(2, viewModel.VendasEmitidas);                 // a descartada é tratada como cancelada
        Assert.Equal(3, viewModel.UltimasVendas.Count);            // mas a trilha de auditoria continua visível
    }

    [Fact]
    public async Task TextoDaConexaoAcompanhaOEstadoEDizOMesmoQueACor()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture, caixaId: null);

        Assert.Equal("NÃO VERIFICADA", viewModel.TextoConexao);
        viewModel.DefinirConexao(EstadoConexao.Online);
        Assert.Equal("ONLINE", viewModel.TextoConexao);
        viewModel.DefinirConexao(EstadoConexao.OnlineComFalhas);
        Assert.Equal("ONLINE (COM FALHAS)", viewModel.TextoConexao);
        viewModel.DefinirConexao(EstadoConexao.Offline);
        Assert.Equal("MODO OFFLINE", viewModel.TextoConexao);
    }

    [Fact]
    public async Task UltimaSincronizacaoMostraSoAHoraSeForDeHojeEADataSeNao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        Assert.Equal("Ainda não sincronizou", viewModel.TextoUltimaSincronizacao);

        using var fixtureHoje = new SqliteInMemoryFixture();
        var agora = DateTimeOffset.Now;
        var hoje = CriarViewModel(fixtureHoje, await SemearCaixaAsync(fixtureHoje, ultimaSincronizacao: agora));
        await hoje.IniciarAsync();
        Assert.Equal($"Última sinc: {agora.ToLocalTime():HH:mm:ss}", hoje.TextoUltimaSincronizacao);

        using var fixtureAntiga = new SqliteInMemoryFixture();
        var antes = DateTimeOffset.Now.AddDays(-2);
        var antiga = CriarViewModel(fixtureAntiga, await SemearCaixaAsync(fixtureAntiga, ultimaSincronizacao: antes));
        await antiga.IniciarAsync();
        Assert.Equal($"Última sinc: {antes.ToLocalTime():dd/MM HH:mm}", antiga.TextoUltimaSincronizacao);   // uma hora sozinha, de anteontem, enganaria
    }

    // ---- últimas vendas ----

    [Fact]
    public async Task TabelaMostraSoAsCincoMaisRecentesDaMaisNovaParaAMaisAntiga()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        await SemearVendasAsync(fixture, caixaId, 8);
        var viewModel = CriarViewModel(fixture, caixaId);

        await viewModel.IniciarAsync();

        Assert.Equal(5, viewModel.UltimasVendas.Count);
        Assert.Equal(new[] { 1007, 1006, 1005, 1004, 1003 }, viewModel.UltimasVendas.Select(v => v.Numero!.Value));
        Assert.Equal(8, viewModel.VendasEmitidas);                 // o cartão conta todas, a tabela só as últimas
        Assert.False(viewModel.SemVendas);
    }

    [Fact]
    public async Task SemVendasDizPorqueEmVezDeTabelaEmBranco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        var comCaixa = CriarViewModel(fixture, caixaId);
        await comCaixa.IniciarAsync();
        Assert.True(comCaixa.SemVendas);
        Assert.Equal("Nenhuma venda registrada neste caixa ainda.", comCaixa.TextoSemVendas);

        var semCaixa = CriarViewModel(fixture, caixaId: null);
        await semCaixa.IniciarAsync();
        Assert.True(semCaixa.SemVendas);
        Assert.Equal("Abra um caixa para começar a vender.", semCaixa.TextoSemVendas);
    }

    // ---- navegação e conexão (via Shell) ----

    [Fact]
    public async Task ShellRepassaAConexaoAoPainelAoAbrirEACadaMudanca()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture);
        shell.DefinirConexao(EstadoConexao.Offline, "sem rede");

        await shell.IrParaDashboardCommand.Execute();
        var painel = (DashboardViewModel)shell.CurrentViewModel!;
        Assert.Equal("MODO OFFLINE", painel.TextoConexao);           // já abre certo, sem esperar a próxima mudança

        shell.DefinirConexao(EstadoConexao.Online, "ok");
        await shell.RecontagemAposReconexao;
        Assert.Equal("ONLINE", painel.TextoConexao);
    }

    [Fact]
    public async Task VerTodosOsPedidosLevaAListagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture);
        await shell.IrParaDashboardCommand.Execute();
        var painel = (DashboardViewModel)shell.CurrentViewModel!;

        var chegou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.ListaPedidos).FirstAsync().ToTask();
        await painel.VerPedidosCommand.Execute();
        await chegou;   // a navegação do Shell roda em segundo plano depois do comando

        Assert.IsType<ListaPedidosViewModel>(shell.CurrentViewModel);
        Assert.Equal(Tela.ListaPedidos, shell.TelaAtual);
    }

    [Fact]
    public async Task DetalhesLevaAListagemComAQuelaVendaSelecionada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture);
        int caixaId;
        await using (var context = fixture.CriarContexto())
            caixaId = await context.Caixas.Select(c => c.Id).FirstAsync();
        await SemearVendasAsync(fixture, caixaId, 3);
        await shell.IrParaDashboardCommand.Execute();
        var painel = (DashboardViewModel)shell.CurrentViewModel!;
        var escolhida = painel.UltimasVendas.Single(v => v.Numero == 1001);

        var chegou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.ListaPedidos).FirstAsync().ToTask();
        await painel.DetalhesCommand.Execute(escolhida);
        await chegou;

        var lista = Assert.IsType<ListaPedidosViewModel>(shell.CurrentViewModel);
        Assert.Equal(escolhida.Id, lista.VendaSelecionada!.Id);
    }
}

using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using SistemaPDV.Converters;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Barra do topo no visual do protótipo: aba da tela atual destacada, chip "Carlos • Caixa 02" e a pílula
// "Sync: N pendentes" (caixas, vendas e clientes novos que ainda não chegaram à API).
[SupportedOSPlatform("windows")]
public class ShellBarraTopoTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static async Task<ShellViewModel> CriarShellLogadoAsync(SqliteInMemoryFixture fixture, bool comCaixaAberto = true)
    {
        int operadorId;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2, PdvKeyHash = PdvKeyTeste.Hash("1234") };
            context.Funcionarios.Add(operador);
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi, ExigirAberturaCaixa = false });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }
        if (comCaixaAberto)
            await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m);

        var segredoProtector = new SegredoProtector();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var shell = new ShellViewModel(
            new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, segredoProtector), segredoProtector),
            new LoginOperadorService(fixture.CriarContexto),
            new CaixaService(fixture.CriarContexto),
            new DashboardService(fixture.CriarContexto),
            new VendaService(fixture.CriarContexto),
            new CatalogoLocalService(fixture.CriarContexto),
            new VendaLocalService(fixture.CriarContexto),
            new CadastroLocalService(fixture.CriarContexto));
        await shell.IniciarAsync();
        var login = (LoginViewModel)shell.CurrentViewModel!;
        login.PdvKeyDigitada = "1234";
        var saiuDoLogin = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await login.EntrarCommand.Execute();
        await saiuDoLogin;
        return shell;
    }

    // ---- aba ativa ----

    [Fact]
    public async Task ApenasAAbaDaTelaAtualFicaAtivaEAtivaMudaComANavegacao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellLogadoAsync(fixture);

        Assert.Equal(Tela.Dashboard, shell.TelaAtual);
        Assert.Equal(new[] { true, false, false, false, false, false },
            new[] { shell.DashboardAtivo, shell.PdvAtivo, shell.PedidosAtivo, shell.CadastrosAtivo, shell.FecharCaixaAtivo, shell.ConfiguracoesAtivo });

        await shell.IrParaPdvCommand.Execute();
        Assert.Equal(new[] { false, true, false, false, false, false },
            new[] { shell.DashboardAtivo, shell.PdvAtivo, shell.PedidosAtivo, shell.CadastrosAtivo, shell.FecharCaixaAtivo, shell.ConfiguracoesAtivo });

        await shell.IrParaListaPedidosCommand.Execute();
        Assert.True(shell.PedidosAtivo);
        Assert.False(shell.PdvAtivo);

        await shell.IrParaConfiguracoesCommand.Execute();
        Assert.True(shell.ConfiguracoesAtivo);
    }

    [Fact]
    public async Task MudarDeTelaNotificaAsAbasParaABarraAtualizar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellLogadoAsync(fixture);
        var avisadas = new List<string>();
        shell.PropertyChanged += (_, e) => avisadas.Add(e.PropertyName!);

        await shell.IrParaPdvCommand.Execute();

        Assert.Contains(nameof(ShellViewModel.PdvAtivo), avisadas);
        Assert.Contains(nameof(ShellViewModel.DashboardAtivo), avisadas);
    }

    // ---- chip do operador/caixa ----

    [Fact]
    public async Task ChipMostraOperadorCaixaComDoisDigitosInicialESituacao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellLogadoAsync(fixture);

        Assert.Equal($"Carlos • Caixa {shell.CaixaAberto!.Id:00}", shell.RotuloOperadorCaixa);
        Assert.StartsWith("Carlos • Caixa 0", shell.RotuloOperadorCaixa);
        Assert.Equal("C", shell.InicialOperador);
        Assert.Equal("Caixa aberto", shell.SituacaoCaixa);
    }

    [Fact]
    public async Task SemCaixaAbertoOChipSoTemONomeESituacaoDiz()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellLogadoAsync(fixture, comCaixaAberto: false);

        Assert.Equal("Carlos", shell.RotuloOperadorCaixa);
        Assert.Equal("Sem caixa aberto", shell.SituacaoCaixa);
    }

    [Fact]
    public async Task AntesDoLoginNaoHaOperadorNemRotulo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
            await context.SaveChangesAsync();
        }
        var segredoProtector = new SegredoProtector();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var shell = new ShellViewModel(
            new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, segredoProtector), segredoProtector),
            new LoginOperadorService(fixture.CriarContexto), new CaixaService(fixture.CriarContexto), new DashboardService(fixture.CriarContexto),
            new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), new VendaLocalService(fixture.CriarContexto),
            new CadastroLocalService(fixture.CriarContexto));

        Assert.Equal(string.Empty, shell.RotuloOperadorCaixa);
        Assert.Equal("?", shell.InicialOperador);
    }

    // ---- Sync: N pendentes ----

    [Theory]
    [InlineData(0, "tudo enviado")]
    [InlineData(1, "1 pendente")]
    [InlineData(7, "7 pendentes")]
    public async Task TextoDaPilulaSyncConcordaComANumeracao(int pendentes, string esperado)
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellLogadoAsync(fixture, comCaixaAberto: false);
        await using (var context = fixture.CriarContexto())
        {
            // clientes novos ainda sem id da API: cada um conta como 1 item esperando envio
            for (var i = 0; i < pendentes; i++)
                context.Clientes.Add(new Cliente { Nome = $"Cliente {i}", SyncStatus = SyncStatus.PendenteSync });
            await context.SaveChangesAsync();
        }

        shell.NotificarDadosSincronizados();
        await shell.WhenAnyValue(s => s.PendentesSync).Where(n => n == pendentes).FirstAsync().Timeout(TimeSpan.FromSeconds(5)).ToTask();

        Assert.Equal(pendentes, shell.PendentesSync);
        Assert.Equal(esperado, shell.TextoSync);
    }

    [Fact]
    public async Task OCaixaAbertoAindaNaoEnviadoJaAparecaNasPendentesAoEntrar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await CriarShellLogadoAsync(fixture);   // o caixa aberto localmente ainda não foi à API

        Assert.Equal(1, shell.PendentesSync);
        Assert.Equal("1 pendente", shell.TextoSync);
    }

    [Fact]
    public async Task FinalizarUmaVendaSobeAContagemDePendentes()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Produtos.Add(new Produto { Nome = "Refri", PrecoVenda = 10m, IdExterno = 10, ProdutoIdApi = 100 });
            context.FormasPagamento.Add(new FormaPagamento { Nome = "PIX", Tipo = "ESPECIE", IdExterno = 28 });
            await context.SaveChangesAsync();
        }
        var shell = await CriarShellLogadoAsync(fixture);
        var antes = shell.PendentesSync;
        await shell.IrParaPdvCommand.Execute();
        var pdv = (PdvViewModel)shell.CurrentViewModel!;
        pdv.AdicionarItem(pdv.ProdutosDisponiveis.Single());
        await pdv.AbrirPagamentoCommand.Execute();
        pdv.AdicionarPagamento(pdv.FormasPagamentoDisponiveis.Single());

        await pdv.FinalizarVendaCommand.Execute();
        await shell.WhenAnyValue(s => s.PendentesSync).Where(n => n == antes + 1).FirstAsync().Timeout(TimeSpan.FromSeconds(5)).ToTask();

        Assert.Equal(antes + 1, shell.PendentesSync);   // a venda nova espera envio
    }

    // ---- rótulo da conexão ----

    [Theory]
    [InlineData(EstadoConexao.Online, "Online")]
    [InlineData(EstadoConexao.OnlineComFalhas, "Online (com falhas)")]
    [InlineData(EstadoConexao.Offline, "Offline (contingência ativa)")]
    [InlineData(EstadoConexao.Desconhecida, "Conexão não verificada")]
    public void RotuloDaConexaoDizOEstadoEmTextoSemDependerDaCor(EstadoConexao estado, string esperado) =>
        Assert.Equal(esperado, EstadoConexaoParaRotuloConverter.Instance.Convert(estado, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
}

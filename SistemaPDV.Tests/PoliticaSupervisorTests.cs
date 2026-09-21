using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// A exigência de chave de supervisor (descartar venda e abrir as Configurações) está DESLIGADA no app por decisão do
// usuário (2026-09-20): ainda não se sabe como obter a chave do supervisor no SoftcomShop. A lógica continua inteira e
// testada nos outros arquivos (o padrão dos serviços é EXIGIR); aqui fica o modo aberto e a garantia de que a composição
// do app usa o interruptor único (PoliticaSupervisor) — sem isso, religar seria "trocar a constante e torcer".
[SupportedOSPlatform("windows")]
public class PoliticaSupervisorTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private sealed record Cenario(int CaixaId, Guid VendaId, int OperadorId);

    private static async Task<Cenario> SemearVendaEmFalhaAsync(SqliteInMemoryFixture fixture)
    {
        int operadorId, formaId, produtoId;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", CodigoNfce = "17", IdExterno = 5 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 10m, IdExterno = 10, ProdutoIdApi = 100 };
            context.AddRange(operador, forma, produto);
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
            await context.SaveChangesAsync();
            (operadorId, formaId, produtoId) = (operador.Id, forma.Id, produto.Id);
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 20), 1, 10m)).Valor!;
        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixa.Id, null, new[] { (produtoId, 1m, 10m, 0m, 0m) }, new[] { (formaId, 10m) });
        await using (var context = fixture.CriarContexto())
        {
            (await context.Vendas.SingleAsync()).SyncStatus = SyncStatus.FalhaSync;
            await context.SaveChangesAsync();
        }
        return new Cenario(caixa.Id, venda.Id, operadorId);
    }

    private static VendaLocalService ServicoAberto(SqliteInMemoryFixture fixture) => new(fixture.CriarContexto, exigirChaveSupervisor: false);

    // ---- descartar venda, com a exigência desligada ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("qualquer coisa")]
    public async Task DescarteFuncionaSemChaveEIgnoraQualquerChaveDigitada(string? chave)
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearVendaEmFalhaAsync(fixture);

        var resultado = await ServicoAberto(fixture).DescartarVendaAsync(c.VendaId, chave, "API recusa a venda", c.OperadorId);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.Descartada, venda.SyncStatus);
        Assert.Null(venda.DescartadaPorId);                 // ninguém autorizou: não inventa um supervisor
        Assert.Equal(c.OperadorId, venda.SolicitadaPorId);  // mas quem PEDIU fica registrado
        Assert.NotNull(venda.DescartadaEm);
        Assert.Equal("API recusa a venda", venda.MotivoDescarte);
    }

    [Fact]
    public async Task ContinuaExigindoMotivoESoVendaEmFalha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearVendaEmFalhaAsync(fixture);
        var servico = ServicoAberto(fixture);

        var semMotivo = await servico.DescartarVendaAsync(c.VendaId, null, "  ", c.OperadorId);
        await using (var context = fixture.CriarContexto())
        {
            (await context.Vendas.SingleAsync()).SyncStatus = SyncStatus.PendenteSync;
            await context.SaveChangesAsync();
        }
        var pendente = await servico.DescartarVendaAsync(c.VendaId, null, "motivo", c.OperadorId);

        Assert.False(semMotivo.Sucesso);
        Assert.False(pendente.Sucesso);   // a pendente comum ainda vai sair: não descarta
    }

    [Fact]
    public async Task VendaDescartadaSemChaveNaoTravaOCaixaEAListaNaoInventaUmNome()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearVendaEmFalhaAsync(fixture);
        var servico = ServicoAberto(fixture);
        await servico.DescartarVendaAsync(c.VendaId, null, "cliente desistiu", c.OperadorId);

        var naoEnviadas = await servico.ContarNaoEnviadasAsync(c.CaixaId);
        var resumo = Assert.Single(await servico.ListarVendasDoCaixaAsync(c.CaixaId));

        Assert.Equal(0, naoEnviadas);
        Assert.Equal("Descartada: cliente desistiu", resumo.UltimoErroSync);
        Assert.DoesNotContain("por", resumo.UltimoErroSync);
    }

    [Fact]
    public async Task ComAExigenciaLigadaOPadraoContinuaRecusandoSemChave()
    {
        // O serviço, por padrão, segue com a regra completa: desligar é decisão da composição do app, não do serviço.
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearVendaEmFalhaAsync(fixture);

        var resultado = await new VendaLocalService(fixture.CriarContexto).DescartarVendaAsync(c.VendaId, null, "motivo", c.OperadorId);

        Assert.False(resultado.Sucesso);
    }

    // ---- a tela de Pedidos ----

    [Fact]
    public async Task TelaDePedidosNaoMostraChaveEHabilitaDescartarSoComMotivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearVendaEmFalhaAsync(fixture);
        var viewModel = new ListaPedidosViewModel(ServicoAberto(fixture), c.CaixaId, c.OperadorId);
        await viewModel.IniciarAsync();
        viewModel.VendaSelecionada = viewModel.Vendas.Single();
        var pode = true;
        using var inscricao = viewModel.DescartarCommand.CanExecute.Subscribe(v => pode = v);

        Assert.False(viewModel.ExigeChave);
        Assert.DoesNotContain("supervisor", viewModel.TextoDescarte, StringComparison.OrdinalIgnoreCase);
        Assert.False(pode);                        // sem motivo ainda

        viewModel.MotivoDescarte = "API recusa";
        Assert.True(pode);                         // sem chave, e pode
        await viewModel.DescartarCommand.Execute();

        Assert.Equal(SyncStatus.Descartada, viewModel.Vendas.Single().SyncStatus);
        Assert.Contains("descartada", viewModel.MensagemDescarte, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TelaDePedidosComExigenciaLigadaAindaPedeAChave()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearVendaEmFalhaAsync(fixture);
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), c.CaixaId, c.OperadorId);
        await viewModel.IniciarAsync();
        viewModel.VendaSelecionada = viewModel.Vendas.Single();
        viewModel.MotivoDescarte = "API recusa";
        var pode = true;
        using var inscricao = viewModel.DescartarCommand.CanExecute.Subscribe(v => pode = v);

        Assert.True(viewModel.ExigeChave);
        Assert.Contains("supervisor", viewModel.TextoDescarte, StringComparison.OrdinalIgnoreCase);
        Assert.False(pode);   // falta a chave
    }

    // ---- as Configurações ----

    private static ConfiguracaoService ConfiguracaoService(SqliteInMemoryFixture fixture, bool exigirChave)
    {
        var segredoProtector = new SegredoProtector();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        return new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(httpClient, segredoProtector), segredoProtector, exigirChave);
    }

    [Fact]
    public async Task ConfiguracoesAbremDiretoPeloBotaoMasComVoltar()
    {
        using var fixture = new SqliteInMemoryFixture();

        var viewModel = new ConfiguracoesViewModel(ConfiguracaoService(fixture, exigirChave: false), exigirSupervisor: true);
        await viewModel.IniciarAsync();

        Assert.False(viewModel.Bloqueada);
        Assert.True(viewModel.Liberada);
        Assert.True(viewModel.PodeVoltar);

        viewModel.CodigoPdv = "07";
        await viewModel.SalvarCommand.Execute();   // salva sem nenhuma chave

        using var leitura = fixture.CriarContexto();
        Assert.Equal("07", leitura.ConfiguracoesSincronizacao.Single().CodigoPdv);
    }

    [Fact]
    public async Task ConfiguracoesComExigenciaLigadaContinuamBloqueadas()
    {
        using var fixture = new SqliteInMemoryFixture();

        var viewModel = new ConfiguracoesViewModel(ConfiguracaoService(fixture, exigirChave: true), exigirSupervisor: true);

        Assert.True(viewModel.Bloqueada);
    }

    [Fact]
    public async Task BotaoDaBarraAbreAsConfiguracoesJaLiberadasQuandoAExigenciaEstaDesligada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
            context.Funcionarios.Add(new Funcionario { Nome = "Carlos", IdExterno = 2, PdvKeyHash = PdvKeyTeste.Hash("1234") });
            await context.SaveChangesAsync();
        }
        var shell = new ShellViewModel(
            ConfiguracaoService(fixture, exigirChave: false),
            new LoginOperadorService(fixture.CriarContexto),
            new CaixaService(fixture.CriarContexto),
            new DashboardService(fixture.CriarContexto),
            new VendaService(fixture.CriarContexto),
            new CatalogoLocalService(fixture.CriarContexto),
            new VendaLocalService(fixture.CriarContexto),
            new CadastroLocalService(fixture.CriarContexto));
        await shell.IniciarAsync();
        var login = (LoginViewModel)shell.CurrentViewModel!;
        login.OperadorSelecionado = login.Operadores[0];
        login.PdvKeyDigitada = "1234";
        var saiuDoLogin = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
        await login.EntrarCommand.Execute();
        await saiuDoLogin;

        await shell.IrParaConfiguracoesCommand.Execute();

        var configuracoes = Assert.IsType<ConfiguracoesViewModel>(shell.CurrentViewModel);
        Assert.False(configuracoes.Bloqueada);
    }

    // ---- a composição do app usa o interruptor ----

    [Fact]
    public void ComposicaoDoAppUsaOInterruptorUnico()
    {
        // Sem este teste, esquecer de repassar PoliticaSupervisor.ExigirChave a um serviço deixaria ele exigindo (ou
        // não) a chave independentemente do interruptor — e religar viraria "trocar a constante e torcer".
        var arquivo = Path.Combine(Path.GetTempPath(), $"pdv-teste-{Guid.NewGuid():N}.db");
        try
        {
            var servicos = new AppServices(arquivo);

            Assert.Equal(PoliticaSupervisor.ExigirChave, servicos.VendaLocalService.ExigeChaveSupervisor);
            Assert.Equal(PoliticaSupervisor.ExigirChave, servicos.ConfiguracaoService.ExigeChaveSupervisor);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { arquivo, arquivo + "-wal", arquivo + "-shm" })
                if (File.Exists(f)) File.Delete(f);
        }
    }
}

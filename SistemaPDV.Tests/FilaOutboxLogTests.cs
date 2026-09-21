using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using ReactiveUI;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Painel lateral da Fila Outbox: o log de atividade (em linguagem de operador), o painel e o botão de sincronizar agora.
[SupportedOSPlatform("windows")]
public class FilaOutboxLogTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";
    private const string PaginaVazia = """{ "current_page": 1, "data": [], "next_page_url": null, "total": 0, "date_sync": 1758000000 }""";

    // ---- o log em si ----

    private sealed class RelogioFalso : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = new(2026, 9, 21, 14, 30, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Agora;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void LogGuardaAMaisRecentePrimeiroEAvisaQuemAssina()
    {
        var log = new LogDeSincronizacao(new RelogioFalso());
        var recebidas = new List<EntradaDeLog>();
        using var assinatura = log.Novas.Subscribe(recebidas.Add);

        log.Registrar(NivelAtividade.Info, "primeira");
        log.Registrar(NivelAtividade.Sucesso, "segunda");

        Assert.Equal(new[] { "segunda", "primeira" }, log.Recentes.Select(e => e.Texto));
        Assert.Equal(new[] { "primeira", "segunda" }, recebidas.Select(e => e.Texto));   // quem assina recebe na ordem em que aconteceu
        Assert.Equal(new DateTime(2026, 9, 21, 14, 30, 0), log.Recentes[0].Quando);
    }

    [Fact]
    public void LogNaoCrescePraSempreGuardaSoAsUltimas()
    {
        var log = new LogDeSincronizacao();

        for (var i = 1; i <= LogDeSincronizacao.Capacidade + 20; i++)
            log.Registrar(NivelAtividade.Info, $"linha {i}");

        Assert.Equal(LogDeSincronizacao.Capacidade, log.Recentes.Count);
        Assert.Equal($"linha {LogDeSincronizacao.Capacidade + 20}", log.Recentes[0].Texto);
        Assert.Equal("linha 21", log.Recentes[^1].Texto);                                // as 20 mais antigas saíram
    }

    // ---- o que o serviço de sincronização conta ----

    private sealed class RedeFake
    {
        public bool Ligada { get; set; } = true;
        public HttpStatusCode RespostaDaVenda { get; set; } = HttpStatusCode.OK;

        public HttpClient CriarHttpClient() => FakeHttpMessageHandler.CriarHttpClient(requisicao =>
        {
            if (!Ligada)
                throw new HttpRequestException("sem rede");

            var caminho = requisicao.RequestUri!.AbsolutePath;
            if (requisicao.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (caminho.EndsWith("/authentication/token"))
                return Json(HttpStatusCode.OK, """{ "data": { "token": "token-fake" } }""");
            if (requisicao.Method == HttpMethod.Post && caminho.EndsWith("/vendas"))
                return RespostaDaVenda == HttpStatusCode.OK
                    ? Json(HttpStatusCode.OK, """{ "data": { "id": 999 } }""")
                    : Json(RespostaDaVenda, """{ "message": "cliente_id inválido" }""");
            return Json(HttpStatusCode.OK, PaginaVazia);
        });

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static async Task<int> SemearVendaPendenteAsync(SqliteInMemoryFixture fixture, bool comEmpresa = true)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
        var produto = new Produto { Nome = "Refrigerante", PrecoVenda = 9.50m, PrecoCompra = 6m, IdExterno = 206, ProdutoIdApi = 77 };
        var forma = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
        context.AddRange(funcionario, produto, forma);
        if (comEmpresa)
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
        {
            UrlApi = UrlApi, ApiClienteId = "1", ApiClienteSecretProtegido = new SegredoProtector().Proteger("segredo"),
            ClienteConsumidorFinalIdExterno = 1, CodigoPdv = "01",
        });
        await context.SaveChangesAsync();
        var caixa = new Models.Caixa
        {
            FuncionarioId = funcionario.Id, IdExterno = 28, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1,
            DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0), TrocoInicial = 10m, AberturaSincronizada = true,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixa.Id, null, new[] { (produto.Id, 1m, 9.50m, 0m, 0m) }, new[] { (forma.Id, 9.50m) });
        return venda.NumeroPedido;
    }

    private static SincronizacaoBackgroundService CriarService(SqliteInMemoryFixture fixture, RedeFake rede, bool comVerificador = false)
    {
        var http = rede.CriarHttpClient();
        var apiClient = new SoftcomApiClient(http);
        var segredo = new SegredoProtector();
        var auth = new SoftcomAuthService(http, segredo);
        return new SincronizacaoBackgroundService(
            fixture.CriarContexto,
            auth,
            new CatalogSyncService(fixture.CriarContexto, apiClient, segredo, auth),
            new CaixaSyncService(fixture.CriarContexto, apiClient),
            new VendaSyncService(fixture.CriarContexto, apiClient),
            verificador: comVerificador ? new VerificadorDeConexao(http) : null);
    }

    [Fact]
    public async Task EnvioContaOQueVaiSairEQuePedidoFoiSincronizado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var numero = await SemearVendaPendenteAsync(fixture);
        using var service = CriarService(fixture, new RedeFake());

        await service.ExecutarCicloOutboxAsync();

        var textos = service.Log.Recentes.Select(e => e.Texto).Reverse().ToList();   // na ordem em que aconteceu
        Assert.Equal("Disparando sincronização de 1 item(ns) pendente(s)...", textos[0]);
        Assert.Contains($"POST /vendas: Pedido #{numero} sincronizado com sucesso!", textos);
        Assert.Equal(NivelAtividade.Sucesso, service.Log.Recentes.First(e => e.Texto.Contains("sincronizado com sucesso")).Nivel);
    }

    [Fact]
    public async Task PedidoRecusadoPelaApiApareceComOMotivoEComoAviso()
    {
        using var fixture = new SqliteInMemoryFixture();
        var numero = await SemearVendaPendenteAsync(fixture);
        using var service = CriarService(fixture, new RedeFake { RespostaDaVenda = HttpStatusCode.UnprocessableEntity });

        await service.ExecutarCicloOutboxAsync();

        var linha = service.Log.Recentes.First(e => e.Texto.StartsWith($"Pedido #{numero} não foi enviado"));
        Assert.Equal(NivelAtividade.Aviso, linha.Nivel);
        Assert.Contains("cliente_id inválido", linha.Texto);
    }

    [Fact]
    public async Task PedidoEsperandoDependenciaDizQualEmVezDeSumirEmSilencio()
    {
        using var fixture = new SqliteInMemoryFixture();
        var numero = await SemearVendaPendenteAsync(fixture, comEmpresa: false);
        using var service = CriarService(fixture, new RedeFake());

        await service.ExecutarCicloOutboxAsync();

        Assert.Contains(service.Log.Recentes, e => e.Texto.StartsWith($"Pedido #{numero} não foi enviado") && e.Texto.Contains("Empresa"));
    }

    [Fact]
    public async Task SemNadaPendenteOCicloNaoEscreveNoLog()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearVendaPendenteAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            (await context.Vendas.SingleAsync()).SyncStatus = SyncStatus.Sincronizado;
            await context.SaveChangesAsync();
        }
        using var service = CriarService(fixture, new RedeFake());

        await service.ExecutarCicloOutboxAsync();

        Assert.Empty(service.Log.Recentes);                                       // o ciclo de 30 s ocioso não vira ruído
    }

    [Fact]
    public async Task QuedaEVoltaDaConexaoViramLinhasDoLogUmaVezCada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearVendaPendenteAsync(fixture);
        var rede = new RedeFake { Ligada = false };
        using var service = CriarService(fixture, rede, comVerificador: true);

        await service.VerificarConexaoAsync();
        await service.VerificarConexaoAsync();                                    // o mesmo estado repetido não repete a linha
        rede.Ligada = true;
        await service.VerificarConexaoAsync();

        var textos = service.Log.Recentes.Select(e => e.Texto).Reverse().ToList();
        Assert.Single(textos, t => t.StartsWith("Conexão perdida"));
        Assert.Single(textos, t => t.StartsWith("Conexão restabelecida"));
        Assert.True(textos.FindIndex(t => t.StartsWith("Conexão perdida")) < textos.FindIndex(t => t.StartsWith("Conexão restabelecida")));
        Assert.Equal(NivelAtividade.Aviso, service.Log.Recentes.Last(e => e.Texto.StartsWith("Conexão perdida")).Nivel);
    }

    [Fact]
    public async Task ONomeDoTokenNuncaVaiParaOLog()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearVendaPendenteAsync(fixture);
        using var service = CriarService(fixture, new RedeFake());

        await service.ExecutarCicloOutboxAsync();

        Assert.DoesNotContain(service.Log.Recentes, e => e.Texto.Contains("token-fake") || e.Texto.Contains("Bearer"));
    }

    // ---- o Shell e o painel ----

    private static ShellViewModel CriarShell(SqliteInMemoryFixture fixture) => new(
        new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(new HttpClient(), new SegredoProtector()), new SegredoProtector()),
        new LoginOperadorService(fixture.CriarContexto),
        new CaixaService(fixture.CriarContexto),
        new DashboardService(fixture.CriarContexto),
        new VendaService(fixture.CriarContexto),
        new CatalogoLocalService(fixture.CriarContexto),
        new VendaLocalService(fixture.CriarContexto),
        new CadastroLocalService(fixture.CriarContexto));

    [Fact]
    public async Task PainelAbreEFechaEEscComOPainelFechadoNaoFazNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);
        Assert.False(shell.PainelOutboxAberto);
        Assert.False(await shell.FecharPainelOutboxCommand.CanExecute.FirstAsync());

        await shell.AbrirPainelOutboxCommand.Execute();
        Assert.True(shell.PainelOutboxAberto);
        Assert.True(await shell.FecharPainelOutboxCommand.CanExecute.FirstAsync());

        await shell.FecharPainelOutboxCommand.Execute();
        Assert.False(shell.PainelOutboxAberto);
    }

    [Fact]
    public async Task AtividadesEntramDaMaisRecenteParaAMaisAntigaELimitadas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);
        Assert.True(shell.SemAtividades);

        for (var i = 1; i <= LogDeSincronizacao.Capacidade + 5; i++)
            shell.AdicionarAtividade(new EntradaDeLog(DateTime.Now, NivelAtividade.Info, $"linha {i}"));

        Assert.False(shell.SemAtividades);
        Assert.Equal(LogDeSincronizacao.Capacidade, shell.Atividades.Count);
        Assert.Equal($"linha {LogDeSincronizacao.Capacidade + 5}", shell.Atividades[0].Texto);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SincronizarAgoraPeloPainelPedeAoServicoERegistraNoLog()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);
        var pedidos = 0;
        using var assinatura = shell.SincronizacaoSolicitada.Subscribe(_ => pedidos++);

        shell.SincronizarAgoraCommand.Execute().Subscribe();
        await shell.SincronizarAgoraCommand.IsExecuting.Where(executando => !executando).FirstAsync();

        Assert.Equal(1, pedidos);
        Assert.Contains(shell.Atividades, a => a.Texto == "Sincronização solicitada pelo operador.");
    }

    [Theory]
    [InlineData(0, "0 itens")]
    [InlineData(1, "1 item")]
    [InlineData(5, "5 itens")]
    public async Task TextoDaFilaLocalDizAQuantidadePorExtenso(int pendentes, string esperado)
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture, comCaixaAberto: false);
        await using (var context = fixture.CriarContexto())
        {
            for (var i = 0; i < pendentes; i++)
                context.Clientes.Add(new Cliente { Nome = $"Cliente {i}", CpfCnpj = "52998224725", SyncStatus = SyncStatus.PendenteSync });
            await context.SaveChangesAsync();
        }

        shell.NotificarDadosSincronizados();                                      // o que o ciclo de sincronização dispara
        await shell.WhenAnyValue(s => s.PendentesSync).Where(n => n == pendentes).FirstAsync().Timeout(TimeSpan.FromSeconds(5));

        Assert.Equal(esperado, shell.TextoFilaLocal);
    }
}

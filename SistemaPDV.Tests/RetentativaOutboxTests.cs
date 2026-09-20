using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Os três outboxes (cliente, caixa, venda) seguem a mesma política: falhou -> espera
// crescente; passou do teto -> desiste até alguém pedir pra reenviar; deu certo -> zera.
[SupportedOSPlatform("windows")]
public class RetentativaOutboxTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    // Conta as requisições que chegam à "API" e responde o que o teste mandar.
    private sealed class ApiContada
    {
        public int Chamadas { get; private set; }
        public Func<HttpResponseMessage> Resposta { get; set; } = () => Json(HttpStatusCode.UnprocessableEntity, """{ "errors": { "nome": ["invalido"] } }""");

        public HttpClient CriarHttpClient() => FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            Chamadas++;
            return Resposta();
        });
    }

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi, ClienteConsumidorFinalIdExterno = 1 });
        await context.SaveChangesAsync();
    }

    private static CatalogSyncService CriarCatalogo(SqliteInMemoryFixture fixture, HttpClient httpClient, RelogioFalso relogio) =>
        new(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(),
            new SoftcomAuthService(httpClient, new SegredoProtector()), relogio);

    private static async Task<int> SemearClienteAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var cliente = new Cliente { Nome = "Maria Souza", CpfCnpj = "52998224725" };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();
        return cliente.Id;
    }

    // ---------------- cliente ----------------

    [Fact]
    public async Task ClienteRejeitadoAgendaAProximaTentativaComEsperaCrescente()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClienteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var catalogo = CriarCatalogo(fixture, api.CriarHttpClient(), relogio);

        await catalogo.SincronizarClientesNovosPendentesAsync("token");
        using (var leitura = fixture.CriarContexto())
        {
            var cliente = leitura.Clientes.Single();
            Assert.Equal(SyncStatus.FalhaSync, cliente.SyncStatus);
            Assert.Equal(1, cliente.TentativasEnvio);
            Assert.Equal(relogio.AgoraUtc.AddSeconds(30), cliente.ProximaTentativaEm);
        }

        relogio.Avancar(TimeSpan.FromSeconds(31));
        await catalogo.SincronizarClientesNovosPendentesAsync("token");
        using (var leitura = fixture.CriarContexto())
        {
            var cliente = leitura.Clientes.Single();
            Assert.Equal(2, cliente.TentativasEnvio);
            Assert.Equal(relogio.AgoraUtc.AddSeconds(60), cliente.ProximaTentativaEm);
        }
    }

    [Fact]
    public async Task LoteNaoReenviaEnquantoEstaEmEspera()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClienteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var catalogo = CriarCatalogo(fixture, api.CriarHttpClient(), relogio);
        await catalogo.SincronizarClientesNovosPendentesAsync("token");
        Assert.Equal(1, api.Chamadas);

        relogio.Avancar(TimeSpan.FromSeconds(10));   // ainda dentro dos 30 s de espera
        await catalogo.SincronizarClientesNovosPendentesAsync("token");

        Assert.Equal(1, api.Chamadas);
    }

    [Fact]
    public async Task DepoisDoTetoDeTentativasDesisteEGuardaOMotivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClienteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var catalogo = CriarCatalogo(fixture, api.CriarHttpClient(), relogio);

        for (var i = 0; i < PoliticaRetentativa.MaximoTentativas + 5; i++)
        {
            await catalogo.SincronizarClientesNovosPendentesAsync("token");
            relogio.Avancar(TimeSpan.FromMinutes(15));   // sempre passa da maior espera
        }

        Assert.Equal(PoliticaRetentativa.MaximoTentativas, api.Chamadas);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(SyncStatus.FalhaSync, cliente.SyncStatus);
        Assert.Contains("parou de tentar", cliente.UltimoErroSync);
    }

    [Fact]
    public async Task SucessoDepoisDeFalhaZeraOsContadores()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClienteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var catalogo = CriarCatalogo(fixture, api.CriarHttpClient(), relogio);
        await catalogo.SincronizarClientesNovosPendentesAsync("token");

        relogio.Avancar(TimeSpan.FromMinutes(1));
        api.Resposta = () => Json(HttpStatusCode.OK, """{ "data": { "id": 77 } }""");
        await catalogo.SincronizarClientesNovosPendentesAsync("token");

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(SyncStatus.Sincronizado, cliente.SyncStatus);
        Assert.Equal(0, cliente.TentativasEnvio);
        Assert.Null(cliente.ProximaTentativaEm);
    }

    [Fact]
    public async Task ReenviarFalhasDeClienteDevolveOExaustoAFila()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClienteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var catalogo = CriarCatalogo(fixture, api.CriarHttpClient(), relogio);
        for (var i = 0; i < PoliticaRetentativa.MaximoTentativas; i++)
        {
            await catalogo.SincronizarClientesNovosPendentesAsync("token");
            relogio.Avancar(TimeSpan.FromMinutes(15));
        }
        var chamadasAntes = api.Chamadas;
        var cadastro = new CadastroLocalService(fixture.CriarContexto);

        var reenviados = await cadastro.ReenviarFalhasAsync();
        await catalogo.SincronizarClientesNovosPendentesAsync("token");

        Assert.Equal(1, reenviados);
        Assert.Equal(chamadasAntes + 1, api.Chamadas);
    }

    [Fact]
    public async Task EnviarUmClienteDiretoIgnoraAEsperaMasContaATentativa()
    {
        // SincronizarClienteNovoAsync(id) é o envio explícito de UM cliente; só o lote
        // (o que o timer chama) respeita a espera.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var catalogo = CriarCatalogo(fixture, api.CriarHttpClient(), relogio);

        await catalogo.SincronizarClienteNovoAsync(id, "token");
        await catalogo.SincronizarClienteNovoAsync(id, "token");

        Assert.Equal(2, api.Chamadas);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(2, leitura.Clientes.Single().TentativasEnvio);
    }

    // ---------------- caixa ----------------

    private static async Task SemearCaixaPendenteAsync(SqliteInMemoryFixture fixture)
    {
        await using (var context = fixture.CriarContexto())
        {
            context.Funcionarios.Add(new Funcionario { Nome = "Carlos Silva", IdExterno = 2 });
            await context.SaveChangesAsync();
        }

        await using var leitura = fixture.CriarContexto();
        var funcionarioId = leitura.Funcionarios.Single().Id;
        await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 20), 1, 10m);
    }

    [Fact]
    public async Task CaixaRejeitadoEntraEmEsperaEDesisteNoTeto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCaixaPendenteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(api.CriarHttpClient()), relogio);

        await service.SincronizarCaixaPendenteAsync("token");
        await service.SincronizarCaixaPendenteAsync("token");   // dentro da espera: não envia
        Assert.Equal(1, api.Chamadas);

        for (var i = 0; i < PoliticaRetentativa.MaximoTentativas + 5; i++)
        {
            relogio.Avancar(TimeSpan.FromMinutes(15));
            await service.SincronizarCaixaPendenteAsync("token");
        }

        Assert.Equal(PoliticaRetentativa.MaximoTentativas, api.Chamadas);
        using var leitura = fixture.CriarContexto();
        Assert.Contains("parou de tentar", leitura.Caixas.Single().UltimoErroSync);
    }

    [Fact]
    public async Task CaixaQueSincronizaZeraAsTentativasParaOFechamentoComecarDoZero()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCaixaPendenteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(api.CriarHttpClient()), relogio);
        await service.SincronizarCaixaPendenteAsync("token");

        relogio.Avancar(TimeSpan.FromMinutes(1));
        api.Resposta = () => Json(HttpStatusCode.OK, """{ "data": { "success": { "id": 24 } } }""");
        await service.SincronizarCaixaPendenteAsync("token");

        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.True(caixa.AberturaSincronizada);
        Assert.Equal(0, caixa.TentativasEnvio);
        Assert.Null(caixa.ProximaTentativaEm);
    }

    // ---------------- venda ----------------

    private static async Task SemearVendaPendenteAsync(SqliteInMemoryFixture fixture)
    {
        int caixaId, produtoId, formaId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
            var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m, IdExterno = 10, ProdutoIdApi = 100 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
            context.AddRange(funcionario, produto, forma);
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
            await context.SaveChangesAsync();

            var caixa = new Models.Caixa
            {
                FuncionarioId = funcionario.Id,
                IdExterno = 24,
                DataCaixa = new DateOnly(2026, 9, 20),
                Turno = 1,
                DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0),
                TrocoInicial = 10m,
                AberturaSincronizada = true,
            };
            context.Caixas.Add(caixa);
            await context.SaveChangesAsync();
            (caixaId, produtoId, formaId) = (caixa.Id, produto.Id, forma.Id);
        }

        await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaId, 9.90m) });
    }

    [Fact]
    public async Task VendaRejeitadaEntraEmEsperaEDesisteNoTeto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearVendaPendenteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(api.CriarHttpClient()), relogio);

        await service.SincronizarVendasPendentesAsync("token");
        using (var leitura = fixture.CriarContexto())
            Assert.Equal(relogio.AgoraUtc.AddSeconds(30), leitura.Vendas.Single().ProximaTentativaEm);
        await service.SincronizarVendasPendentesAsync("token");   // dentro da espera
        Assert.Equal(1, api.Chamadas);

        for (var i = 0; i < PoliticaRetentativa.MaximoTentativas + 5; i++)
        {
            relogio.Avancar(TimeSpan.FromMinutes(15));
            await service.SincronizarVendasPendentesAsync("token");
        }

        Assert.Equal(PoliticaRetentativa.MaximoTentativas, api.Chamadas);
        using var final = fixture.CriarContexto();
        Assert.Equal(PoliticaRetentativa.MaximoTentativas, final.Vendas.Single().TentativasEnvio);
        Assert.Contains("parou de tentar", final.Vendas.Single().UltimoErroSync);
    }

    [Fact]
    public async Task ReenviarFalhasDoCaixaDevolveVendasECaixaAFila()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearVendaPendenteAsync(fixture);
        var relogio = new RelogioFalso();
        var api = new ApiContada();
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(api.CriarHttpClient()), relogio);
        for (var i = 0; i < PoliticaRetentativa.MaximoTentativas; i++)
        {
            await service.SincronizarVendasPendentesAsync("token");
            relogio.Avancar(TimeSpan.FromMinutes(15));
        }
        int caixaId;
        using (var leitura = fixture.CriarContexto())
            caixaId = leitura.Caixas.Single().Id;
        var chamadasAntes = api.Chamadas;

        var reenviados = await new VendaLocalService(fixture.CriarContexto).ReenviarFalhasAsync(caixaId);
        await service.SincronizarVendasPendentesAsync("token");

        Assert.Equal(1, reenviados);
        Assert.Equal(chamadasAntes + 1, api.Chamadas);
    }
}

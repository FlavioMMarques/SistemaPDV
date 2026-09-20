using System.Net;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Revisão de segurança da Task 48 (skill security-and-hardening): a resposta da API
// e a URL configurada são dados não confiáveis. Portal cativo de wi-fi responde 200
// com HTML; um UrlApi com http:// mandaria token e dados pessoais em texto puro.
// Cobre caixa (abertura e fechamento) e venda — o cliente tem os seus em
// CatalogSyncServiceClienteNovoTests.
public class SincronizacaoRespostaHostilTests
{
    private const string UrlHttps = "https://exemplo.softcomshop.com.br/registrar?client_id=1";
    private const string UrlHttp = "http://exemplo.softcomshop.com.br/registrar?client_id=1";
    private const string Html = "<html><body>Faça login no wi-fi da loja</body></html>";

    private static HttpResponseMessage Resposta(HttpStatusCode status, string conteudo) => new(status)
    {
        Content = new StringContent(conteudo, Encoding.UTF8, "text/html"),
    };

    private static async Task<int> SemearCaixaAbertoAsync(SqliteInMemoryFixture fixture, string urlApi)
    {
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
            context.Funcionarios.Add(funcionario);
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = urlApi });
            await context.SaveChangesAsync();
        }

        await using var leitura = fixture.CriarContexto();
        var funcionarioId = leitura.Funcionarios.Single().Id;
        var resultado = await new CaixaService(fixture.CriarContexto)
            .AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 20), 1, 10m);
        return resultado.Valor!.Id;
    }

    private static async Task<Guid> SemearVendaPendenteAsync(SqliteInMemoryFixture fixture, string urlApi)
    {
        int caixaId, produtoId, formaId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
            var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m, IdExterno = 10 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
            context.AddRange(funcionario, produto, forma);
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = urlApi, ClienteConsumidorFinalIdExterno = 1 });
            await context.SaveChangesAsync();

            var caixa = new Caixa
            {
                FuncionarioId = funcionario.Id,
                IdExterno = 24,
                DataCaixa = new DateOnly(2026, 9, 20),
                Turno = 1,
                DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0),
                TrocoInicial = 10m,
            };
            context.Caixas.Add(caixa);
            await context.SaveChangesAsync();
            (caixaId, produtoId, formaId) = (caixa.Id, produto.Id, forma.Id);
        }

        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaId, 9.90m) });
        return venda.Id;
    }

    [Fact]
    public async Task AberturaDeCaixaComRespostaOkQueNaoEhJsonMarcaFalhaSemLancar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaAbertoAsync(fixture, UrlHttps);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, Html));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.Equal(SyncStatus.FalhaSync, caixa.SyncStatus);
        Assert.False(caixa.AberturaSincronizada);
    }

    [Fact]
    public async Task AberturaDeCaixaComCorpoGiganteGravaMensagemTruncada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaAbertoAsync(fixture, UrlHttps);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, new string('x', 200_000)));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        await service.SincronizarAberturaAsync("token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.True(leitura.Caixas.Single().UltimoErroSync!.Length <= 500);
    }

    [Fact]
    public async Task VendaComRespostaOkQueNaoEhJsonMarcaFalhaSemLancar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture, UrlHttps);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, Html));
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.FalhaSync, venda.SyncStatus);
        Assert.Null(venda.VendaIdExterno);
    }

    [Fact]
    public async Task VendaComCorpoGiganteGravaMensagemTruncada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture, UrlHttps);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, new string('x', 200_000)));
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        await service.SincronizarVendaAsync(vendaId, "token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.True(leitura.Vendas.Single().UltimoErroSync!.Length <= 500);
    }

    [Fact]
    public async Task AberturaDeCaixaEmHttpNaoEnviaNadaENaoMarcaOCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaAbertoAsync(fixture, UrlHttp);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, "{}"); });
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.Equal(SyncStatus.PendenteSync, caixa.SyncStatus);
        Assert.False(caixa.AberturaSincronizada);
        Assert.Null(caixa.UltimoErroSync);
    }

    [Fact]
    public async Task FechamentoDeCaixaEmHttpNaoViraSincronizadoSemTerSidoEnviado()
    {
        // O risco concreto de acrescentar um valor ao enum: o fechamento tratava
        // "tudo que não é Falha/TokenExpirado" como sucesso — uma conexão recusada
        // marcaria como sincronizado algo que nunca saiu da máquina.
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAbertoAsync(fixture, UrlHttp);
        await new CaixaService(fixture.CriarContexto).FecharCaixaLocalAsync(caixaId, 10m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());
        await using (var context = fixture.CriarContexto())
        {
            var caixa = context.Caixas.Single();
            caixa.AberturaSincronizada = true;
            await context.SaveChangesAsync();
        }
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, "{}"); });
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarFechamentoAsync("token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        Assert.NotEqual(SyncStatus.Sincronizado, leitura.Caixas.Single().SyncStatus);
    }

    [Fact]
    public async Task VendaEmHttpNaoEnviaNadaENaoMarcaNemContaTentativa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture, UrlHttp);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, "{}"); });
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.PendenteSync, venda.SyncStatus);
        Assert.Equal(0, venda.TentativasEnvio);
        Assert.Null(venda.UltimoErroSync);
    }
}

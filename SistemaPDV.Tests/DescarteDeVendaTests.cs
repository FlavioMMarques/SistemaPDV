using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Descartar uma venda que a API nunca aceita (erro permanente) — senão ela trava o fechamento do caixa para sempre.
// Decisões do usuário (2026-09-20): só SUPERVISOR, com a chave dele; a venda descartada NÃO entra no esperado do
// fechamento (é tratada como cancelada); só vendas EM FALHA podem ser descartadas (a pendente comum ainda vai sair).
[SupportedOSPlatform("windows")]
public class DescarteDeVendaTests
{
    private const string ChaveSupervisor = "9999";
    private const string ChaveOperador = "1234";
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private sealed record Cenario(int CaixaId, Guid VendaId, int OperadorId, int SupervisorId, int FormaId, int ProdutoId);

    private static async Task<Cenario> SemearAsync(SqliteInMemoryFixture fixture, SyncStatus statusDaVenda = SyncStatus.FalhaSync)
    {
        int operadorId, supervisorId, formaId, produtoId;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2, PdvKeyHash = PdvKeyTeste.Hash(ChaveOperador) };
            var supervisor = new Funcionario { Nome = "Chefe", IdExterno = 3, Supervisor = true, PdvKeyHash = PdvKeyTeste.Hash(ChaveSupervisor) };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", CodigoNfce = "17", IdExterno = 5 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 10m, IdExterno = 10, ProdutoIdApi = 100 };
            context.AddRange(operador, supervisor, forma, produto);
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi, ClienteConsumidorFinalIdExterno = 1 });
            await context.SaveChangesAsync();
            (operadorId, supervisorId, formaId, produtoId) = (operador.Id, supervisor.Id, forma.Id, produto.Id);
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 20), 1, 10m)).Valor!;
        await using (var context = fixture.CriarContexto())
        {
            (await context.Caixas.SingleAsync()).AberturaSincronizada = true;
            await context.SaveChangesAsync();
        }
        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixa.Id, null, new[] { (produtoId, 1m, 10m, 0m, 0m) }, new[] { (formaId, 10m) });
        await DefinirStatusAsync(fixture, venda.Id, statusDaVenda);
        return new Cenario(caixa.Id, venda.Id, operadorId, supervisorId, formaId, produtoId);
    }

    private static async Task DefinirStatusAsync(SqliteInMemoryFixture fixture, Guid vendaId, SyncStatus status)
    {
        await using var context = fixture.CriarContexto();
        (await context.Vendas.SingleAsync(v => v.Id == vendaId)).SyncStatus = status;
        await context.SaveChangesAsync();
    }

    private static VendaLocalService Servico(SqliteInMemoryFixture fixture) => new(fixture.CriarContexto);

    // ---- o descarte em si ----

    [Fact]
    public async Task SupervisorDescartaVendaEmFalhaComMotivoEFicaRegistrado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);

        var resultado = await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "API recusa o produto; venda cancelada no balcão", c.OperadorId);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.Descartada, venda.SyncStatus);
        Assert.NotNull(venda.DescartadaEm);
        Assert.Equal(c.SupervisorId, venda.DescartadaPorId);     // quem autorizou
        Assert.Equal(c.OperadorId, venda.SolicitadaPorId);       // quem pediu
        Assert.Equal("API recusa o produto; venda cancelada no balcão", venda.MotivoDescarte);
    }

    [Fact]
    public async Task ChaveDeOperadorQueNaoESupervisorNaoDescarta()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);

        var resultado = await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveOperador, "motivo qualquer", c.OperadorId);

        Assert.False(resultado.Sucesso);
        Assert.Contains("supervisor", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.FalhaSync, leitura.Vendas.Single().SyncStatus);
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ChaveErradaOuVaziaNaoDescarta(string? chave)
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);

        var resultado = await Servico(fixture).DescartarVendaAsync(c.VendaId, chave, "motivo qualquer", c.OperadorId);

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.FalhaSync, leitura.Vendas.Single().SyncStatus);
    }

    [Fact]
    public async Task SupervisorDesativadoNaoDescarta()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            (await context.Funcionarios.SingleAsync(f => f.Id == c.SupervisorId)).Desativado = true;
            await context.SaveChangesAsync();
        }

        var resultado = await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "motivo qualquer", c.OperadorId);

        Assert.False(resultado.Sucesso);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SemMotivoNaoDescarta(string? motivo)
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);

        var resultado = await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, motivo, c.OperadorId);

        Assert.False(resultado.Sucesso);
        Assert.Contains("motivo", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SyncStatus.PendenteSync)]
    [InlineData(SyncStatus.Sincronizado)]
    [InlineData(SyncStatus.Descartada)]
    public async Task SoVendaEmFalhaPodeSerDescartada(SyncStatus status)
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture, status);

        var resultado = await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "motivo qualquer", c.OperadorId);

        Assert.False(resultado.Sucesso);
        Assert.Contains("falha", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(status, leitura.Vendas.Single().SyncStatus);
        Assert.Null(leitura.Vendas.Single().MotivoDescarte);
    }

    [Fact]
    public async Task VendaInexistenteDevolveFalha()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);

        var resultado = await Servico(fixture).DescartarVendaAsync(Guid.NewGuid(), ChaveSupervisor, "motivo", null);

        Assert.False(resultado.Sucesso);
    }

    // ---- efeitos: a venda descartada sai de todo lugar que conta/soma/envia vendas ----

    [Fact]
    public async Task VendaDescartadaNaoImpedeMaisOFechamentoDoCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        var bloqueado = await new CaixaService(fixture.CriarContexto).FecharCaixaLocalAsync(c.CaixaId, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());
        Assert.False(bloqueado.Sucesso);   // antes de descartar: travado pela venda em falha

        await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "motivo qualquer", c.OperadorId);
        var fechado = await new CaixaService(fixture.CriarContexto).FecharCaixaLocalAsync(c.CaixaId, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        Assert.True(fechado.Sucesso, fechado.Mensagem);
    }

    [Fact]
    public async Task VendaDescartadaNaoContaComoNaoEnviadaNemEntraNoEsperado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "motivo qualquer", c.OperadorId);

        Assert.Equal(0, await Servico(fixture).ContarNaoEnviadasAsync(c.CaixaId));
        Assert.Empty(await Servico(fixture).TotaisPorFormaPagamentoAsync(c.CaixaId));   // tratada como cancelada
    }

    [Fact]
    public async Task VendaDescartadaSaiDoFaturamentoEDosPendentesDoDashboard()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        var antes = await new DashboardService(fixture.CriarContexto).ObterResumoAsync(c.CaixaId);
        Assert.Equal(10m, antes.FaturamentoHoje);
        Assert.True(antes.PendentesOutbox >= 1);

        await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "motivo qualquer", c.OperadorId);
        var depois = await new DashboardService(fixture.CriarContexto).ObterResumoAsync(c.CaixaId);

        Assert.Equal(0m, depois.FaturamentoHoje);
        Assert.Equal(antes.PendentesOutbox - 1, depois.PendentesOutbox);   // só a venda descartada saiu da contagem
    }

    [Fact]
    public async Task VendaDescartadaNuncaMaisEEnviada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "motivo qualquer", c.OperadorId);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamou = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": { "id": 1 } }""", Encoding.UTF8, "application/json") };
        });
        var vendaSync = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var lote = await vendaSync.SincronizarVendasPendentesAsync("t");
        var direto = await vendaSync.SincronizarVendaAsync(c.VendaId, "t");

        Assert.Equal(0, lote.Quantidade);
        Assert.False(direto.Sucesso);
        Assert.False(chamou);
    }

    [Fact]
    public async Task ListagemMostraAVendaDescartadaComOMotivoParaAuditoria()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        await Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "cliente desistiu", c.OperadorId);

        var vendas = await Servico(fixture).ListarVendasDoCaixaAsync(c.CaixaId);

        var resumo = Assert.Single(vendas);
        Assert.Equal(SyncStatus.Descartada, resumo.SyncStatus);
        Assert.Contains("cliente desistiu", resumo.UltimoErroSync);
        Assert.Contains("Chefe", resumo.UltimoErroSync);   // quem autorizou fica visível
    }

    // ---- descarte no MEIO de um envio (o supervisor descarta enquanto o ciclo de sincronização já está tentando) ----

    // O handler do "servidor" faz o descarte no meio da requisição: é exatamente a janela entre o envio ter lido a
    // venda (ainda FalhaSync) e gravar o resultado.
    private static VendaSyncService EnvioQueSofreDescarteNoMeio(SqliteInMemoryFixture fixture, Cenario c, HttpStatusCode statusDaApi, string corpoDaApi)
    {
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            var descarte = Servico(fixture).DescartarVendaAsync(c.VendaId, ChaveSupervisor, "descartada no meio do envio", c.OperadorId).GetAwaiter().GetResult();
            Assert.True(descarte.Sucesso, descarte.Mensagem);
            return new HttpResponseMessage(statusDaApi) { Content = new StringContent(corpoDaApi, Encoding.UTF8, "application/json") };
        });
        return new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));
    }

    [Fact]
    public async Task FalhaDaApiDepoisDeUmDescarteNoMeioNaoMexeNaVendaDescartada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        var envio = EnvioQueSofreDescarteNoMeio(fixture, c, HttpStatusCode.UnprocessableEntity, """{"errors":{"quantidade":["inválida"]}}""");

        var resultado = await envio.SincronizarVendaAsync(c.VendaId, "t");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.Descartada, venda.SyncStatus);
        Assert.Equal(0, venda.TentativasEnvio);            // a tentativa que falhou depois do descarte não é contada
        Assert.Null(venda.UltimoErroSync);                 // nem grava erro em cima de uma venda já descartada
        Assert.Equal("descartada no meio do envio", venda.MotivoDescarte);
    }

    [Fact]
    public async Task SeAApiAceitouAVendaAntesDoDescarteAVerdadeDaApiVence()
    {
        // O descarte aconteceu mas a venda JÁ tinha chegado à API: ela existe lá, então localmente fica Sincronizado
        // (senão sairia do "esperado" do caixa por engano). A auditoria do pedido de descarte é preservada.
        using var fixture = new SqliteInMemoryFixture();
        var c = await SemearAsync(fixture);
        var envio = EnvioQueSofreDescarteNoMeio(fixture, c, HttpStatusCode.OK, """{ "data": { "id": 900 } }""");

        var resultado = await envio.SincronizarVendaAsync(c.VendaId, "t");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.Sincronizado, venda.SyncStatus);
        Assert.Equal(900, venda.VendaIdExterno);
        Assert.NotNull(venda.DescartadaEm);
        Assert.Equal(0, await Servico(fixture).ContarNaoEnviadasAsync(c.CaixaId));   // nada trava o fechamento do caixa
    }
}

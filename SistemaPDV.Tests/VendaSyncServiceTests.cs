using System.Net;
using System.Text;
using SistemaPDV.Services.Sync;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Tests;

public class VendaSyncServiceTests
{
    private static async Task<Guid> SemearVendaPendenteAsync(
        SqliteInMemoryFixture fixture,
        bool empresaSincronizada = true,
        int? clienteId = null,
        bool clienteSincronizado = true,
        int? clienteConsumidorFinalIdExterno = 1,
        bool produtoSincronizado = true,
        bool formaPagamentoSincronizada = true)
    {
        await using var context = fixture.CriarContexto();

        var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
        var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m, IdExterno = produtoSincronizado ? 10 : null, ProdutoIdApi = produtoSincronizado ? 100 : null };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = formaPagamentoSincronizada ? 5 : null };
        context.AddRange(funcionario, produto, forma);

        if (empresaSincronizada)
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });

        Cliente? cliente = null;
        if (clienteId is not null)
        {
            cliente = new Cliente { Nome = "Cliente Teste", IdExterno = clienteSincronizado ? 99 : null };
            context.Clientes.Add(cliente);
        }

        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
        {
            UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
            ClienteConsumidorFinalIdExterno = clienteConsumidorFinalIdExterno,
        });

        await context.SaveChangesAsync();

        var caixa = new Caixa
        {
            FuncionarioId = funcionario.Id,
            IdExterno = 24,
            DataCaixa = new DateOnly(2026, 9, 18),
            Turno = 1,
            DataAbertura = new DateTime(2026, 9, 18, 8, 0, 0),
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        var venda = new Venda { DataHora = new DateTime(2026, 9, 18, 10, 0, 0), CaixaId = caixa.Id, ClienteId = cliente?.Id };
        venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto.Id, Quantidade = 2, PrecoUnitario = 9.90m });
        venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = forma.Id, Valor = 19.80m });
        context.Vendas.Add(venda);
        await context.SaveChangesAsync();

        return venda.Id;
    }

    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task SucessoGravaVendaIdExternoESincronizado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, """{ "data": { "id": 555 } }"""));
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single(v => v.Id == vendaId);
        Assert.Equal(555, venda.VendaIdExterno);
        Assert.Equal(SyncStatus.Sincronizado, venda.SyncStatus);
    }

    [Fact]
    public async Task VendaAvulsaUsaClienteConsumidorFinalConfigurado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture, clienteConsumidorFinalIdExterno: 1);

        string? corpoCapturado = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpoCapturado = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return RespostaJson(HttpStatusCode.OK, """{ "data": { "id": 1 } }""");
        });
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.Contains("\"cliente_id\":1", corpoCapturado);
    }

    [Fact]
    public async Task ConflitoDoServidorEhTratadoComoJaSincronizado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.Conflict, """{ "errors": { "message": ["Venda já registrada."] } }"""));
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.Sincronizado, leitura.Vendas.Single().SyncStatus);
    }

    [Fact]
    public async Task ErroDeValidacaoMarcaFalhaEIncrementaTentativas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.UnprocessableEntity, """{ "errors": { "cliente_id": ["Campo obrigatório."] } }"""));
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.FalhaSync, venda.SyncStatus);
        Assert.Equal(1, venda.TentativasEnvio);
        Assert.Contains("obrigatório", venda.UltimoErroSync);
    }

    [Fact]
    public async Task TokenExpiradoEhTratadoComoFalhaEIncrementaTentativas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.Unauthorized, """{ "errors": { "message": ["Token expirado."] } }"""));
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.FalhaSync, venda.SyncStatus);
        Assert.Equal(1, venda.TentativasEnvio);
        Assert.Contains("token", venda.UltimoErroSync, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmpresaNaoSincronizadaFicaPendenteSemChamarRede()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture, empresaSincronizada: false);

        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return RespostaJson(HttpStatusCode.OK, "{}"); });
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.PendenteSync, leitura.Vendas.Single().SyncStatus);
    }

    [Fact]
    public async Task ProdutoNaoSincronizadoFicaPendenteSemChamarRede()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaPendenteAsync(fixture, produtoSincronizado: false);

        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return RespostaJson(HttpStatusCode.OK, "{}"); });
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarVendaAsync(vendaId, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
    }
}

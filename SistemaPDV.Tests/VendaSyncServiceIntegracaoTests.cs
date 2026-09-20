using System.Net;
using System.Text;
using SistemaPDV.Services.Sync;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Tests;

public class VendaSyncServiceIntegracaoTests
{
    private record BaseTeste(int CaixaId, int ProdutoId, int FormaPagamentoId);

    private static async Task<BaseTeste> SemearBaseCompletaAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();

        var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
        var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m, IdExterno = 10, ProdutoIdApi = 100 };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
        context.AddRange(funcionario, produto, forma);
        context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
        {
            UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1",
            ClienteConsumidorFinalIdExterno = 1,
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

        return new BaseTeste(caixa.Id, produto.Id, forma.Id);
    }

    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task CicloCompletoRegistrarLocalESincronizar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaPagamentoId) = await SemearBaseCompletaAsync(fixture);

        var vendaService = new VendaService(fixture.CriarContexto);
        var venda = await vendaService.RegistrarVendaLocalAsync(
            caixaId, clienteId: null, itens: new[] { (produtoId, 2m, 9.90m, 0m, 0m) }, pagamentos: new[] { (formaPagamentoId, 19.80m) });

        // Confirma que registrar não tocou rede nenhuma: nada foi sincronizado ainda.
        using (var leituraAntes = fixture.CriarContexto())
            Assert.Equal(SyncStatus.PendenteSync, leituraAntes.Vendas.Single().SyncStatus);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, """{ "data": { "id": 777 } }"""));
        var syncService = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await syncService.SincronizarVendaAsync(venda.Id, "token-fake");

        Assert.True(resultado.Sucesso);
        using var leituraDepois = fixture.CriarContexto();
        var vendaSincronizada = leituraDepois.Vendas.Single();
        Assert.Equal(777, vendaSincronizada.VendaIdExterno);
        Assert.Equal(SyncStatus.Sincronizado, vendaSincronizada.SyncStatus);
    }

    [Fact]
    public async Task SincronizarVendasPendentesReenviaPendentesEFalhadasMasNaoAsJaSincronizadas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (caixaId, produtoId, formaPagamentoId) = await SemearBaseCompletaAsync(fixture);
        var vendaService = new VendaService(fixture.CriarContexto);

        var vendaPendente = await vendaService.RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaPagamentoId, 9.90m) });
        var vendaQueVaiFalharDeNovo = await vendaService.RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaPagamentoId, 9.90m) });
        var vendaJaSincronizada = await vendaService.RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaPagamentoId, 9.90m) });

        await using (var context = fixture.CriarContexto())
        {
            var venda = await context.Vendas.SingleAsync(v => v.Id == vendaJaSincronizada.Id);
            venda.SyncStatus = SyncStatus.Sincronizado;
            venda.VendaIdExterno = 1;
            await context.SaveChangesAsync();

            var vendaFalha = await context.Vendas.SingleAsync(v => v.Id == vendaQueVaiFalharDeNovo.Id);
            vendaFalha.SyncStatus = SyncStatus.FalhaSync;
            vendaFalha.UltimoErroSync = "erro anterior";
            await context.SaveChangesAsync();
        }

        var chamadas = 0;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            chamadas++;
            return RespostaJson(HttpStatusCode.OK, """{ "data": { "id": 999 } }""");
        });
        var syncService = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await syncService.SincronizarVendasPendentesAsync("token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(2, chamadas); // só pendente + falhada, não a já sincronizada
        Assert.Equal(2, resultado.Quantidade);

        using var leitura = fixture.CriarContexto();
        Assert.All(leitura.Vendas, v => Assert.Equal(SyncStatus.Sincronizado, v.SyncStatus));
    }
}

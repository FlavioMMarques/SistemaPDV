using System.Net;
using System.Text;
using SistemaPDV.Services.Sync;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Tests;

// Comprova a correção da revisão de código de 2026-09-18: uma venda que lança
// exceção (em vez de devolver ComFalha) não pode abortar o lote inteiro nem deixar
// SincronizarVendasPendentesAsync propagar a exceção pro chamador.
public class VendaSyncServiceCorrecoesTests
{
    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task LoteComVendaQueLancaExcecaoNaoPropagaEProcessaAsDemais()
    {
        using var fixture = new SqliteInMemoryFixture();

        // A FK Restrict de Venda->Caixa (CaixaConfiguration) impede criar uma venda
        // "quebrada" (caixa inexistente) — o banco já protege contra isso. O jeito
        // real de SincronizarVendaAsync lançar InvalidOperationException hoje é a
        // ConfiguracaoSincronizacao sumir (não tem FK protegendo essa linha) — então
        // simulamos removendo-a DEPOIS de criar as vendas, antes de sincronizar.
        int caixaId, produtoId, formaPagamentoId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
            var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m, IdExterno = 10, ProdutoIdApi = 100 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
            context.AddRange(funcionario, produto, forma);
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
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
            caixaId = caixa.Id;
            produtoId = produto.Id;
            formaPagamentoId = forma.Id;
        }

        var vendaService = new VendaService(fixture.CriarContexto);
        var venda1 = await vendaService.RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaPagamentoId, 9.90m) });
        var venda2 = await vendaService.RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 9.90m, 0m, 0m) }, new[] { (formaPagamentoId, 9.90m) });

        // Remove a configuração DEPOIS de criar as vendas — agora qualquer tentativa
        // de sincronizar vai bater no `?? throw new InvalidOperationException(...)`.
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.RemoveRange(context.ConfiguracoesSincronizacao);
            await context.SaveChangesAsync();
        }

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => RespostaJson(HttpStatusCode.OK, """{ "data": { "id": 555 } }"""));
        var service = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        // Antes da correção: a exceção da PRIMEIRA venda subia sem tratamento e
        // SincronizarVendasPendentesAsync inteiro lançava — nem a segunda venda era
        // tentada. Depois da correção: o método completa normalmente.
        var resultado = await service.SincronizarVendasPendentesAsync("token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(0, resultado.Quantidade); // nenhuma sincronizou (config sumiu), mas nada travou

        using var leitura = fixture.CriarContexto();
        // As duas continuam PendenteSync — nenhuma virou Sincronizado, mas também
        // nenhuma exceção vazou pro chamador.
        Assert.All(leitura.Vendas, v => Assert.Equal(SyncStatus.PendenteSync, v.SyncStatus));
    }
}

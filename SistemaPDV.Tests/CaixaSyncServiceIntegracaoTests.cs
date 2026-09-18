using System.Net;
using System.Text;
using SistemaPDV.Services.Sync;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class CaixaSyncServiceIntegracaoTests
{
    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task CicloCompletoAbrirSincronizarFecharSincronizar()
    {
        using var fixture = new SqliteInMemoryFixture();

        int funcionarioId, formaPagamentoId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
            context.AddRange(funcionario, forma);
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
            await context.SaveChangesAsync();
            funcionarioId = funcionario.Id;
            formaPagamentoId = forma.Id;
        }

        var caminhosChamados = new List<string>();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            var caminho = req.RequestUri!.AbsolutePath;
            caminhosChamados.Add(caminho);

            if (caminho.EndsWith("/abrir"))
                return RespostaJson(HttpStatusCode.Created, """{ "data": { "msg": "ok", "success": { "id": 24 } } }""");

            if (caminho.EndsWith("/fechar"))
                return RespostaJson(HttpStatusCode.OK, """{ "data": { "msg": "ok", "success": { "id": 24 } } }""");

            return RespostaJson(HttpStatusCode.NotFound, "{}");
        });

        var caixaService = new CaixaService(fixture.CriarContexto);
        var syncService = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        // 1) Abre localmente — sem rede.
        var abertura = await caixaService.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);
        Assert.True(abertura.Sucesso);
        Assert.Empty(caminhosChamados);

        // 2) Sincroniza — deve chamar /abrir e preencher o IdExterno.
        var resultado1 = await syncService.SincronizarCaixaPendenteAsync("token-fake");
        Assert.True(resultado1.Sucesso);
        Assert.Contains(caminhosChamados, c => c.EndsWith("/abrir"));

        using (var leitura = fixture.CriarContexto())
        {
            var caixa = leitura.Caixas.Single();
            Assert.Equal(24, caixa.IdExterno);
            Assert.Equal(SyncStatus.Sincronizado, caixa.SyncStatus);
        }

        // 3) Nada mais pendente — sincronizar de novo não chama a API.
        caminhosChamados.Clear();
        var resultadoNeutro = await syncService.SincronizarCaixaPendenteAsync("token-fake");
        Assert.True(resultadoNeutro.Sucesso);
        Assert.Empty(caminhosChamados);

        // 4) Fecha localmente — sem rede.
        var caixaId = fixture.CriarContexto().Caixas.Single().Id;
        var fechamento = await caixaService.FecharCaixaLocalAsync(
            caixaId, trocoFinal: 10m, digitacoes: new[] { (formaPagamentoId, 100m) }, digitacoesBandeiras: Array.Empty<(string, decimal)>());
        Assert.True(fechamento.Sucesso);

        // 5) Sincroniza de novo — dessa vez deve chamar /fechar, não /abrir.
        caminhosChamados.Clear();
        var resultado2 = await syncService.SincronizarCaixaPendenteAsync("token-fake");
        Assert.True(resultado2.Sucesso);
        Assert.Contains(caminhosChamados, c => c.EndsWith("/fechar"));
        Assert.DoesNotContain(caminhosChamados, c => c.EndsWith("/abrir"));

        using (var leituraFinal = fixture.CriarContexto())
        {
            Assert.Equal(SyncStatus.Sincronizado, leituraFinal.Caixas.Single().SyncStatus);
        }
    }
}

using System.Net;
using System.Text;
using SistemaPDV.Services.Sync;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

// Testes escritos especificamente pra comprovar os bugs encontrados na revisão de
// código de 2026-09-18 (ver docs/APRENDIZADOS.md) — sem esses testes, uma futura
// mudança poderia reintroduzir qualquer um deles sem que nada acuse.
public class CaixaSyncServiceCorrecoesTests
{
    private static async Task<int> SemearFuncionarioEConfiguracaoAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
        context.Funcionarios.Add(funcionario);
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
        await context.SaveChangesAsync();
        return funcionario.Id;
    }

    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task CaixaAbertoEFechadoInteiramenteOfflineNaoPerdeOFechamentoAoSincronizar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioEConfiguracaoAsync(fixture);

        // Todo o ciclo acontece OFFLINE primeiro — abre e fecha sem nenhuma
        // sincronização no meio, exatamente o cenário que causava perda de dados.
        var caixaService = new CaixaService(fixture.CriarContexto);
        var abertura = await caixaService.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);
        Assert.True(abertura.Sucesso);
        var fechamento = await caixaService.FecharCaixaLocalAsync(abertura.Valor!.Id, 10m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());
        Assert.True(fechamento.Sucesso);

        var caminhosChamados = new List<string>();
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            var caminho = req.RequestUri!.AbsolutePath;
            caminhosChamados.Add(caminho);

            if (caminho.EndsWith("/abrir"))
                return RespostaJson(HttpStatusCode.Created, """{ "data": { "msg": "ok", "success": { "id": 24 } } }""");

            return RespostaJson(HttpStatusCode.OK, """{ "data": { "msg": "ok", "success": { "id": 24 } } }""");
        });
        var syncService = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        // Primeira chamada de sync: deve sincronizar a ABERTURA (nunca confirmada
        // ainda), não o fechamento — mesmo o caixa já estando fechado localmente.
        var resultado1 = await syncService.SincronizarCaixaPendenteAsync("token-fake");
        Assert.True(resultado1.Sucesso);
        Assert.Contains(caminhosChamados, c => c.EndsWith("/abrir"));
        Assert.DoesNotContain(caminhosChamados, c => c.EndsWith("/fechar"));

        // Segunda chamada: agora sim deve pegar o fechamento pendente — ANTES da
        // correção, isso nunca acontecia (o caixa ficava "Sincronizado" já na
        // primeira chamada, e o fechamento se perdia pra sempre).
        caminhosChamados.Clear();
        var resultado2 = await syncService.SincronizarCaixaPendenteAsync("token-fake");
        Assert.True(resultado2.Sucesso);
        Assert.Contains(caminhosChamados, c => c.EndsWith("/fechar"));

        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.True(caixa.AberturaSincronizada);
        Assert.Equal(SyncStatus.Sincronizado, caixa.SyncStatus);
        Assert.Equal(StatusCaixa.Fechado, caixa.Status);
    }

    [Fact]
    public async Task ConflitoNaAberturaPermiteFechamentoSincronizarDepois()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioEConfiguracaoAsync(fixture);

        var caixaService = new CaixaService(fixture.CriarContexto);
        var abertura = await caixaService.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);
        await caixaService.FecharCaixaLocalAsync(abertura.Valor!.Id, 10m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/abrir")
                ? RespostaJson(HttpStatusCode.Conflict, """{ "errors": { "message": ["Já existe caixa aberto."] } }""")
                : RespostaJson(HttpStatusCode.OK, """{ "data": { "msg": "ok", "success": { "id": 24 } } }"""));
        var syncService = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        // 409 na abertura — antes da correção, isso deixava AberturaSincronizada de
        // fora (o campo nem existia), e o fechamento nunca mais conseguia sincronizar.
        var resultadoAbertura = await syncService.SincronizarAberturaAsync("token-fake");
        Assert.True(resultadoAbertura.Sucesso);

        using (var leitura = fixture.CriarContexto())
        {
            var caixa = leitura.Caixas.Single();
            Assert.True(caixa.AberturaSincronizada);
            Assert.Null(caixa.IdExterno); // 409 não devolve id — e não devia bloquear o fechamento por isso
        }

        var resultadoFechamento = await syncService.SincronizarFechamentoAsync("token-fake");
        Assert.True(resultadoFechamento.Sucesso);
    }

    [Fact]
    public async Task AberturaComFalhaAnteriorEhRetentada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioEConfiguracaoAsync(fixture);

        await using (var context = fixture.CriarContexto())
        {
            var funcionario = await context.Funcionarios.SingleAsync(f => f.Id == funcionarioId);
            context.Caixas.Add(new Models.Caixa
            {
                FuncionarioId = funcionario.Id,
                DataCaixa = new DateOnly(2026, 9, 18),
                Turno = 1,
                DataAbertura = new DateTime(2026, 9, 18, 8, 0, 0),
                TrocoInicial = 10m,
                Status = StatusCaixa.Aberto,
                SyncStatus = SyncStatus.FalhaSync, // já tentou antes e falhou
                UltimoErroSync = "erro de rede anterior",
            });
            await context.SaveChangesAsync();
        }

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.Created, """{ "data": { "msg": "ok", "success": { "id": 30 } } }"""));
        var syncService = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await syncService.SincronizarAberturaAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.Sincronizado, leitura.Caixas.Single().SyncStatus);
    }

    [Fact]
    public async Task DataCaixaEnviadaUsaDataDoNegocioNaoOTimestampDeAbertura()
    {
        using var fixture = new SqliteInMemoryFixture();
        var funcionarioId = await SemearFuncionarioEConfiguracaoAsync(fixture);

        await using (var context = fixture.CriarContexto())
        {
            context.Caixas.Add(new Models.Caixa
            {
                FuncionarioId = funcionarioId,
                DataCaixa = new DateOnly(2026, 9, 18),
                Turno = 1,
                // DataAbertura tem um horário bem diferente de meia-noite, de propósito
                // — se o bug reaparecer, o teste pega o timestamp errado no corpo.
                DataAbertura = new DateTime(2026, 9, 18, 23, 59, 59),
                TrocoInicial = 10m,
            });
            await context.SaveChangesAsync();
        }

        string? corpoCapturado = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpoCapturado = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return RespostaJson(HttpStatusCode.Created, """{ "data": { "msg": "ok", "success": { "id": 1 } } }""");
        });
        var syncService = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        await syncService.SincronizarAberturaAsync("token-fake");

        Assert.Contains("\"data_caixa\":\"2026-09-18 00:00:00\"", corpoCapturado);
    }
}

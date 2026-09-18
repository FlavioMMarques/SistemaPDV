using System.Net;
using System.Text;
using SistemaPDV.Services.Sync;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class CaixaSyncServiceAberturaTests
{
    private static async Task<(int FuncionarioId, int CaixaId)> SemearCaixaPendenteAsync(SqliteInMemoryFixture fixture, int? funcionarioIdExterno = 2)
    {
        await using var context = fixture.CriarContexto();

        var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = funcionarioIdExterno };
        context.Funcionarios.Add(funcionario);
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
        await context.SaveChangesAsync();

        var caixa = new Models.Caixa
        {
            FuncionarioId = funcionario.Id,
            DataCaixa = new DateOnly(2026, 9, 18),
            Turno = 1,
            DataAbertura = new DateTime(2026, 9, 18, 8, 0, 0),
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        return (funcionario.Id, caixa.Id);
    }

    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task SucessoGravaIdExternoEMarcaAberturaSincronizada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.Created, """{ "data": { "msg": "Caixa aberto com sucesso!", "success": { "id": 24 } } }"""));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.Equal(24, caixa.IdExterno);
        Assert.True(caixa.AberturaSincronizada);
        Assert.Null(caixa.UltimoErroSync);
        // Caixa ainda está Aberto (não foi fechado) — nesse caso SyncStatus reflete
        // o estado real: nada pendente. Ver CaixaSyncServiceCorrecoesTests pro caso
        // em que o caixa já foi fechado offline antes da abertura confirmar (lá
        // SyncStatus PRECISA continuar PendenteSync, pra não perder o fechamento).
        Assert.Equal(SyncStatus.Sincronizado, caixa.SyncStatus);
    }

    [Fact]
    public async Task ConflitoDoServidorEhTratadoComoJaSincronizado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.Conflict, """{ "errors": { "message": ["Já existe caixa aberto para esta data/operador/turno."] } }"""));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.True(caixa.AberturaSincronizada);
        Assert.Null(caixa.IdExterno); // 409 não devolve o id, só confirma que já existe
    }

    [Fact]
    public async Task ErroDeValidacaoMarcaFalhaComMensagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.UnprocessableEntity, """{ "errors": { "turno": ["O turno é obrigatório."] } }"""));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.Equal(SyncStatus.FalhaSync, caixa.SyncStatus);
        Assert.Contains("turno", caixa.UltimoErroSync, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenExpiradoEhTratadoComoFalhaEMarcaFalhaSync()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.Unauthorized, """{ "errors": { "message": ["Token expirado."] } }"""));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.Equal(SyncStatus.FalhaSync, caixa.SyncStatus);
        Assert.False(caixa.AberturaSincronizada);
        Assert.Contains("token", caixa.UltimoErroSync, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SemCaixaPendenteNaoFazNadaNemChamaRede()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
            await context.SaveChangesAsync();
        }

        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamou = true;
            return RespostaJson(HttpStatusCode.OK, "{}");
        });
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.True(resultado.Sucesso);
        Assert.False(chamou);
    }

    [Fact]
    public async Task FuncionarioAindaNaoSincronizadoNaoTentaEDeixaPendente()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaPendenteAsync(fixture, funcionarioIdExterno: null);

        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamou = true;
            return RespostaJson(HttpStatusCode.OK, "{}");
        });
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarAberturaAsync("token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.PendenteSync, leitura.Caixas.Single().SyncStatus);
    }
}

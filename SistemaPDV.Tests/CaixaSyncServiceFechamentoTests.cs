using System.Net;
using System.Text;
using SistemaPDV.Services.Sync;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class CaixaSyncServiceFechamentoTests
{
    private static async Task<int> SemearCaixaFechadoPendenteAsync(
        SqliteInMemoryFixture fixture, int? caixaIdExterno = 24, bool aberturaSincronizada = true, SyncStatus syncStatus = SyncStatus.PendenteSync)
    {
        await using var context = fixture.CriarContexto();

        var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
        context.AddRange(funcionario, forma);
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
        await context.SaveChangesAsync();

        var caixa = new Models.Caixa
        {
            FuncionarioId = funcionario.Id,
            IdExterno = caixaIdExterno,
            AberturaSincronizada = aberturaSincronizada,
            DataCaixa = new DateOnly(2026, 9, 18),
            Turno = 1,
            DataAbertura = new DateTime(2026, 9, 18, 8, 0, 0),
            DataFechamento = new DateTime(2026, 9, 18, 18, 0, 0),
            TrocoInicial = 10m,
            TrocoFinal = 10m,
            Status = StatusCaixa.Fechado,
            SyncStatus = syncStatus,
        };
        caixa.Digitacoes.Add(new DigitacaoCaixa { FormaPagamentoId = forma.Id, Valor = 235.60m });
        caixa.DigitacoesBandeiras.Add(new DigitacaoBandeiraCaixa { Bandeira = "VISA", Valor = 235.60m });
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        return caixa.Id;
    }

    private static HttpResponseMessage RespostaJson(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task SucessoMarcaSincronizado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaFechadoPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "data": { "msg": "Caixa fechado com sucesso!", "success": { "id": 24 } } }"""));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarFechamentoAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single(c => c.Id == caixaId);
        Assert.Equal(SyncStatus.Sincronizado, caixa.SyncStatus);
    }

    [Fact]
    public async Task ErroDeValidacaoMarcaFalhaComMensagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaFechadoPendenteAsync(fixture);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.UnprocessableEntity, """{ "errors": { "message": ["Este caixa já está fechado."] } }"""));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarFechamentoAsync("token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.Equal(SyncStatus.FalhaSync, caixa.SyncStatus);
        Assert.Contains("já está fechado", caixa.UltimoErroSync);
    }

    [Fact]
    public async Task RetentaFecharUmCaixaQueJaFalhouAntes()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaFechadoPendenteAsync(fixture, syncStatus: SyncStatus.FalhaSync);

        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            RespostaJson(HttpStatusCode.OK, """{ "data": { "msg": "ok", "success": { "id": 24 } } }"""));
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarFechamentoAsync("token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.Sincronizado, leitura.Caixas.Single(c => c.Id == caixaId).SyncStatus);
    }

    [Fact]
    public async Task CaixaCujaAberturaAindaNaoSincronizouNaoEhTentadoNemChamaRede()
    {
        using var fixture = new SqliteInMemoryFixture();
        // AberturaSincronizada = false — mesmo com IdExterno preenchido (não deveria
        // acontecer na prática, mas o teste isola exatamente essa condição), o
        // fechamento não pode ser tentado antes da abertura confirmar.
        await SemearCaixaFechadoPendenteAsync(fixture, aberturaSincronizada: false);

        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamou = true;
            return RespostaJson(HttpStatusCode.OK, "{}");
        });
        var service = new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient));

        var resultado = await service.SincronizarFechamentoAsync("token-fake");

        // "Nada pendente pra fechar ainda" — não é erro, é a mesma semântica de
        // SincronizarAberturaAsync quando não há nada elegível. O orquestrador
        // (SincronizarCaixaPendenteAsync) que garante a abertura ser tentada primeiro.
        Assert.True(resultado.Sucesso);
        Assert.Equal(0, resultado.Quantidade);
        Assert.False(chamou);
    }

    [Fact]
    public async Task SemCaixaFechadoPendenteNaoFazNadaNemChamaRede()
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

        var resultado = await service.SincronizarFechamentoAsync("token-fake");

        Assert.True(resultado.Sucesso);
        Assert.False(chamou);
    }
}

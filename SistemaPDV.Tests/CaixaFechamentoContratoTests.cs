using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// O fechamento enviava forma_pagamento_id = id LOCAL da forma (ex: 1) em vez do id da API (ex: 5): a API
// registraria a apuração na forma de pagamento errada.
[SupportedOSPlatform("windows")]
public class CaixaFechamentoContratoTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task<(int CaixaId, int FormaLocalId)> SemearCaixaFechadoAsync(SqliteInMemoryFixture fixture, int? formaIdExterno)
    {
        int funcionarioId, formaId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            // Formas "de enchimento" pra o id local da forma usada NÃO coincidir com o id da API.
            context.FormasPagamento.Add(new FormaPagamento { Nome = "Outra", Tipo = "X", IdExterno = 90 });
            var forma = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", IdExterno = formaIdExterno };
            context.AddRange(funcionario, forma);
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
            await context.SaveChangesAsync();
            (funcionarioId, formaId) = (funcionario.Id, forma.Id);
        }

        var service = new CaixaService(fixture.CriarContexto);
        var aberto = await service.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 20), 1, 10m);
        await service.FecharCaixaLocalAsync(aberto.Valor!.Id, 12.50m, new[] { (formaId, 150m) }, Array.Empty<(string, decimal)>());
        await using (var context = fixture.CriarContexto())
        {
            var caixa = context.Caixas.Single();
            caixa.AberturaSincronizada = true;   // já confirmada: só o fechamento está pendente
            await context.SaveChangesAsync();
        }
        return (aberto.Valor!.Id, formaId);
    }

    [Fact]
    public async Task FechamentoEnviaOIdDaApiDaFormaDePagamentoENaoOIdLocal()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, formaLocalId) = await SemearCaixaFechadoAsync(fixture, formaIdExterno: 5);
        Assert.NotEqual(5, formaLocalId);   // o teste só vale se os ids diferem
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            corpo = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, "{}");
        });

        var resultado = await new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarFechamentoAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var doc = JsonDocument.Parse(corpo!);
        var digitacao = doc.RootElement.GetProperty("digitacao")[0];
        Assert.Equal(5, digitacao.GetProperty("forma_pagamento_id").GetInt32());
        Assert.Equal(150m, digitacao.GetProperty("valor").GetDecimal());
    }

    [Fact]
    public async Task FormaSemIdDaApiFazOFechamentoEsperarSemEnviarNemMarcarFalha()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaFechadoAsync(fixture, formaIdExterno: null);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Json(HttpStatusCode.OK, "{}"); });

        var resultado = await new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarFechamentoAsync("t");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        var caixa = leitura.Caixas.Single();
        Assert.Equal(SyncStatus.PendenteSync, caixa.SyncStatus);   // segue na fila
        Assert.Equal(0, caixa.TentativasEnvio);                    // sem gastar tentativa
        Assert.Contains("formas de pagamento", caixa.UltimoErroSync, StringComparison.OrdinalIgnoreCase);   // e diz por quê
    }
}

using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Venda avulsa (sem cliente) usa o cliente "Consumidor Final". Ficava 🟡 pra sempre e SEM explicação quando o id
// não estava configurado à mão: a espera por dependência não marca falha (de propósito), mas também não dizia nada.
// Na API real o Consumidor Final é o cliente com indicador_finalidade = 1 (id 1, "CONSUMIDOR").
[SupportedOSPlatform("windows")]
public class VendaConsumidorFinalTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task<Guid> SemearVendaAvulsaAsync(SqliteInMemoryFixture fixture, int? consumidorFinalConfigurado, bool comClienteConsumidorNaBase)
    {
        int caixaId, produtoId, formaId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 50m, IdExterno = 308, ProdutoIdApi = 127 };
            var forma = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
            context.AddRange(funcionario, produto, forma);
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi, ClienteConsumidorFinalIdExterno = consumidorFinalConfigurado });
            if (comClienteConsumidorNaBase)
            {
                context.Clientes.Add(new Cliente { Nome = "CONSUMIDOR", IdExterno = 1, IndicadorFinalidade = 1, SyncStatus = SyncStatus.Sincronizado });
                context.Clientes.Add(new Cliente { Nome = "Maria", IdExterno = 9, IndicadorFinalidade = 0, SyncStatus = SyncStatus.Sincronizado });
            }
            await context.SaveChangesAsync();

            var caixa = new Models.Caixa
            {
                FuncionarioId = funcionario.Id, IdExterno = 28, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1,
                DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0), TrocoInicial = 10m, AberturaSincronizada = true,
            };
            context.Caixas.Add(caixa);
            await context.SaveChangesAsync();
            (caixaId, produtoId, formaId) = (caixa.Id, produto.Id, forma.Id);
        }

        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            caixaId, null, new[] { (produtoId, 1m, 50m, 0m, 0m) }, new[] { (formaId, 50m) });
        return venda.Id;
    }

    private static (VendaSyncService Service, Func<string?> Corpo, Func<bool> Chamou) CriarService(SqliteInMemoryFixture fixture)
    {
        string? corpo = null;
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(r =>
        {
            chamou = true;
            corpo = r.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "id": 400 } }""");
        });
        return (new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)), () => corpo, () => chamou);
    }

    [Fact]
    public async Task VendaAvulsaSemIdConfiguradoUsaOClienteConsumidorFinalDaApi()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaAvulsaAsync(fixture, consumidorFinalConfigurado: null, comClienteConsumidorNaBase: true);
        var (service, corpo, _) = CriarService(fixture);

        var resultado = await service.SincronizarVendaAsync(vendaId, "t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var doc = JsonDocument.Parse(corpo()!);
        Assert.Equal(1, doc.RootElement.GetProperty("cliente_id").GetInt32());   // o de indicador_finalidade = 1, não o "Maria" (9)
    }

    [Fact]
    public async Task IdConfiguradoManualmenteTemPrioridadeSobreADeteccao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaAvulsaAsync(fixture, consumidorFinalConfigurado: 9, comClienteConsumidorNaBase: true);
        var (service, corpo, _) = CriarService(fixture);

        await service.SincronizarVendaAsync(vendaId, "t");

        using var doc = JsonDocument.Parse(corpo()!);
        Assert.Equal(9, doc.RootElement.GetProperty("cliente_id").GetInt32());
    }

    [Fact]
    public async Task SemConfigurarENemAchaOConsumidorFinalFicaPendenteMasDizPorQue()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaAvulsaAsync(fixture, consumidorFinalConfigurado: null, comClienteConsumidorNaBase: false);
        var (service, _, chamou) = CriarService(fixture);

        var resultado = await service.SincronizarVendaAsync(vendaId, "t");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou());
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.PendenteSync, venda.SyncStatus);   // continua na fila: não é culpa da venda
        Assert.Equal(0, venda.TentativasEnvio);                    // e não gasta tentativa
        Assert.Contains("Consumidor Final", venda.UltimoErroSync);  // mas o operador vê o motivo
    }

    [Fact]
    public async Task MotivoDaEsperaSomeQuandoAVendaEEnviada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var vendaId = await SemearVendaAvulsaAsync(fixture, consumidorFinalConfigurado: null, comClienteConsumidorNaBase: false);
        var (service, _, _) = CriarService(fixture);
        await service.SincronizarVendaAsync(vendaId, "t");   // espera: Consumidor Final não encontrado

        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.Add(new Cliente { Nome = "CONSUMIDOR", IdExterno = 1, IndicadorFinalidade = 1, SyncStatus = SyncStatus.Sincronizado });
            await context.SaveChangesAsync();
        }
        var resultado = await service.SincronizarVendaAsync(vendaId, "t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var venda = leitura.Vendas.Single();
        Assert.Equal(SyncStatus.Sincronizado, venda.SyncStatus);
        Assert.Null(venda.UltimoErroSync);
    }
}

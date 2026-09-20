using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Regra de negócio (usuário, 2026-09-20): o fechamento SÓ pode acontecer se o caixa não tem venda pendente.
// O fechamento resume o caixa na API; se uma venda ainda não chegou (pendente, em falha ou em espera crescente),
// a API fecharia o caixa sem ela. Três camadas: o serviço recusa, a tela avisa e bloqueia o botão, e o envio do
// fechamento espera (cobre caixa que já estava fechado antes da regra).
[SupportedOSPlatform("windows")]
public class FechamentoComVendasPendentesTests
{
    private static async Task<int> SemearCaixaComVendaAsync(SqliteInMemoryFixture fixture, SyncStatus statusDaVenda)
    {
        int funcionarioId, formaId, produtoId;
        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 10m, IdExterno = 10, ProdutoIdApi = 100 };
            context.AddRange(funcionario, forma, produto);
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
            await context.SaveChangesAsync();
            (funcionarioId, formaId, produtoId) = (funcionario.Id, forma.Id, produto.Id);
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 20), 1, 10m)).Valor!;
        await using (var context = fixture.CriarContexto())
        {
            (await context.Caixas.SingleAsync()).AberturaSincronizada = true;
            await context.SaveChangesAsync();
        }
        await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(caixa.Id, null, new[] { (produtoId, 1m, 10m, 0m, 0m) }, new[] { (formaId, 10m) });
        await DefinirStatusDasVendasAsync(fixture, statusDaVenda);
        return caixa.Id;
    }

    private static async Task DefinirStatusDasVendasAsync(SqliteInMemoryFixture fixture, SyncStatus status)
    {
        await using var context = fixture.CriarContexto();
        foreach (var venda in context.Vendas)
            venda.SyncStatus = status;
        await context.SaveChangesAsync();
    }

    // ---- camada 1: o serviço recusa ----

    [Theory]
    [InlineData(SyncStatus.PendenteSync)]
    [InlineData(SyncStatus.FalhaSync)]
    public async Task ServicoRecusaFecharCaixaComVendaNaoEnviada(SyncStatus statusDaVenda)
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture, statusDaVenda);

        var resultado = await new CaixaService(fixture.CriarContexto)
            .FecharCaixaLocalAsync(caixaId, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        Assert.False(resultado.Sucesso);
        Assert.Contains("venda", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(StatusCaixa.Aberto, leitura.Caixas.Single().Status);   // nada mudou
    }

    [Fact]
    public async Task ServicoFechaQuandoTodasAsVendasJaForamEnviadas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture, SyncStatus.Sincronizado);

        var resultado = await new CaixaService(fixture.CriarContexto)
            .FecharCaixaLocalAsync(caixaId, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        Assert.True(resultado.Sucesso, resultado.Mensagem);
    }

    [Fact]
    public async Task VendaDeOutroCaixaNaoImpedeOFechamento()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture, SyncStatus.PendenteSync);
        int funcionarioId;
        await using (var context = fixture.CriarContexto())
            funcionarioId = context.Funcionarios.Single().Id;
        var outroCaixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;

        var resultado = await new CaixaService(fixture.CriarContexto)
            .FecharCaixaLocalAsync(outroCaixa.Id, 0m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        Assert.True(resultado.Sucesso, resultado.Mensagem);   // a venda pendente é do OUTRO caixa
        Assert.NotEqual(caixaId, outroCaixa.Id);
    }

    // ---- camada 2: a tela avisa e bloqueia ----

    [Fact]
    public async Task TelaAvisaEBloqueiaOBotaoEDepoisLiberaQuandoAsVendasSaem()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture, SyncStatus.PendenteSync);
        var viewModel = new FecharCaixaViewModel(new CaixaService(fixture.CriarContexto), new VendaLocalService(fixture.CriarContexto), caixaId);
        var pode = true;
        using var inscricao = viewModel.ConfirmarCommand.CanExecute.Subscribe(v => pode = v);

        await viewModel.IniciarAsync();

        Assert.Equal(1, viewModel.VendasPendentes);
        Assert.False(pode);
        Assert.Contains("1 venda", viewModel.AvisoVendasPendentes);
        Assert.Contains("Reenviar falhas", viewModel.AvisoVendasPendentes);   // diz o que fazer

        await DefinirStatusDasVendasAsync(fixture, SyncStatus.Sincronizado);   // a sincronização em background enviou
        await ((IAtualizavelPorSincronizacao)viewModel).AtualizarAposSincronizacaoAsync();

        Assert.Equal(0, viewModel.VendasPendentes);
        Assert.True(pode);
        Assert.Null(viewModel.AvisoVendasPendentes);
    }

    [Fact]
    public async Task MesmoSeOBotaoForAcionadoOServicoRecusaEATelaMostraOMotivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaComVendaAsync(fixture, SyncStatus.FalhaSync);
        var viewModel = new FecharCaixaViewModel(new CaixaService(fixture.CriarContexto), new VendaLocalService(fixture.CriarContexto), caixaId);
        await viewModel.IniciarAsync();

        var fechado = await viewModel.ConfirmarCommand.Execute();   // Execute() ignora o CanExecute: prova a camada 1

        Assert.Null(fechado);
        Assert.Contains("venda", viewModel.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    // ---- camada 3: o envio do fechamento espera ----

    [Fact]
    public async Task EnvioDoFechamentoEsperaSeAindaHaVendaNaoEnviadaEDizPorQue()
    {
        // Caixa fechado ANTES da regra existir (fechado direto no banco): o outbox não pode mandar o fechamento
        // enquanto uma venda dele não chegou à API.
        using var fixture = new SqliteInMemoryFixture();
        await SemearCaixaComVendaAsync(fixture, SyncStatus.FalhaSync);
        await using (var context = fixture.CriarContexto())
        {
            var caixa = await context.Caixas.SingleAsync();
            caixa.Status = StatusCaixa.Fechado;
            caixa.DataFechamento = DateTime.Now;
            caixa.TrocoFinal = 0m;
            caixa.SyncStatus = SyncStatus.PendenteSync;
            await context.SaveChangesAsync();
        }
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
        {
            chamou = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        });

        var resultado = await new CaixaSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient)).SincronizarFechamentoAsync("t");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        var salvo = leitura.Caixas.Single();
        Assert.Equal(SyncStatus.PendenteSync, salvo.SyncStatus);   // segue na fila, sem gastar tentativa
        Assert.Equal(0, salvo.TentativasEnvio);
        Assert.Contains("venda", salvo.UltimoErroSync, StringComparison.OrdinalIgnoreCase);
    }
}

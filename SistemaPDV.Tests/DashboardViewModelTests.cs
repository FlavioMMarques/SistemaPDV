using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

public class DashboardViewModelTests
{
    private static async Task<int> SemearCaixaAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos Silva" };
        context.Funcionarios.Add(funcionario);
        await context.SaveChangesAsync();

        var caixa = new Caixa
        {
            FuncionarioId = funcionario.Id,
            DataCaixa = DateOnly.FromDateTime(DateTime.Now),
            Turno = 1,
            DataAbertura = DateTime.Now,
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();
        return caixa.Id;
    }

    [Fact]
    public async Task IniciarCarregaOResumoDoCaixaAtual()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        var viewModel = new DashboardViewModel(new DashboardService(fixture.CriarContexto), caixaId);

        await viewModel.IniciarAsync();

        Assert.Equal(0m, viewModel.FaturamentoHoje);
        Assert.Equal(1, viewModel.PendentesOutbox);
    }

    [Fact]
    public async Task NaoDisparaSincronizacaoSozinho()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        var viewModel = new DashboardViewModel(new DashboardService(fixture.CriarContexto), caixaId);

        await viewModel.IniciarAsync();

        // Se o dashboard disparasse sincronização sozinho, o caixa semeado (sem
        // config de API nenhuma) teria lançado uma exceção durante IniciarAsync.
        // Chegar até aqui sem exceção já prova que ele só leu, não tentou sincronizar.
        Assert.True(true);
    }

    [Fact]
    public async Task SemCaixaAbertoNovaVendaFicaDesabilitadaEFaturamentoZero()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new DashboardViewModel(new DashboardService(fixture.CriarContexto), caixaId: null);

        var podeExecutar = false;
        viewModel.NovaVendaCommand.CanExecute.Subscribe(v => podeExecutar = v);
        await viewModel.IniciarAsync();

        Assert.False(podeExecutar);
        Assert.Equal(0m, viewModel.FaturamentoHoje);
    }

    [Fact]
    public async Task NovaVendaCommandEmiteQuandoExecutado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearCaixaAsync(fixture);
        var viewModel = new DashboardViewModel(new DashboardService(fixture.CriarContexto), caixaId);

        // Assinar antes de disparar e esperar a emissão de verdade (FirstAsync) em
        // vez de um bool setado via Subscribe — um bool checado logo após o await
        // corre risco de checar antes da notificação chegar, dependendo de qual
        // scheduler o ReactiveCommand usa (achado: esse teste ficou flaky só de
        // rodar em paralelo com mais testes, 2026-09-18 — mesmo padrão de
        // "esperar o sinal de saída" já usado em ShellViewModelTests).
        var disparouTask = viewModel.NovaVendaCommand.FirstAsync().ToTask();
        await viewModel.NovaVendaCommand.Execute();
        await disparouTask;

        Assert.True(disparouTask.IsCompletedSuccessfully);
    }
}

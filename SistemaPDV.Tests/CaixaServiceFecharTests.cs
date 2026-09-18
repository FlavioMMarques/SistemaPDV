using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Tests;

public class CaixaServiceFecharTests
{
    private static async Task<(int FuncionarioId, int FormaPagamentoId, int CaixaId)> AbrirCaixaDeTesteAsync(SqliteInMemoryFixture fixture)
    {
        var funcionarioId = 0;
        var formaPagamentoId = 0;

        await using (var context = fixture.CriarContexto())
        {
            var funcionario = new Funcionario { Nome = "Carlos Silva" };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
            context.AddRange(funcionario, forma);
            await context.SaveChangesAsync();
            funcionarioId = funcionario.Id;
            formaPagamentoId = forma.Id;
        }

        var caixaService = new CaixaService(fixture.CriarContexto);
        var abertura = await caixaService.AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 18), 1, 10m);

        return (funcionarioId, formaPagamentoId, abertura.Valor!.Id);
    }

    [Fact]
    public async Task FecharCaixaLocalGravaDigitacaoEMudaStatus()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, formaPagamentoId, caixaId) = await AbrirCaixaDeTesteAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var resultado = await service.FecharCaixaLocalAsync(
            caixaId,
            trocoFinal: 10m,
            digitacoes: new[] { (formaPagamentoId, 235.60m) },
            digitacoesBandeiras: new[] { ("VISA", 235.60m) });

        Assert.True(resultado.Sucesso);

        using var leitura = fixture.CriarContexto();
        var caixa = await leitura.Caixas
            .Include(c => c.Digitacoes)
            .Include(c => c.DigitacoesBandeiras)
            .SingleAsync(c => c.Id == caixaId);

        Assert.Equal(StatusCaixa.Fechado, caixa.Status);
        Assert.Equal(SyncStatus.PendenteSync, caixa.SyncStatus);
        Assert.NotNull(caixa.DataFechamento);
        Assert.Equal(10m, caixa.TrocoFinal);
        Assert.Single(caixa.Digitacoes);
        Assert.Single(caixa.DigitacoesBandeiras);
    }

    [Fact]
    public async Task FecharCaixaJaFechadoFalha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, formaPagamentoId, caixaId) = await AbrirCaixaDeTesteAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        await service.FecharCaixaLocalAsync(caixaId, 10m, new[] { (formaPagamentoId, 10m) }, Array.Empty<(string, decimal)>());
        var segundaTentativa = await service.FecharCaixaLocalAsync(caixaId, 10m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        Assert.False(segundaTentativa.Sucesso);
        Assert.Contains("já está fechado", segundaTentativa.Mensagem);
    }

    [Fact]
    public async Task FecharCaixaInexistenteFalha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CaixaService(fixture.CriarContexto);

        var resultado = await service.FecharCaixaLocalAsync(999, 10m, Array.Empty<(int, decimal)>(), Array.Empty<(string, decimal)>());

        Assert.False(resultado.Sucesso);
    }

    [Fact]
    public async Task FecharComFormaPagamentoInexistenteDevolveFalhaSemEstourarExcecao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, _, caixaId) = await AbrirCaixaDeTesteAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var resultado = await service.FecharCaixaLocalAsync(
            caixaId, 10m, digitacoes: new[] { (999999, 10m) }, digitacoesBandeiras: Array.Empty<(string, decimal)>());

        Assert.False(resultado.Sucesso);
        Assert.Contains("999999", resultado.Mensagem);

        // Nenhuma mudança deve ter sido salva — o caixa continua aberto.
        using var leitura = fixture.CriarContexto();
        Assert.Equal(StatusCaixa.Aberto, leitura.Caixas.Single(c => c.Id == caixaId).Status);
    }

    [Fact]
    public async Task FecharComBandeiraMuitoLongaDevolveFalhaSemEstourarExcecao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, formaPagamentoId, caixaId) = await AbrirCaixaDeTesteAsync(fixture);
        var service = new CaixaService(fixture.CriarContexto);

        var bandeiraMuitoLonga = new string('X', 31);

        var resultado = await service.FecharCaixaLocalAsync(
            caixaId, 10m, digitacoes: Array.Empty<(int, decimal)>(), digitacoesBandeiras: new[] { (bandeiraMuitoLonga, 10m) });

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(StatusCaixa.Aberto, leitura.Caixas.Single(c => c.Id == caixaId).Status);
    }
}

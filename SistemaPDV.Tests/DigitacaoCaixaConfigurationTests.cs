using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Tests;

public class DigitacaoCaixaConfigurationTests
{
    private static async Task<(int FuncionarioId, int FormaPagamentoId, int CaixaId)> SemearBaseAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();

        var funcionario = new Funcionario { Nome = "Carlos Silva" };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
        context.AddRange(funcionario, forma);
        await context.SaveChangesAsync();

        var caixa = new Caixa
        {
            FuncionarioId = funcionario.Id,
            DataCaixa = new DateOnly(2026, 9, 18),
            Turno = 1,
            DataAbertura = DateTime.Now,
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        return (funcionario.Id, forma.Id, caixa.Id);
    }

    [Fact]
    public async Task CaixaComDigitacoesFazRoundTripCompleto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, formaPagamentoId, caixaId) = await SemearBaseAsync(fixture);

        using (var escrita = fixture.CriarContexto())
        {
            var caixa = await escrita.Caixas.SingleAsync(c => c.Id == caixaId);
            caixa.Digitacoes.Add(new DigitacaoCaixa { CaixaId = caixaId, FormaPagamentoId = formaPagamentoId, Valor = 235.60m });
            caixa.DigitacoesBandeiras.Add(new DigitacaoBandeiraCaixa { CaixaId = caixaId, Bandeira = "VISA", Valor = 235.60m });
            await escrita.SaveChangesAsync();
        }

        using var leitura = fixture.CriarContexto();
        var caixaLido = await leitura.Caixas
            .Include(c => c.Digitacoes)
            .Include(c => c.DigitacoesBandeiras)
            .SingleAsync(c => c.Id == caixaId);

        Assert.Single(caixaLido.Digitacoes);
        Assert.Equal(235.60m, caixaLido.Digitacoes.Single().Valor);
        Assert.Single(caixaLido.DigitacoesBandeiras);
        Assert.Equal("VISA", caixaLido.DigitacoesBandeiras.Single().Bandeira);
    }

    [Fact]
    public async Task ApagarCaixaApagaDigitacoesJunto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, formaPagamentoId, caixaId) = await SemearBaseAsync(fixture);

        using (var escrita = fixture.CriarContexto())
        {
            var caixa = await escrita.Caixas.SingleAsync(c => c.Id == caixaId);
            caixa.Digitacoes.Add(new DigitacaoCaixa { CaixaId = caixaId, FormaPagamentoId = formaPagamentoId, Valor = 10m });
            await escrita.SaveChangesAsync();

            escrita.Caixas.Remove(caixa);
            await escrita.SaveChangesAsync();
        }

        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.DigitacoesCaixa);
    }
}

using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Tests;

// O número do pedido é MAX + 1 lido do banco. Com dois processos do app no MESMO banco, os dois podem ler o mesmo MAX e
// o índice único recusa o segundo: em vez de perder a venda que o operador acabou de finalizar, tenta o próximo número.
public class NumeroPedidoColisaoTests
{
    private sealed record Base(int CaixaId, int ProdutoId, int FormaId);

    private static async Task<Base> SemearAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
        var produto = new Produto { Nome = "Refri", PrecoVenda = 10m, IdExterno = 10, ProdutoIdApi = 100 };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", IdExterno = 5 };
        context.AddRange(funcionario, produto, forma);
        await context.SaveChangesAsync();
        var caixa = new Models.Caixa
        {
            FuncionarioId = funcionario.Id, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1,
            DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0), TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();
        return new Base(caixa.Id, produto.Id, forma.Id);
    }

    // Um "outro processo": grava uma venda com o número que ESTE contexto está prestes a usar, logo antes do SaveChanges.
    private static async Task GravarVendaConcorrenteAsync(SqliteInMemoryFixture fixture, int caixaId, int numero)
    {
        await using var outro = fixture.CriarContexto();
        outro.Vendas.Add(new Venda { CaixaId = caixaId, NumeroPedido = numero, DataHora = DateTime.Now });
        await outro.SaveChangesAsync();
    }

    private static Func<AppDbContext> FabricaComConcorrencia(SqliteInMemoryFixture fixture, int caixaId, Func<int, bool> concorrerNaTentativa, Action registrarTentativa)
    {
        var tentativa = 0;
        return () =>
        {
            var context = fixture.CriarContexto();
            var atual = ++tentativa;
            // Só os contextos que GRAVAM (o de conferência e o de leitura não disparam SavingChanges).
            context.SavingChanges += (_, _) =>
            {
                registrarTentativa();
                var numero = context.ChangeTracker.Entries<Venda>().Single().Entity.NumeroPedido;
                if (concorrerNaTentativa(atual))
                    GravarVendaConcorrenteAsync(fixture, caixaId, numero).GetAwaiter().GetResult();
            };
            return context;
        };
    }

    private static Task<Venda> RegistrarAsync(VendaService servico, Base b) =>
        servico.RegistrarVendaLocalAsync(b.CaixaId, null, new[] { (b.ProdutoId, 1m, 10m, 0m, 0m) }, new[] { (b.FormaId, 10m) });

    [Fact]
    public async Task ColisaoUmaVezTentaOProximoNumeroEGravaAVenda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        var gravacoes = 0;
        var servico = new VendaService(FabricaComConcorrencia(fixture, b.CaixaId, tentativa => tentativa == 1, () => gravacoes++));

        var venda = await RegistrarAsync(servico, b);

        Assert.Equal(2, venda.NumeroPedido);   // o 1 foi do outro processo
        Assert.Equal(2, gravacoes);            // uma tentativa que colidiu + uma que gravou
        using var leitura = fixture.CriarContexto();
        Assert.Equal(new[] { 1, 2 }, leitura.Vendas.Select(v => v.NumeroPedido).OrderBy(n => n));
        var gravada = leitura.Vendas.Include(v => v.Itens).Include(v => v.Pagamentos).Single(v => v.Id == venda.Id);
        Assert.Single(gravada.Itens);          // a venda que sobreviveu está completa
        Assert.Single(gravada.Pagamentos);
    }

    [Fact]
    public async Task ColisoesSeguidasAteOLimiteSobemEmVezDeInsistirParaSempre()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        var gravacoes = 0;
        var servico = new VendaService(FabricaComConcorrencia(fixture, b.CaixaId, _ => true, () => gravacoes++));

        await Assert.ThrowsAsync<DbUpdateException>(() => RegistrarAsync(servico, b));

        Assert.Equal(5, gravacoes);   // 5 tentativas e desiste
        using var leitura = fixture.CriarContexto();
        Assert.All(leitura.Vendas, v => Assert.Empty(v.Itens));   // nenhuma "venda nossa" ficou gravada pela metade
    }

    [Fact]
    public async Task FalhaQueNaoEColisaoNaoEhRepetida()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var gravacoes = 0;
        var servico = new VendaService(FabricaComConcorrencia(fixture, 0, _ => false, () => gravacoes++));
        var caixaInexistente = new Base(CaixaId: 9999, ProdutoId: 1, FormaId: 1);   // viola a chave estrangeira

        await Assert.ThrowsAsync<DbUpdateException>(() => RegistrarAsync(servico, caixaInexistente));

        Assert.Equal(1, gravacoes);   // não é caso de repetir: sobe na hora
    }

    [Fact]
    public async Task SemConcorrenciaContinuaSequencialSemRepetirNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearAsync(fixture);
        var gravacoes = 0;
        var servico = new VendaService(FabricaComConcorrencia(fixture, b.CaixaId, _ => false, () => gravacoes++));

        var v1 = await RegistrarAsync(servico, b);
        var v2 = await RegistrarAsync(servico, b);

        Assert.Equal(new[] { 1, 2 }, new[] { v1.NumeroPedido, v2.NumeroPedido });
        Assert.Equal(2, gravacoes);
    }
}

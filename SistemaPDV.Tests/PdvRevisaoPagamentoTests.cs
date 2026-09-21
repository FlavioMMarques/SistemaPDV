using System.Reactive.Linq;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Achados da revisão de código do painel de pagamento (Fase 7b): cada teste foi escrito ANTES da correção e falhou.
public class PdvRevisaoPagamentoTests
{
    private sealed record Base(int CaixaId, FormaPagamento Dinheiro, FormaPagamento Pix);

    private static async Task<(PdvViewModel Vm, Base B)> AbrirAsync(SqliteInMemoryFixture fixture, decimal precoDoProduto = 24.49m)
    {
        int operadorId;
        FormaPagamento dinheiro, pix;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            dinheiro = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
            pix = new FormaPagamento { Nome = "PIX", Tipo = "ESPECIE", CodigoNfce = "17", IdExterno = 28 };
            context.AddRange(operador, dinheiro, pix);
            context.Produtos.Add(new Produto { Nome = "Café", PrecoVenda = precoDoProduto, CodigoBarras = "789100030", IdExterno = 1, ProdutoIdApi = 10 });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }
        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;
        var vm = new PdvViewModel(new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), caixa.Id);
        await vm.IniciarAsync();
        return (vm, new Base(caixa.Id, dinheiro, pix));
    }

    // ---- pagamento que passa do total depois de tirar um item ----

    [Fact]
    public async Task TirarUmItemDepoisDePagarNaoDeixaFinalizarComPagamentoAMaisNoPix()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());   // 48,98
        vm.ValorPagamentoAdicionar = "48,98";
        vm.AdicionarPagamento(b.Pix);                         // pago por PIX (que não dá troco)
        Assert.True(vm.PodeFinalizarVenda);

        vm.RemoverItem(vm.Itens[0]);                          // o total cai para 24,49; o PIX continua 48,98

        Assert.False(vm.PodeFinalizarVenda);                  // finalizar gravaria um PIX de 48,98 numa venda de 24,49
        Assert.True(vm.PagamentosPassamDoTotal);
        Assert.Contains("48,98", vm.AvisoExcesso);
        Assert.Contains("24,49", vm.AvisoExcesso);
    }

    [Fact]
    public async Task RemoverOPagamentoQueSobrouLiberaDeNovoEOAvisoSome()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());
        vm.ValorPagamentoAdicionar = "48,98";
        vm.AdicionarPagamento(b.Pix);
        vm.RemoverItem(vm.Itens[0]);
        Assert.True(vm.PagamentosPassamDoTotal);

        vm.RemoverPagamento(vm.Pagamentos.Single());

        Assert.False(vm.PagamentosPassamDoTotal);
        Assert.Null(vm.AvisoExcesso);
    }

    [Fact]
    public async Task ConfirmarComPagamentoAcimaDoTotalNaoGravaEExplicaOMotivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());
        vm.ValorPagamentoAdicionar = "48,98";
        vm.AdicionarPagamento(b.Pix);
        vm.RemoverItem(vm.Itens[0]);
        await vm.AbrirPagamentoCommand.Execute();

        await vm.AvancarCommand.Execute();   // F10

        Assert.True(vm.EmPagamento);
        Assert.Contains("acima do total", vm.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Vendas);
    }

    [Fact]
    public async Task DinheiroComTrocoNaoDisparaOAvisoDeExcesso()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());
        vm.ValorPagamentoAdicionar = "50,00";

        vm.AdicionarPagamento(b.Dinheiro);   // 24,49 aplicados, 25,51 de troco

        Assert.False(vm.PagamentosPassamDoTotal);
        Assert.True(vm.PodeFinalizarVenda);
    }

    // ---- total fracionado (peso × preço): pagamentos em centavos ----

    [Fact]
    public async Task TotalFracionadoAceitaOPixArredondadoEmCentavos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture, precoDoProduto: 9.99m);
        vm.QuantidadeAdicionar = "0,333";
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());   // 0,333 × 9,99 = 3,32667 → a tela mostra R$ 3,33
        Assert.Equal("3,33", vm.ResumoTotalEmCentavos());

        vm.ValorPagamentoAdicionar = "3,33";
        vm.AdicionarPagamento(b.Pix);

        Assert.Single(vm.Pagamentos);                        // não pode recusar "3,33" para um total exibido como 3,33
        Assert.True(vm.PodeFinalizarVenda);
    }

    [Fact]
    public async Task TotalFracionadoNoDinheiroGravaCentavosExatosETrocoCerto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture, precoDoProduto: 9.99m);
        vm.QuantidadeAdicionar = "0,333";
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());

        vm.ValorPagamentoAdicionar = "5,00";
        vm.AdicionarPagamento(b.Dinheiro);

        var pagamento = vm.Pagamentos.Single();
        Assert.Equal(3.33m, pagamento.Valor);                // centavos, não 3,32667
        Assert.Equal(1.67m, pagamento.Troco);
    }

    // ---- fechar o painel ----

    [Fact]
    public async Task FecharOPainelDesmarcaAFormaEALimpaAMensagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());
        await vm.AbrirPagamentoCommand.Execute();
        await vm.SelecionarFormaCommand.Execute(vm.OpcoesPagamento.Single(o => o.Forma.IdExterno == b.Pix.IdExterno));
        vm.ValorPagamentoAdicionar = "abc";
        vm.AdicionarSelecionadaCommand.Execute().Subscribe();   // gera uma mensagem de erro
        Assert.NotNull(vm.Mensagem);

        await vm.FecharPagamentoCommand.Execute();

        Assert.False(vm.EmPagamento);
        Assert.Null(vm.FormaSelecionada);                       // reabrir não traz a forma antiga marcada
        Assert.Null(vm.Mensagem);
        Assert.All(vm.OpcoesPagamento, o => Assert.False(o.Selecionada));
    }
}

internal static class PdvViewModelTestExtensions
{
    // O total como a tela o mostra ("R$ {0:F2}" do Total): é o que o operador lê e digita como pagamento.
    public static string ResumoTotalEmCentavos(this PdvViewModel vm) => ValorMonetario.Formatar(vm.Total);
}

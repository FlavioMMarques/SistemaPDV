using System.Reactive.Linq;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Lógica por trás do visual do protótipo (Fase 7): busca por nome/código com Enter de leitor de código de barras, cupom
// numerado com subtotal/desconto/total, e o painel de pagamento aberto no Finalizar/F10.
public class PdvTelaPrototipoTests
{
    private sealed record Base(int CaixaId, FormaPagamento Dinheiro, FormaPagamento Pix);

    private static async Task<Base> SemearAsync(SqliteInMemoryFixture fixture)
    {
        int operadorId;
        FormaPagamento dinheiro, pix;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            dinheiro = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", IdExterno = 5 };
            pix = new FormaPagamento { Nome = "PIX", Tipo = "ESPECIE", IdExterno = 28 };
            context.AddRange(operador, dinheiro, pix);
            context.Produtos.AddRange(
                new Produto { Nome = "Arroz Parboilizado 1kg", CodigoBarras = "789100010", PrecoVenda = 6.89m, EstoqueAtual = 85, UnidadeMedida = "UN", IdExterno = 1 },
                new Produto { Nome = "Feijão Preto Tipo 1 1kg", CodigoBarras = "789100020", PrecoVenda = 7.99m, EstoqueAtual = 62, UnidadeMedida = "UN", IdExterno = 2 },
                new Produto { Nome = "Café Torrado Tradicional 500g", CodigoBarras = "789100030", Sku = "CAFE500", PrecoVenda = 16.50m, EstoqueAtual = 40, UnidadeMedida = "UN", IdExterno = 3 },
                new Produto { Nome = "Banana Prata", CodigoBarras = "789400020", PrecoVenda = 5.99m, EstoqueAtual = 55, UnidadeMedida = "KG", IdExterno = 4 });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;
        return new Base(caixa.Id, dinheiro, pix);
    }

    private static async Task<(PdvViewModel Vm, Base B)> AbrirAsync(SqliteInMemoryFixture fixture)
    {
        var b = await SemearAsync(fixture);
        var vm = new PdvViewModel(new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), b.CaixaId);
        await vm.IniciarAsync();
        return (vm, b);
    }

    private static Produto Produto(PdvViewModel vm, string codigo) => vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == codigo);

    // ---- cupom ----

    [Fact]
    public async Task ItensSaoNumeradosERenumeradosQuandoUmSai()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100010"));
        vm.AdicionarItem(Produto(vm, "789100020"));
        vm.AdicionarItem(Produto(vm, "789100030"));
        Assert.Equal(new[] { 1, 2, 3 }, vm.Itens.Select(i => i.Numero));

        vm.RemoverItem(vm.Itens[1]);

        Assert.Equal(new[] { 1, 2 }, vm.Itens.Select(i => i.Numero));   // o 3 virou 2
        Assert.Equal("Café Torrado Tradicional 500g", vm.Itens[1].Produto.Nome);
    }

    [Fact]
    public async Task LinhaDoItemMostraQuantidadeUnidadePrecoECodigo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100030"));
        vm.QuantidadeAdicionar = "0,5";
        vm.AdicionarItem(Produto(vm, "789400020"));

        Assert.Equal("1 un x R$ 16,50 (789100030)", vm.Itens[0].Descricao);
        Assert.Equal("0,5 kg x R$ 5,99 (789400020)", vm.Itens[1].Descricao);   // fração e unidade do produto
    }

    [Fact]
    public async Task ResumoSubtotalDescontoETotalBatem()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        Assert.Equal("0 item(ns)", vm.ResumoItens);
        Assert.False(vm.TemItens);

        vm.AdicionarItem(Produto(vm, "789100030"));   // 16,50
        vm.AdicionarItem(Produto(vm, "789100020"));   // 7,99
        vm.Itens[0].DescontoItem = 2m;

        Assert.Equal("2 item(ns)", vm.ResumoItens);
        Assert.True(vm.TemItens);
        Assert.Equal(24.49m, vm.Subtotal);
        Assert.Equal(2m, vm.TotalDescontos);
        Assert.Equal(22.49m, vm.Subtotal - vm.TotalDescontos);
        Assert.Equal(22.49m, vm.Total);
    }

    // ---- busca ----

    [Fact]
    public async Task BuscaAchaPorNomeEPorCodigoDeBarrasSkuOuReferencia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);

        vm.FiltroProduto = "arroz";
        Assert.Equal(new[] { "Arroz Parboilizado 1kg" }, vm.ProdutosFiltrados.Select(p => p.Nome));

        vm.FiltroProduto = "7891000";     // pedaço do código de barras
        Assert.Equal(3, vm.ProdutosFiltrados.Count);

        vm.FiltroProduto = "cafe500";     // SKU, sem diferenciar maiúsculas
        Assert.Equal(new[] { "Café Torrado Tradicional 500g" }, vm.ProdutosFiltrados.Select(p => p.Nome));
    }

    [Fact]
    public async Task EnterComCodigoDeBarrasExatoAdicionaOProdutoELimpaABusca()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.FiltroProduto = "789100020";   // o leitor digita o código e manda Enter

        await vm.AdicionarPorBuscaCommand.Execute();

        Assert.Equal("Feijão Preto Tipo 1 1kg", Assert.Single(vm.Itens).Produto.Nome);
        Assert.Equal(string.Empty, vm.FiltroProduto);   // pronto pro próximo código
        Assert.Null(vm.Mensagem);
    }

    [Fact]
    public async Task EnterComBuscaQueRestringeAUmProdutoAdicionaEsseProduto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.FiltroProduto = "banana";

        await vm.AdicionarPorBuscaCommand.Execute();

        Assert.Equal("Banana Prata", Assert.Single(vm.Itens).Produto.Nome);
    }

    [Fact]
    public async Task EnterComVariosResultadosNaoAdicionaNadaEAvisa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.FiltroProduto = "7891";   // 3 produtos

        await vm.AdicionarPorBuscaCommand.Execute();

        Assert.Empty(vm.Itens);   // nunca adiciona às cegas
        Assert.Contains("Vários", vm.Mensagem);
        Assert.Equal("7891", vm.FiltroProduto);
    }

    [Fact]
    public async Task EnterSemResultadoAvisaEEnterVazioNaoFazNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.FiltroProduto = "zzz";

        await vm.AdicionarPorBuscaCommand.Execute();
        Assert.Empty(vm.Itens);
        Assert.Contains("zzz", vm.Mensagem);

        vm.FiltroProduto = "  ";
        await vm.AdicionarPorBuscaCommand.Execute();
        Assert.Empty(vm.Itens);
    }

    [Fact]
    public async Task LimparZeraABuscaEEstadoVazioDiferenciaBuscaSemResultadoDeCatalogoVazio()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.FiltroProduto = "zzz";
        Assert.True(vm.SemResultados);
        Assert.Contains("“zzz”", vm.TextoSemResultados);

        await vm.LimparBuscaCommand.Execute();

        Assert.Equal(string.Empty, vm.FiltroProduto);
        Assert.False(vm.SemResultados);

        // catálogo ainda vazio (1ª sincronização): mensagem diferente — a ação do operador é esperar, não trocar o texto
        using var vazio = new SqliteInMemoryFixture();
        var caixaId = (await SemearAsync(vazio)).CaixaId;
        await using (var context = vazio.CriarContexto())
        {
            context.Produtos.RemoveRange(context.Produtos);
            await context.SaveChangesAsync();
        }
        var vmVazio = new PdvViewModel(new VendaService(vazio.CriarContexto), new CatalogoLocalService(vazio.CriarContexto), caixaId);
        await vmVazio.IniciarAsync();
        Assert.True(vmVazio.SemResultados);
        Assert.Contains("sincroniz", vmVazio.TextoSemResultados);
    }

    // ---- painel de pagamento ----

    [Fact]
    public async Task NaoAbrePagamentoComCupomVazio()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        var pode = true;
        using var inscricao = vm.AbrirPagamentoCommand.CanExecute.Subscribe(v => pode = v);

        Assert.False(pode);
        vm.AdicionarItem(Produto(vm, "789100010"));
        Assert.True(pode);
    }

    [Fact]
    public async Task AbrirPagamentoSugereOValorQueFaltaEAdicionarSugereOResto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100030"));   // 16,50
        vm.AdicionarItem(Produto(vm, "789100020"));   // 7,99 → 24,49

        await vm.AbrirPagamentoCommand.Execute();

        Assert.True(vm.EmPagamento);
        Assert.Equal("24,49", vm.ValorPagamentoAdicionar);   // o operador só escolhe a forma
        vm.ValorPagamentoAdicionar = "10,00";
        vm.AdicionarPagamento(b.Dinheiro);
        Assert.Equal(10m, vm.TotalPago);
        Assert.Equal(14.49m, vm.Restante);
        Assert.Equal(0m, vm.Troco);
        Assert.Equal("14,49", vm.ValorPagamentoAdicionar);   // pagamento misto: o próximo já vem com o que falta
    }

    [Fact]
    public async Task PagarAMaisGeraTrocoESemRestante()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100010"));   // 6,89
        vm.ValorPagamentoAdicionar = "10,00";

        vm.AdicionarPagamento(b.Dinheiro);

        Assert.Equal(0m, vm.Restante);
        Assert.Equal(3.11m, vm.Troco);
        Assert.Equal(string.Empty, vm.ValorPagamentoAdicionar);   // nada mais a sugerir
    }

    [Fact]
    public async Task EscComPainelAbertoSoVoltaAoCupomSemPerderNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100010"));
        await vm.AbrirPagamentoCommand.Execute();

        await vm.EscCommand.Execute();

        Assert.False(vm.EmPagamento);
        Assert.Single(vm.Itens);   // o cupom continua
    }

    [Fact]
    public async Task EscComPainelFechadoCancelaAVenda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100010"));

        await vm.EscCommand.Execute();

        Assert.Empty(vm.Itens);
    }

    [Fact]
    public async Task F10AbreOPainelDepoisSoConfirmaComOValorPagoEFechaOPainel()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100010"));   // 6,89

        await vm.AvancarCommand.Execute();            // 1ª batida: abre
        Assert.True(vm.EmPagamento);

        await vm.AvancarCommand.Execute();            // 2ª batida sem pagar: não confirma, diz quanto falta
        Assert.True(vm.EmPagamento);
        Assert.Contains("6,89", vm.Mensagem);
        using (var leitura = fixture.CriarContexto())
            Assert.Empty(leitura.Vendas);

        vm.AdicionarPagamento(b.Dinheiro);            // o valor já vinha sugerido (6,89)
        await vm.AvancarCommand.Execute();            // 3ª batida: confirma

        Assert.False(vm.EmPagamento);                 // limpou e voltou ao cupom
        Assert.Empty(vm.Itens);
        using var depois = fixture.CriarContexto();
        Assert.Single(depois.Vendas);
    }

    [Fact]
    public async Task F10ComCupomVazioNaoAbreNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);

        await vm.AvancarCommand.Execute();

        Assert.False(vm.EmPagamento);
    }

    [Fact]
    public async Task FinalizarPeloBotaoDoPainelFechaOPainelECancelarTambem()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100010"));
        await vm.AbrirPagamentoCommand.Execute();
        vm.AdicionarPagamento(b.Pix);

        await vm.FinalizarVendaCommand.Execute();

        Assert.False(vm.EmPagamento);

        vm.AdicionarItem(Produto(vm, "789100020"));
        await vm.AbrirPagamentoCommand.Execute();
        await vm.CancelarCommand.Execute();
        Assert.False(vm.EmPagamento);   // cancelar a venda também fecha o painel
    }

    [Fact]
    public async Task ComOPainelAbertoAVendaContinuaContandoComoEmAndamento()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(Produto(vm, "789100010"));

        await vm.AbrirPagamentoCommand.Execute();

        Assert.True(vm.TemVendaEmAndamento);   // o Shell segue bloqueando a navegação
    }
}

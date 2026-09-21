using System.Reactive.Linq;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Produto com preço R$ 0,00 no cadastro: o PDV pede o preço na hora de lançar. Regras do usuário (2026-09-21): o preço tem de
// ser MAIOR que zero (Cancelar não lança o item) e qualquer operador pode informá-lo; o valor vale só para aquele item do cupom.
public class PrecoZeroTests
{
    private sealed record Base(int CaixaId, int CafeId, int BalaId, int FormaId);

    private static async Task<(PdvViewModel Vm, Base B, Produto Bala, Produto Cafe)> AbrirAsync(SqliteInMemoryFixture fixture)
    {
        int operadorId;
        FormaPagamento dinheiro;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            dinheiro = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
            context.AddRange(operador, dinheiro);
            context.Produtos.AddRange(
                new Produto { Nome = "Café", PrecoVenda = 16.50m, CodigoBarras = "789100030", IdExterno = 1, ProdutoIdApi = 10 },
                new Produto { Nome = "Bala a granel", PrecoVenda = 0m, CodigoBarras = "789500030", IdExterno = 2, ProdutoIdApi = 20, UnidadeMedida = "KG" });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }
        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;
        var vm = new PdvViewModel(new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), caixa.Id);
        await vm.IniciarAsync();
        var cafe = vm.ProdutosDisponiveis.Single(p => p.Nome == "Café");
        var bala = vm.ProdutosDisponiveis.Single(p => p.Nome == "Bala a granel");
        return (vm, new Base(caixa.Id, cafe.Id, bala.Id, dinheiro.Id), bala, cafe);
    }

    // ---- lançar ----

    [Fact]
    public async Task ProdutoSemPrecoNaoEntraNoCupomSozinhoEAbreOPainelDePreco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, _) = await AbrirAsync(fixture);
        var avisos = new List<string>();
        using var assinatura = vm.ItemLancado.Subscribe(avisos.Add);

        vm.AdicionarItem(bala);

        Assert.Empty(vm.Itens);                                   // nada de item de graça por engano
        Assert.True(vm.PedindoPreco);
        Assert.Same(bala, vm.ProdutoAguardandoPreco);
        Assert.True(vm.ModalAberto);                              // a tela de trás dorme
        Assert.Empty(avisos);                                     // e não avisa "adicionado ao cupom" de um item que não entrou
        Assert.True(bala.SemPreco);
    }

    [Fact]
    public async Task ProdutoComPrecoContinuaEntrandoDireto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, _, cafe) = await AbrirAsync(fixture);

        vm.AdicionarItem(cafe);

        Assert.Single(vm.Itens);
        Assert.False(vm.PedindoPreco);
        Assert.False(vm.Itens[0].PrecoInformado);
        Assert.False(cafe.SemPreco);
    }

    [Fact]
    public async Task ConfirmarLancaOItemComOPrecoInformadoSemMudarOCadastro()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, _) = await AbrirAsync(fixture);
        var avisos = new List<string>();
        using var assinatura = vm.ItemLancado.Subscribe(avisos.Add);
        vm.QuantidadeAdicionar = "0,5";                           // meio quilo
        vm.AdicionarItem(bala);

        vm.PrecoInformado = "12,50";
        await vm.ConfirmarPrecoCommand.Execute();

        var item = Assert.Single(vm.Itens);
        Assert.Equal(12.50m, item.PrecoUnitario);
        Assert.Equal(0.5m, item.Quantidade);
        Assert.Equal(6.25m, item.Total);                          // a quantidade continua valendo
        Assert.True(item.PrecoInformado);
        Assert.Contains("preço informado", item.Descricao);       // o cupom diz que não é o preço de tabela
        Assert.False(vm.PedindoPreco);
        Assert.False(vm.ModalAberto);
        Assert.Equal(new[] { "Bala a granel" }, avisos);          // agora sim o aviso
        Assert.Equal(0m, bala.PrecoVenda);                        // o cadastro (em memória) não mudou
        await using var leitura = fixture.CriarContexto();
        Assert.Equal(0m, (await leitura.Produtos.SingleAsync(p => p.Nome == "Bala a granel")).PrecoVenda);   // nem o banco
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("12,345")]          // mais casas do que centavos: ambíguo, recusa em vez de adivinhar
    [InlineData("1.234")]           // milhar ou fração? em dinheiro é recusado
    public async Task PrecoInvalidoOuZeroNaoLancaEMantemOPainelAbertoComOMotivo(string digitado)
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(bala);

        vm.PrecoInformado = digitado;
        await vm.ConfirmarPrecoCommand.Execute();

        Assert.Empty(vm.Itens);
        Assert.True(vm.PedindoPreco);                             // continua pedindo: não vira item de graça
        Assert.Contains("maior que zero", vm.MensagemPreco);
    }

    [Fact]
    public async Task PrecoAcimaDoTetoDeSanidadeEhRecusadoMasOTetoEmSiPassa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(bala);

        vm.PrecoInformado = "1000000,01";
        await vm.ConfirmarPrecoCommand.Execute();
        Assert.Empty(vm.Itens);
        Assert.Contains("Confira o valor", vm.MensagemPreco);

        vm.PrecoInformado = "1000000";
        await vm.ConfirmarPrecoCommand.Execute();
        Assert.Single(vm.Itens);
    }

    [Fact]
    public async Task PrecoComPontoOuVirgulaValeIgual()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(bala);
        vm.PrecoInformado = "3.75";
        await vm.ConfirmarPrecoCommand.Execute();

        Assert.Equal(3.75m, vm.Itens.Single().PrecoUnitario);
    }

    // ---- cancelar ----

    [Fact]
    public async Task CancelarNaoLancaOItemELimpaOPainel()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(bala);
        vm.PrecoInformado = "9";

        await vm.CancelarPrecoCommand.Execute();

        Assert.Empty(vm.Itens);
        Assert.False(vm.PedindoPreco);
        Assert.Equal(string.Empty, vm.PrecoInformado);
        Assert.Null(vm.MensagemPreco);
    }

    [Fact]
    public async Task EscComOPainelDePrecoAbertoSoOFechaENaoCancelaAVenda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, cafe) = await AbrirAsync(fixture);
        vm.AdicionarItem(cafe);                                   // já há um item no cupom
        vm.AdicionarItem(bala);

        await vm.EscCommand.Execute();

        Assert.False(vm.PedindoPreco);
        Assert.Single(vm.Itens);                                  // o cupom ficou como estava: o Esc só descartou o lançamento
    }

    [Fact]
    public async Task NovaVendaComOPainelAbertoDescartaOPainelEOCupom()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, cafe) = await AbrirAsync(fixture);
        vm.AdicionarItem(cafe);
        vm.AdicionarItem(bala);

        await vm.NovoCommand.Execute();

        Assert.False(vm.PedindoPreco);
        Assert.Empty(vm.Itens);
    }

    // ---- por onde o produto chega ----

    [Fact]
    public async Task LeitorDeCodigoDeBarrasComProdutoSemPrecoTambemPedeOPreco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, _, _) = await AbrirAsync(fixture);
        vm.FiltroProduto = "789500030";                           // o leitor digita o código e manda Enter

        await vm.AdicionarPorBuscaCommand.Execute();

        Assert.Empty(vm.Itens);
        Assert.True(vm.PedindoPreco);
        Assert.Equal("Bala a granel", vm.ProdutoAguardandoPreco!.Nome);
    }

    [Fact]
    public async Task CliqueNoCardPeloComandoTambemPedeOPreco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, _) = await AbrirAsync(fixture);

        await vm.AdicionarItemCommand.Execute(bala);

        Assert.True(vm.PedindoPreco);
        Assert.Empty(vm.Itens);
    }

    // ---- o painel por cima do resto ----

    [Fact]
    public async Task ComOPainelDePrecoAbertoF10NaoAbreOPagamentoNemPagaPorBaixo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _, bala, cafe) = await AbrirAsync(fixture);
        vm.AdicionarItem(cafe);
        vm.AdicionarItem(bala);
        Assert.False(await vm.AbrirPagamentoCommand.CanExecute.FirstAsync());

        await vm.AvancarCommand.Execute();                        // F10

        Assert.False(vm.EmPagamento);
        Assert.True(vm.PedindoPreco);                             // continua pedindo o preço
    }

    // ---- a venda gravada ----

    [Fact]
    public async Task VendaFinalizadaGravaOPrecoInformadoEPodeSerEnviada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b, bala, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(bala);
        vm.PrecoInformado = "12,50";
        await vm.ConfirmarPrecoCommand.Execute();
        vm.ValorPagamentoAdicionar = "12,50";
        vm.AdicionarPagamento(vm.FormasPagamentoDisponiveis.Single());

        var venda = await vm.FinalizarVendaCommand.Execute();

        Assert.NotNull(venda);
        await using var leitura = fixture.CriarContexto();
        var item = await leitura.ItensVenda.SingleAsync(i => i.VendaId == venda!.Id);
        Assert.Equal(12.50m, item.PrecoUnitario);                 // é isso que viaja à API (o preço do item já é enviado)
        Assert.Equal(b.BalaId, item.ProdutoId);
    }

    [Fact]
    public async Task ServicoDeVendaRecusaItemComPrecoZeroOuNegativoMesmoSeATelaFalhar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (_, b, _, _) = await AbrirAsync(fixture);
        var servico = new VendaService(fixture.CriarContexto);

        await Assert.ThrowsAsync<ArgumentException>(() => servico.RegistrarVendaLocalAsync(
            b.CaixaId, null, new[] { (b.BalaId, 1m, 0m, 0m, 0m) }, new[] { (b.FormaId, 0m) }));
        await Assert.ThrowsAsync<ArgumentException>(() => servico.RegistrarVendaLocalAsync(
            b.CaixaId, null, new[] { (b.CafeId, 1m, -3m, 0m, 0m) }, new[] { (b.FormaId, 1m) }));

        await using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Vendas);                             // nada foi gravado
    }
}

using System.Reactive.Linq;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Desconto na venda toda, digitado no cupom em R$ ou em %. A API só aceita desconto por item, então o valor é rateado entre
// os itens (RateioDeDesconto) — e continua valendo quando o cupom muda (item entra/sai).
public class PdvDescontoTests
{
    private sealed record Base(int CaixaId, FormaPagamento Pix);

    private static async Task<(PdvViewModel Vm, Base B)> AbrirAsync(SqliteInMemoryFixture fixture)
    {
        int operadorId;
        FormaPagamento pix;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            pix = new FormaPagamento { Nome = "PIX", Tipo = "ESPECIE", IdExterno = 28 };
            context.AddRange(operador, pix);
            context.Produtos.AddRange(
                new Produto { Nome = "Arroz Parboilizado 1kg", CodigoBarras = "789100010", PrecoVenda = 6.89m, EstoqueAtual = 85, UnidadeMedida = "UN", IdExterno = 1 },
                new Produto { Nome = "Feijão Preto Tipo 1 1kg", CodigoBarras = "789100020", PrecoVenda = 7.99m, EstoqueAtual = 62, UnidadeMedida = "UN", IdExterno = 2 });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;
        var vm = new PdvViewModel(new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), caixa.Id);
        await vm.IniciarAsync();
        return (vm, new Base(caixa.Id, pix));
    }

    // Arroz 6,89 + Feijão 7,99 = subtotal 14,88.
    private static async Task<(PdvViewModel Vm, Base B)> ComDoisItensAsync(SqliteInMemoryFixture fixture)
    {
        var (vm, b) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == "789100010"));
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == "789100020"));
        return (vm, b);
    }

    private static void Aplicar(PdvViewModel vm, string texto, bool percentual = false)
    {
        vm.DescontoDigitado = texto;
        vm.DescontoEmPercentual = percentual;
        vm.AplicarDescontoCommand.Execute().Subscribe();
    }

    [Fact]
    public async Task DescontoEmReaisReduzOTotalERepartePelosItens()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "2,00");

        Assert.Equal(14.88m, vm.Subtotal);
        Assert.Equal(2m, vm.TotalDescontos);
        Assert.Equal("− R$ 2,00", vm.TextoDesconto);
        Assert.Equal(12.88m, vm.Total);
        Assert.Equal(2m, vm.Itens.Sum(i => i.DescontoItem));           // é o que vai para a API, item a item
        Assert.Equal(12.88m, vm.Itens.Sum(i => i.Total));
        Assert.True(vm.TemDesconto);
        Assert.Null(vm.Mensagem);
    }

    [Fact]
    public async Task DescontoEmPercentualViraReais()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "10", percentual: true);

        Assert.Equal(1.49m, vm.TotalDescontos);            // 10% de 14,88 = 1,488 → 1,49
        Assert.Equal(13.39m, vm.Total);
        Assert.Equal("Desconto (10%):", vm.RotuloDesconto);
    }

    [Fact]
    public async Task OSinalDePorcentoDigitadoValeMesmoComOBotaoEmReais()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "10%", percentual: false);

        Assert.Equal(1.49m, vm.TotalDescontos);
    }

    [Fact]
    public async Task SemDescontoORotuloEOSimples()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Assert.False(vm.TemDesconto);
        Assert.Equal("Desconto:", vm.RotuloDesconto);
        Assert.Equal(0m, vm.TotalDescontos);
        Assert.Equal("R$ 0,00", vm.TextoDesconto);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1,234")]      // mais casas do que centavos
    public async Task ValorInvalidoOuZeroERecusadoSemMexerNoCupom(string digitado)
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, digitado);

        Assert.NotNull(vm.Mensagem);
        Assert.Equal(0m, vm.TotalDescontos);
        Assert.Equal(14.88m, vm.Total);
    }

    [Fact]
    public async Task DescontoIgualOuMaiorQueOSubtotalERecusado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "14,88");
        Assert.Contains("menor que o subtotal", vm.Mensagem);
        Assert.Equal(0m, vm.TotalDescontos);

        Aplicar(vm, "14,87");                    // 1 centavo a menos cabe
        Assert.Null(vm.Mensagem);
        Assert.Equal(14.87m, vm.TotalDescontos);
        Assert.Equal(0.01m, vm.Total);
    }

    [Fact]
    public async Task CemPorCentoOuMaisERecusado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "100", percentual: true);

        Assert.Contains("menor que 100", vm.Mensagem);
        Assert.Equal(0m, vm.TotalDescontos);
    }

    [Fact]
    public async Task PercentualTaoPequenoQueViraZeroERecusado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "0,01", percentual: true);       // 0,01% de 14,88 = R$ 0,0015 → nada

        Assert.NotNull(vm.Mensagem);
        Assert.Equal(0m, vm.TotalDescontos);
    }

    [Fact]
    public async Task UmNovoDescontoSubstituiOAnteriorEmVezDeSomar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "2,00");
        Aplicar(vm, "3,00");

        Assert.Equal(3m, vm.TotalDescontos);
        Assert.Equal(11.88m, vm.Total);
    }

    [Fact]
    public async Task AplicarLimpaOCampoDeDigitacao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);

        Aplicar(vm, "2,00");

        Assert.Equal(string.Empty, vm.DescontoDigitado);
    }

    [Fact]
    public async Task RemoverDescontoZeraTudo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);
        Aplicar(vm, "10", percentual: true);

        vm.RemoverDescontoCommand.Execute().Subscribe();

        Assert.False(vm.TemDesconto);
        Assert.Equal(0m, vm.TotalDescontos);
        Assert.Equal(14.88m, vm.Total);
        Assert.All(vm.Itens, i => Assert.Equal(0m, i.DescontoItem));
        Assert.Equal("Desconto:", vm.RotuloDesconto);
    }

    [Fact]
    public async Task PercentualAcompanhaOCupomQuandoUmItemEntra()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == "789100010"));   // 6,89
        Aplicar(vm, "10", percentual: true);
        Assert.Equal(0.69m, vm.TotalDescontos);

        vm.AdicionarItem(vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == "789100020"));   // subtotal 14,88

        Assert.Equal(1.49m, vm.TotalDescontos);                  // 10% do subtotal NOVO
        Assert.Equal(13.39m, vm.Total);
        Assert.Equal(vm.TotalDescontos, vm.Itens.Sum(i => i.DescontoItem));
    }

    [Fact]
    public async Task ValorFixoContinuaIgualQuandoUmItemEntra()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == "789100010"));
        Aplicar(vm, "2,00");

        vm.AdicionarItem(vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == "789100020"));

        Assert.Equal(2m, vm.TotalDescontos);
        Assert.Equal(12.88m, vm.Total);
    }

    [Fact]
    public async Task ValorFixoQueDeixouDeCaberAoTirarUmItemEDescartadoComAviso()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);
        Aplicar(vm, "10,00");                                     // cabe em 14,88

        vm.RemoverItem(vm.Itens.Last());                          // sobra só o arroz, 6,89: 10,00 não cabe mais

        Assert.False(vm.TemDesconto);
        Assert.Equal(0m, vm.TotalDescontos);
        Assert.Equal(6.89m, vm.Total);
        Assert.Contains("Desconto removido", vm.Mensagem);
    }

    [Fact]
    public async Task EsvaziarOCupomLimpaODesconto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);
        Aplicar(vm, "2,00");

        await vm.NovoCommand.Execute();

        Assert.False(vm.TemDesconto);
        Assert.Equal(string.Empty, vm.DescontoDigitado);
        Assert.False(vm.DescontoEmPercentual);
        Assert.Equal(0m, vm.TotalDescontos);

        // E o desconto antigo não pega carona na próxima venda.
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single(p => p.CodigoBarras == "789100010"));
        Assert.Equal(0m, vm.TotalDescontos);
    }

    [Fact]
    public async Task DescontoSemItensNaoFazNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirAsync(fixture);

        Aplicar(vm, "2,00");

        Assert.NotNull(vm.Mensagem);
        Assert.False(vm.TemDesconto);
    }

    [Fact]
    public async Task AVendaGravaOsDescontosDosItensEOsPagamentosCobremOTotalComDesconto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await ComDoisItensAsync(fixture);
        Aplicar(vm, "2,00");
        vm.ValorPagamentoAdicionar = "12,88";
        vm.AdicionarPagamento(b.Pix);
        Assert.True(vm.PodeFinalizarVenda);

        var venda = await vm.FinalizarVendaCommand.Execute();

        Assert.NotNull(venda);
        await using var context = fixture.CriarContexto();
        var gravada = await context.Vendas.Include(v => v.Itens).Include(v => v.Pagamentos).SingleAsync(v => v.Id == venda!.Id);
        Assert.Equal(2m, gravada.Itens.Sum(i => i.DescontoItem));
        Assert.Equal(12.88m, gravada.Pagamentos.Sum(p => p.Valor));
        Assert.Equal(0m, gravada.Desconto);         // o cabeçalho não leva desconto: ele está nos itens (a API só o aceita por item)
    }

    [Fact]
    public async Task ODetalheDoPedidoMostraOTotalDeDescontoDosItens()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await ComDoisItensAsync(fixture);
        Aplicar(vm, "2,00");
        vm.ValorPagamentoAdicionar = "12,88";
        vm.AdicionarPagamento(b.Pix);
        var venda = (await vm.FinalizarVendaCommand.Execute())!;

        var detalhe = await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id);

        Assert.Equal(2m, detalhe!.Desconto);
    }

    [Fact]
    public async Task OBotaoAlternaEntreReaisEPorcento()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);
        Assert.Equal("R$", vm.TextoTipoDesconto);

        vm.AlternarTipoDescontoCommand.Execute().Subscribe();
        Assert.True(vm.DescontoEmPercentual);
        Assert.Equal("%", vm.TextoTipoDesconto);

        vm.AlternarTipoDescontoCommand.Execute().Subscribe();
        Assert.Equal("R$", vm.TextoTipoDesconto);
    }

    [Fact]
    public async Task AsPropriedadesDoDescontoAvisamATelaQuandoMudam()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);
        var mudadas = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => mudadas.Add(e.PropertyName);

        Aplicar(vm, "10", percentual: true);

        Assert.Contains(nameof(PdvViewModel.TemDesconto), mudadas);
        Assert.Contains(nameof(PdvViewModel.RotuloDesconto), mudadas);
        Assert.Contains(nameof(PdvViewModel.TotalDescontos), mudadas);
        Assert.Contains(nameof(PdvViewModel.Total), mudadas);
    }

    [Fact]
    public async Task ODescontoDoItemAvisaATelaDoItemQuandoMuda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await ComDoisItensAsync(fixture);
        var item = vm.Itens[0];
        var mudadas = new List<string?>();
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => mudadas.Add(e.PropertyName);

        item.DescontoItem = 1m;

        Assert.Contains(nameof(ItemCarrinho.Total), mudadas);
        Assert.Contains(nameof(ItemCarrinho.TemDesconto), mudadas);
        Assert.True(item.TemDesconto);
    }
}

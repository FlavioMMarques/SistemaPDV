using System.Reactive.Linq;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Painel de pagamento no estilo do protótipo: escolhe-se a forma num cartão, informa-se o valor (no dinheiro, o que o
// cliente ENTREGOU — o troco sai daí) e pode-se pagar com mais de uma forma na mesma venda.
public class PagamentoDinheiroComTrocoTests
{
    private sealed record Base(int CaixaId, FormaPagamento Dinheiro, FormaPagamento Pix, FormaPagamento Credito);

    private static async Task<Base> SemearAsync(SqliteInMemoryFixture fixture, params string[] bandeiras)
    {
        int operadorId;
        FormaPagamento dinheiro, pix, credito;
        await using (var context = fixture.CriarContexto())
        {
            var operador = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            dinheiro = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
            pix = new FormaPagamento { Nome = "PIX", Tipo = "ESPECIE", CodigoNfce = "17", IdExterno = 28 };
            credito = new FormaPagamento { Nome = "CARTÃO DE CRÉDITO", Tipo = "CARTAO", CodigoNfce = "03", IdExterno = 11 };
            context.AddRange(operador, dinheiro, pix, credito);
            context.Produtos.Add(new Produto { Nome = "Café", PrecoVenda = 24.49m, CodigoBarras = "789100030", IdExterno = 1, ProdutoIdApi = 10 });
            var id = 1;
            foreach (var nome in bandeiras)
                context.Cartoes.Add(new Cartao { IdExterno = id++, BandeiraNome = nome, BandeiraId = "0" + id, Tipo = "CREDITO" });
            await context.SaveChangesAsync();
            operadorId = operador.Id;
        }

        var caixa = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(operadorId, new DateOnly(2026, 9, 21), 1, 10m)).Valor!;
        return new Base(caixa.Id, dinheiro, pix, credito);
    }

    private static async Task<(PdvViewModel Vm, Base B)> AbrirComCupomDe2449Async(SqliteInMemoryFixture fixture, params string[] bandeiras)
    {
        var b = await SemearAsync(fixture, bandeiras);
        var vm = new PdvViewModel(new VendaService(fixture.CriarContexto), new CatalogoLocalService(fixture.CriarContexto), b.CaixaId);
        await vm.IniciarAsync();
        vm.AdicionarItem(vm.ProdutosDisponiveis.Single());   // R$ 24,49
        await vm.AbrirPagamentoCommand.Execute();
        return (vm, b);
    }

    // A tela carrega as formas do banco (objetos novos): acha a opção pelo id da API, não pela identidade do objeto semeado.
    private static OpcaoPagamento Opcao(PdvViewModel vm, FormaPagamento forma) => vm.OpcoesPagamento.Single(o => o.Forma.IdExterno == forma.IdExterno);

    // ---- que forma é qual ----

    [Theory]
    [InlineData("ESPÉCIE", "ESPECIE", "01", true)]
    [InlineData("DINHEIRO", "ESPECIE", "01", true)]
    [InlineData("Dinheiro", "ESPECIE", null, true)]      // sem código: cai no Tipo
    [InlineData("PIX OFF", "ESPECIE", "20", false)]      // Tipo ESPECIE, mas NÃO é dinheiro (código 20): caso real da API
    [InlineData("CARTÃO DE CRÉDITO", "CARTAO", "03", false)]
    [InlineData("DUPLICATA", "DUPLICATA", "99", false)]
    public void ReconheceODinheiroPeloCodigoDaNfceENaoSoPeloTipo(string nome, string tipo, string? codigo, bool ehDinheiro) =>
        Assert.Equal(ehDinheiro, new FormaPagamento { Nome = nome, Tipo = tipo, CodigoNfce = codigo }.EhDinheiro);

    [Theory]
    [InlineData("PIX", "ESPECIE", "17", "⚡", "Chave dinâmica / QR Code")]
    [InlineData("ESPÉCIE", "ESPECIE", "01", "💵", "Cálculo automático de troco")]
    [InlineData("CARTÃO DE CRÉDITO", "CARTAO", "03", "💳", "Crédito • maquininha")]
    [InlineData("CARTÃO DE DÉBITO", "CARTAO", "04", "🏦", "Débito à vista")]
    [InlineData("DUPLICATA", "DUPLICATA", "99", "🧾", "Duplicata")]
    public void CartaoDoPainelTemIconeEExplicacaoPorTipo(string nome, string tipo, string codigo, string icone, string subtitulo)
    {
        var opcao = new OpcaoPagamento(new FormaPagamento { Nome = nome, Tipo = tipo, CodigoNfce = codigo });

        Assert.Equal(nome, opcao.Titulo);
        Assert.Equal(icone, opcao.Icone);
        Assert.Equal(subtitulo, opcao.Subtitulo);
    }

    // ---- escolher a forma ----

    [Fact]
    public async Task EscolherUmCartaoMarcaSoEleSugereOValorEClicarDeNovoDesmarca()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);

        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Pix));

        Assert.Equal(b.Pix.IdExterno, vm.FormaSelecionada!.IdExterno);
        Assert.True(Opcao(vm, b.Pix).Selecionada);
        Assert.False(Opcao(vm, b.Dinheiro).Selecionada);
        Assert.Equal("24,49", vm.ValorPagamentoAdicionar);

        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Pix));   // de novo: desmarca
        Assert.Null(vm.FormaSelecionada);
        Assert.False(Opcao(vm, b.Pix).Selecionada);
    }

    [Fact]
    public async Task RotuloDoCampoMudaNoDinheiroEOTrocoPrevistoAcompanhaODigitado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);

        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Dinheiro));
        Assert.True(vm.DinheiroSelecionado);
        Assert.Equal("Valor recebido do cliente (R$)", vm.RotuloValor);
        Assert.Equal(0m, vm.TrocoPrevisto);                   // valor sugerido = o que falta: sem troco

        vm.ValorPagamentoAdicionar = "50,00";
        Assert.Equal(25.51m, vm.TrocoPrevisto);               // 50,00 − 24,49

        vm.ValorPagamentoAdicionar = "20";
        Assert.Equal(0m, vm.TrocoPrevisto);                   // entregou menos que o total: nada a devolver

        vm.ValorPagamentoAdicionar = "abc";
        Assert.Equal(0m, vm.TrocoPrevisto);                   // inválido não quebra

        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Dinheiro));   // desmarca
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Pix));
        Assert.False(vm.DinheiroSelecionado);
        Assert.Equal("Valor a lançar (R$)", vm.RotuloValor);
        vm.ValorPagamentoAdicionar = "50,00";
        Assert.Equal(0m, vm.TrocoPrevisto);                   // só o dinheiro dá troco
    }

    [Fact]
    public async Task BandeiraSoApareceParaCartaoComBandeirasSincronizadas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture, "VISA", "MASTERCARD");

        Assert.False(vm.ExigeBandeiraSelecionada);                                 // nada escolhido
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Pix));
        Assert.False(vm.ExigeBandeiraSelecionada);                                 // PIX não é cartão
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Credito));
        Assert.True(vm.ExigeBandeiraSelecionada);
    }

    [Fact]
    public async Task SemBandeirasSincronizadasNemOCartaoPedeBandeira()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);

        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Credito));

        Assert.False(vm.ExigeBandeiraSelecionada);
    }

    // ---- dinheiro e troco ----

    [Fact]
    public async Task DinheiroAMaisLancaSoOQueFaltaEGuardaORecebidoEOTroco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);
        vm.ValorPagamentoAdicionar = "10,00";
        vm.AdicionarPagamento(b.Pix);   // PIX 10,00 → faltam 14,49

        vm.ValorPagamentoAdicionar = "50,00";
        vm.AdicionarPagamento(b.Dinheiro);

        var dinheiro = vm.Pagamentos.Last();
        Assert.Equal(14.49m, dinheiro.Valor);            // o que entra na venda
        Assert.Equal(50m, dinheiro.ValorRecebido);       // o que o cliente entregou
        Assert.Equal(35.51m, dinheiro.Troco);
        Assert.Equal("recebido R$ 50,00 • troco R$ 35,51", dinheiro.Detalhe);
        Assert.Equal(24.49m, vm.TotalPago);
        Assert.Equal(0m, vm.Restante);
        Assert.Equal(35.51m, vm.Troco);
        Assert.True(vm.PodeFinalizarVenda);
    }

    [Fact]
    public async Task DinheiroAMenosEhPagamentoParcialSemTroco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);

        vm.ValorPagamentoAdicionar = "5,00";
        vm.AdicionarPagamento(b.Dinheiro);

        Assert.Equal(5m, vm.Pagamentos.Single().Valor);
        Assert.Equal(0m, vm.Troco);
        Assert.Equal(19.49m, vm.Restante);
        Assert.Equal(string.Empty, vm.Pagamentos.Single().Detalhe);
        Assert.False(vm.PodeFinalizarVenda);
    }

    [Fact]
    public async Task PagamentoMistoDinheiroMaisCartaoSomaOTotalEGravaOsValoresDaVenda()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture, "VISA");
        vm.ValorPagamentoAdicionar = "10,00";
        vm.AdicionarPagamento(b.Dinheiro);
        vm.ValorPagamentoAdicionar = "14,49";
        vm.BandeiraSelecionada = "VISA";
        vm.AdicionarPagamento(b.Credito);

        var venda = await vm.FinalizarVendaCommand.Execute();

        Assert.NotNull(venda);
        using var leitura = fixture.CriarContexto();
        var pagamentos = leitura.PagamentosVenda.ToList().OrderBy(p => p.Valor).ToList();
        Assert.Equal(new[] { 10m, 14.49m }, pagamentos.Select(p => p.Valor));   // o que foi APLICADO à venda
        Assert.Equal(new[] { null, "VISA" }, pagamentos.Select(p => p.Bandeira));
    }

    [Fact]
    public async Task VendaJaPagaNaoAceitaOutroLancamento()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);
        vm.ValorPagamentoAdicionar = "24,49";
        vm.AdicionarPagamento(b.Pix);

        vm.ValorPagamentoAdicionar = "10,00";
        vm.AdicionarPagamento(b.Dinheiro);

        Assert.Single(vm.Pagamentos);                           // não criou um pagamento de R$ 0,00
        Assert.Contains("totalmente paga", vm.Mensagem);
    }

    // ---- lançar e confirmar ----

    [Fact]
    public async Task AdicionarSelecionadaLancaLimpaAEscolhaESugereOResto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Pix));
        vm.ValorPagamentoAdicionar = "10,00";

        await vm.AdicionarSelecionadaCommand.Execute();

        Assert.Single(vm.Pagamentos);
        Assert.Null(vm.FormaSelecionada);                        // escolhe-se a próxima forma
        Assert.All(vm.OpcoesPagamento, o => Assert.False(o.Selecionada));
        Assert.Equal("14,49", vm.ValorPagamentoAdicionar);
    }

    [Fact]
    public async Task ConfirmarComUmaFormaEscolhidaLancaEConcluiNumCliqueSo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Pix));   // valor sugerido = 24,49
        var pode = false;
        using var inscricao = vm.ConfirmarVendaCommand.CanExecute.Subscribe(v => pode = v);
        Assert.True(pode);                                            // escolher uma forma já habilita o Confirmar

        await vm.ConfirmarVendaCommand.Execute();

        Assert.False(vm.EmPagamento);
        Assert.Empty(vm.Itens);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(24.49m, Assert.Single(leitura.PagamentosVenda.ToList()).Valor);
    }

    [Fact]
    public async Task ConfirmarNoDinheiroComOValorRecebidoMaiorFechaComOTotalCerto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Dinheiro));
        vm.ValorPagamentoAdicionar = "50,00";

        await vm.ConfirmarVendaCommand.Execute();

        Assert.False(vm.EmPagamento);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(24.49m, Assert.Single(leitura.PagamentosVenda.ToList()).Valor);   // grava o valor da venda, não o entregue
    }

    [Fact]
    public async Task ConfirmarCartaoSemBandeiraNaoFechaMostraOMotivoEFicaNoPainel()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture, "VISA", "ELO");
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Credito));

        await vm.ConfirmarVendaCommand.Execute();

        Assert.True(vm.EmPagamento);
        Assert.Contains("bandeira", vm.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Vendas);
    }

    [Fact]
    public async Task ConfirmarSemNadaLancadoNemEscolhidoNaoHabilitaEAvisaQuantoFalta()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, _) = await AbrirComCupomDe2449Async(fixture);
        var pode = true;
        using var inscricao = vm.ConfirmarVendaCommand.CanExecute.Subscribe(v => pode = v);
        Assert.False(pode);

        await vm.AvancarCommand.Execute();   // F10 com o painel aberto e nada pago

        Assert.True(vm.EmPagamento);
        Assert.Contains("24,49", vm.Mensagem);
    }

    [Fact]
    public async Task FecharOPainelDesmarcaAFormaEscolhidaEMantemOsPagamentosLancados()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (vm, b) = await AbrirComCupomDe2449Async(fixture);
        vm.ValorPagamentoAdicionar = "10,00";
        vm.AdicionarPagamento(b.Pix);
        await vm.SelecionarFormaCommand.Execute(Opcao(vm, b.Dinheiro));

        await vm.EscCommand.Execute();

        Assert.False(vm.EmPagamento);
        Assert.Single(vm.Pagamentos);   // o que já foi lançado continua
    }
}

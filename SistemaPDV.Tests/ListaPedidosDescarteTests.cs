using System.Reactive.Linq;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Descarte de venda em falha na tela de Pedidos: só supervisor (chave dele) e com motivo.
public class ListaPedidosDescarteTests
{
    private static async Task<int> SemearSupervisorEVendaEmFalhaAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var operador = new Funcionario { Nome = "Carlos" };
        var supervisor = new Funcionario { Nome = "Chefe", Supervisor = true, PdvKeyHash = PdvKeyTeste.Hash("9999") };
        var produto = new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m };
        var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL" };
        context.AddRange(operador, supervisor, produto, forma);
        await context.SaveChangesAsync();

        var caixa = new Caixa
        {
            FuncionarioId = operador.Id,
            DataCaixa = DateOnly.FromDateTime(DateTime.Now),
            Turno = 1,
            DataAbertura = DateTime.Now,
            TrocoInicial = 10m,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();

        var venda = new Venda { CaixaId = caixa.Id, DataHora = DateTime.Now, NumeroPedido = 1, SyncStatus = SyncStatus.FalhaSync, UltimoErroSync = "422 sempre" };
        venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 9.90m });
        venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = forma.Id, Valor = 9.90m });
        context.Vendas.Add(venda);
        await context.SaveChangesAsync();
        return caixa.Id;
    }

    private static ListaPedidosViewModel CriarViewModel(SqliteInMemoryFixture fixture, int caixaId) =>
        new(new VendaLocalService(fixture.CriarContexto), caixaId);

    [Fact]
    public async Task SoUmaVendaEmFalhaSelecionadaPodeSerDescartada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearSupervisorEVendaEmFalhaAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        Assert.False(viewModel.PodeDescartar);   // nada selecionado

        viewModel.VendaSelecionada = viewModel.Vendas.Single();
        Assert.True(viewModel.PodeDescartar);    // em falha

        await using (var context = fixture.CriarContexto())
        {
            (await context.Vendas.SingleAsync()).SyncStatus = SyncStatus.PendenteSync;
            await context.SaveChangesAsync();
        }
        await viewModel.AtualizarCommand.Execute();
        viewModel.VendaSelecionada = viewModel.Vendas.Single();
        Assert.False(viewModel.PodeDescartar);   // pendente comum ainda vai sair: não descarta
    }

    [Fact]
    public async Task DescartarExigeMotivoEChaveDeSupervisor()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearSupervisorEVendaEmFalhaAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        viewModel.VendaSelecionada = viewModel.Vendas.Single();
        var pode = true;
        using var inscricao = viewModel.DescartarCommand.CanExecute.Subscribe(v => pode = v);
        Assert.False(pode);

        viewModel.MotivoDescarte = "API recusa";
        Assert.False(pode);            // falta a chave
        viewModel.ChaveSupervisor = "9999";
        Assert.True(pode);
        viewModel.MotivoDescarte = "   ";
        Assert.False(pode);            // motivo em branco
    }

    [Fact]
    public async Task DescartarComChaveDeSupervisorMarcaDescartadaERecarregaALista()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearSupervisorEVendaEmFalhaAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        viewModel.VendaSelecionada = viewModel.Vendas.Single();
        viewModel.MotivoDescarte = "API recusa o produto";
        viewModel.ChaveSupervisor = "9999";

        await viewModel.DescartarCommand.Execute();

        Assert.Equal(SyncStatus.Descartada, viewModel.Vendas.Single().SyncStatus);
        Assert.Contains("descartada", viewModel.MensagemDescarte, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, viewModel.MotivoDescarte);
        Assert.Equal(string.Empty, viewModel.ChaveSupervisor);   // a chave nunca fica no formulário
        Assert.Null(viewModel.VendaSelecionada);
    }

    [Fact]
    public async Task ChaveErradaMostraOMotivoMantemOTextoELimpaAChave()
    {
        using var fixture = new SqliteInMemoryFixture();
        var caixaId = await SemearSupervisorEVendaEmFalhaAsync(fixture);
        var viewModel = CriarViewModel(fixture, caixaId);
        await viewModel.IniciarAsync();
        viewModel.VendaSelecionada = viewModel.Vendas.Single();
        viewModel.MotivoDescarte = "API recusa o produto";
        viewModel.ChaveSupervisor = "0000";

        await viewModel.DescartarCommand.Execute();

        Assert.Contains("supervisor", viewModel.MensagemDescarte, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("API recusa o produto", viewModel.MotivoDescarte);   // o operador não digita tudo de novo
        Assert.Equal(string.Empty, viewModel.ChaveSupervisor);            // mas a chave errada não fica
        Assert.Equal(SyncStatus.FalhaSync, viewModel.Vendas.Single().SyncStatus);
    }
}

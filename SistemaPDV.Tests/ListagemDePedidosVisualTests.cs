using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Listagem de Pedidos no visual do protótipo: indicadores, busca, filtros de status e de forma, resumo dos itens e os botões
// (Sincronizar fila, Nova venda, Detalhes).
[SupportedOSPlatform("windows")]
public class ListagemDePedidosVisualTests
{
    private sealed record Semente(int CaixaId, int Dinheiro, int Pix, int Cafe, int Arroz);

    private static async Task<Semente> SemearAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var operador = new Funcionario { Nome = "Carlos Silva" };
        var dinheiro = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
        var pix = new FormaPagamento { Nome = "PIX", Tipo = "ESPECIE", CodigoNfce = "17", IdExterno = 28 };
        var cafe = new Produto { Nome = "Café Torrado 500g", PrecoVenda = 16.50m, IdExterno = 1 };
        var arroz = new Produto { Nome = "Arroz 1kg", PrecoVenda = 6.89m, IdExterno = 2 };
        var joao = new Cliente { Nome = "João da Padaria", IdExterno = 10 };
        context.AddRange(operador, dinheiro, pix, cafe, arroz, joao);
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
        return new Semente(caixa.Id, dinheiro.Id, pix.Id, cafe.Id, arroz.Id);
    }

    private static async Task<Guid> AdicionarVendaAsync(
        SqliteInMemoryFixture fixture, Semente s, int numero, SyncStatus status, int forma, params (int Produto, decimal Qtd, decimal Preco)[] itens)
    {
        await using var context = fixture.CriarContexto();
        var venda = new Venda { CaixaId = s.CaixaId, DataHora = new DateTime(2026, 9, 21, 10, 0, 0).AddMinutes(numero), NumeroPedido = numero, SyncStatus = status };
        foreach (var (produto, qtd, preco) in itens)
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto, Quantidade = qtd, PrecoUnitario = preco });
        venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = forma, Valor = itens.Sum(i => i.Qtd * i.Preco) });
        context.Vendas.Add(venda);
        await context.SaveChangesAsync();
        return venda.Id;
    }

    private static async Task<ListaPedidosViewModel> CriarComTresVendasAsync(SqliteInMemoryFixture fixture)
    {
        var s = await SemearAsync(fixture);
        await AdicionarVendaAsync(fixture, s, 1001, SyncStatus.Sincronizado, s.Pix, (s.Cafe, 2, 16.50m));
        await AdicionarVendaAsync(fixture, s, 1002, SyncStatus.PendenteSync, s.Dinheiro, (s.Arroz, 3, 6.89m), (s.Cafe, 1, 16.50m));
        await AdicionarVendaAsync(fixture, s, 1003, SyncStatus.FalhaSync, s.Dinheiro, (s.Arroz, 1, 6.89m));
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), s.CaixaId);
        await viewModel.IniciarAsync();
        return viewModel;
    }

    // ---- indicadores ----

    [Fact]
    public async Task IndicadoresContamTodasAsVendasDoCaixa()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);

        Assert.Equal(3, viewModel.TotalPedidos);
        Assert.Equal(33.00m + 37.17m + 6.89m, viewModel.VolumeFaturado);   // 2×16,50 + (3×6,89 + 16,50) + 6,89
        Assert.Equal(1, viewModel.Sincronizados);
        Assert.Equal(2, viewModel.Pendentes);                              // a pendente e a que falhou ainda não chegaram
        Assert.Equal("1 com falha de envio", viewModel.DetalhePendentes);
        Assert.True(viewModel.TemFalhas);
    }

    [Fact]
    public async Task VendaDescartadaNaoEntraNosIndicadoresMasContinuaNaTabela()
    {
        using var fixture = new SqliteInMemoryFixture();
        var s = await SemearAsync(fixture);
        await AdicionarVendaAsync(fixture, s, 1001, SyncStatus.Sincronizado, s.Pix, (s.Cafe, 1, 16.50m));
        await AdicionarVendaAsync(fixture, s, 1002, SyncStatus.Descartada, s.Pix, (s.Cafe, 5, 16.50m));
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), s.CaixaId);

        await viewModel.IniciarAsync();

        Assert.Equal(1, viewModel.TotalPedidos);
        Assert.Equal(16.50m, viewModel.VolumeFaturado);
        Assert.Equal(0, viewModel.Pendentes);
        Assert.Equal("Aguardando disparo assíncrono", viewModel.DetalhePendentes);
        Assert.Equal(2, viewModel.VendasFiltradas.Count);                  // a trilha de auditoria continua visível
    }

    [Fact]
    public async Task FiltrosNaoMudamOsIndicadores()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);

        viewModel.Busca = "1001";

        Assert.Single(viewModel.VendasFiltradas);
        Assert.Equal(3, viewModel.TotalPedidos);                           // os cartões de cima contam tudo
    }

    // ---- busca e filtros ----

    [Fact]
    public async Task BuscaAchaPorNumeroComOuSemCardinalEPorNomeSemAcentoNemMaiuscula()
    {
        using var fixture = new SqliteInMemoryFixture();
        var s = await SemearAsync(fixture);
        await AdicionarVendaAsync(fixture, s, 1001, SyncStatus.Sincronizado, s.Pix, (s.Cafe, 1, 16.50m));
        await using (var context = fixture.CriarContexto())
        {
            var joao = await context.Clientes.SingleAsync();
            (await context.Vendas.SingleAsync()).ClienteId = joao.Id;
            await context.SaveChangesAsync();
        }
        await AdicionarVendaAsync(fixture, s, 1002, SyncStatus.Sincronizado, s.Pix, (s.Cafe, 1, 16.50m));
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), s.CaixaId);
        await viewModel.IniciarAsync();

        viewModel.Busca = "#1002";
        Assert.Equal(1002, viewModel.VendasFiltradas.Single().Numero);

        viewModel.Busca = "1001";
        Assert.Equal(1001, viewModel.VendasFiltradas.Single().Numero);

        viewModel.Busca = "joao";                                          // sem acento e sem maiúscula
        Assert.Equal("João da Padaria", viewModel.VendasFiltradas.Single().ClienteNome);

        viewModel.Busca = "consumidor";
        Assert.Equal(1002, viewModel.VendasFiltradas.Single().Numero);

        viewModel.Busca = "  ";                                            // só espaços não filtra nada
        Assert.Equal(2, viewModel.VendasFiltradas.Count);
    }

    [Fact]
    public async Task FiltroDeStatusMostraSoOQueFoiEscolhido()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);

        viewModel.StatusSelecionado = ListaPedidosViewModel.OpcoesStatus.Single(o => o.Status == SyncStatus.FalhaSync);

        Assert.Equal(1003, viewModel.VendasFiltradas.Single().Numero);

        viewModel.StatusSelecionado = ListaPedidosViewModel.OpcoesStatus[0];
        Assert.Equal(3, viewModel.VendasFiltradas.Count);
    }

    [Fact]
    public async Task FiltroDeFormaListaSoAsFormasQueApareceramEFiltra()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);

        Assert.Equal(new[] { "Todas Formas", "ESPÉCIE", "PIX" }, viewModel.OpcoesForma);

        viewModel.FormaSelecionada = "PIX";
        Assert.Equal(1001, viewModel.VendasFiltradas.Single().Numero);

        viewModel.FormaSelecionada = "ESPÉCIE";
        Assert.Equal(new[] { 1003, 1002 }, viewModel.VendasFiltradas.Select(v => v.Numero!.Value));   // mais nova primeiro
    }

    [Fact]
    public async Task FiltrosSeCombinamELimparVoltaAoInicio()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);
        viewModel.FormaSelecionada = "ESPÉCIE";
        viewModel.Busca = "1002";
        viewModel.StatusSelecionado = ListaPedidosViewModel.OpcoesStatus.Single(o => o.Status == SyncStatus.FalhaSync);   // 1002 é pendente, não falha

        Assert.True(viewModel.SemPedidos);
        Assert.Equal("Nenhum pedido encontrado com esses filtros.", viewModel.TextoSemPedidos);
        Assert.True(viewModel.PodeLimparFiltros);

        await viewModel.LimparFiltrosCommand.Execute();

        Assert.Equal(3, viewModel.VendasFiltradas.Count);
        Assert.False(viewModel.TemFiltro);
        Assert.False(viewModel.PodeLimparFiltros);
    }

    [Fact]
    public async Task SemNenhumaVendaADicaDizQueNaoHaPedidosEDeixaDeOferecerLimpar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var s = await SemearAsync(fixture);
        var viewModel = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), s.CaixaId);
        await viewModel.IniciarAsync();

        Assert.True(viewModel.SemPedidos);
        Assert.Equal("Nenhum pedido registrado neste caixa ainda.", viewModel.TextoSemPedidos);
        Assert.False(viewModel.PodeLimparFiltros);
    }

    [Fact]
    public async Task RecarregarNaoPerdeOFiltroEscolhidoEDaAFormaQueSumiuVoltaATodas()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);
        viewModel.FormaSelecionada = "PIX";
        viewModel.Busca = "10";

        await viewModel.AtualizarAposSincronizacaoAsync();               // um ciclo de sincronização mexeu no banco

        Assert.Equal("PIX", viewModel.FormaSelecionada);                 // o operador não perde a escolha a cada ciclo
        Assert.Equal("10", viewModel.Busca);

        await using (var context = fixture.CriarContexto())              // a única venda em PIX some das opções
        {
            var pix = await context.Vendas.SingleAsync(v => v.NumeroPedido == 1001);
            pix.SyncStatus = SyncStatus.Descartada;
            context.Remove(pix);
            await context.SaveChangesAsync();
        }
        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.Equal("Todas Formas", viewModel.FormaSelecionada);
    }

    [Fact]
    public async Task ComboBoxMandandoNullNaoApagaAEscolha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);
        viewModel.FormaSelecionada = "PIX";

        viewModel.FormaSelecionada = null!;                              // o controle limpa a seleção ao trocar a lista

        Assert.Equal("PIX", viewModel.FormaSelecionada);
    }

    // ---- resumo dos itens ----

    [Fact]
    public async Task ResumoDosItensDizQuantosSaoEQualEOPrimeiroComSingularEPlural()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);

        var umItem = viewModel.Vendas.Single(v => v.Numero == 1003);
        var doisItens = viewModel.Vendas.Single(v => v.Numero == 1002);

        Assert.Equal("1 item: Arroz 1kg", umItem.ResumoItens);
        Assert.Equal("2 itens: Arroz 1kg", doisItens.ResumoItens);
        Assert.Equal($"3 × Arroz 1kg{Environment.NewLine}1 × Café Torrado 500g", doisItens.ItensDetalhe);   // a dica com a lista inteira
    }

    [Fact]
    public async Task QuantidadeFracionadaApareceComVirgulaNaDicaDosItens()
    {
        using var fixture = new SqliteInMemoryFixture();
        var s = await SemearAsync(fixture);
        await AdicionarVendaAsync(fixture, s, 1001, SyncStatus.Sincronizado, s.Pix, (s.Arroz, 0.333m, 6.89m));
        var resumo = (await new VendaLocalService(fixture.CriarContexto).ListarVendasDoCaixaAsync(s.CaixaId)).Single();

        Assert.Equal("0,333 × Arroz 1kg", resumo.ItensDetalhe);
    }

    // ---- botões ----

    [Fact]
    public async Task DetalhesSelecionaAVendaDaLinha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = await CriarComTresVendasAsync(fixture);
        var falha = viewModel.Vendas.Single(v => v.Numero == 1003);

        await viewModel.DetalhesCommand.Execute(falha);

        Assert.Equal(falha, viewModel.VendaSelecionada);
        Assert.True(viewModel.PodeDescartar);                              // é aqui que uma venda em falha pode ser descartada
    }

    [Fact]
    public async Task NovaVendaEmitidaDaListagemLevaAoPdv()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture);
        var listaAberta = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.ListaPedidos).FirstAsync().ToTask();
        await shell.IrParaListaPedidosCommand.Execute();
        await listaAberta;
        var lista = (ListaPedidosViewModel)shell.CurrentViewModel!;

        var chegouAoPdv = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.Pdv).FirstAsync().ToTask();
        await lista.NovaVendaCommand.Execute();
        await chegouAoPdv;

        Assert.IsType<PdvViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task SincronizarAgoraPedeAoServicoEAvisaOOperador()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture);
        await shell.IrParaListaPedidosCommand.Execute();
        var lista = (ListaPedidosViewModel)shell.CurrentViewModel!;
        var pedido = shell.SincronizacaoSolicitada.FirstAsync().ToTask();   // o Shell repassa o comando de forma assíncrona

        await lista.SincronizarAgoraCommand.Execute();
        await pedido;

        Assert.Contains(shell.Toasts.Ativos, t => t.Texto.Contains("Sincronizando"));   // há o caixa ainda sem subir: fila com 1 item
    }

    [Fact]
    public async Task SincronizarAgoraSemPendenciaOuSemInternetNaoDizQueEstaSincronizando()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture, comCaixaAberto: false);
        shell.SolicitarSincronizacao();
        Assert.Contains(shell.Toasts.Ativos, t => t.Texto == "Nenhuma pendência na fila local.");   // fila vazia

        shell.DefinirConexao(Services.Sync.EstadoConexao.Offline, "sem rede");
        shell.SolicitarSincronizacao();
        Assert.Single(shell.Toasts.Ativos, t => t.Texto.Contains("Sem conexão"));      // o mesmo aviso (chave) foi substituído
        Assert.DoesNotContain(shell.Toasts.Ativos, t => t.Texto.Contains("Sincronizando"));
    }
}

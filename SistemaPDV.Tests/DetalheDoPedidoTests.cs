using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using ReactiveUI;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Modal "Detalhes do pedido": os dados da venda e, principalmente, a REQUISIÇÃO à API — que tem de ser a mesma que o envio faz.
[SupportedOSPlatform("windows")]
public class DetalheDoPedidoTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private sealed record Base(int CaixaId, int ProdutoId, int FormaId);

    private static async Task<Base> SemearBaseAsync(SqliteInMemoryFixture fixture, bool comEmpresa = true)
    {
        await using var context = fixture.CriarContexto();
        var funcionario = new Funcionario { Nome = "Carlos Silva", IdExterno = 2 };
        var produto = new Produto
        {
            Nome = "Refrigerante Cola 2L", Sku = "REF-2L", CodigoBarras = "789200010", PrecoVenda = 9.50m, PrecoCompra = 6.50m,
            UnidadeMedida = "UN", IdExterno = 206, ProdutoIdApi = 77,
        };
        var forma = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
        context.AddRange(funcionario, produto, forma);
        if (comEmpresa)
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi, ClienteConsumidorFinalIdExterno = 1, CodigoPdv = "01" });
        await context.SaveChangesAsync();

        var caixa = new Models.Caixa
        {
            FuncionarioId = funcionario.Id, IdExterno = 28, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1,
            DataAbertura = new DateTime(2026, 9, 20, 8, 0, 0), TrocoInicial = 10m, AberturaSincronizada = true,
        };
        context.Caixas.Add(caixa);
        await context.SaveChangesAsync();
        return new Base(caixa.Id, produto.Id, forma.Id);
    }

    private static async Task<Venda> RegistrarAsync(SqliteInMemoryFixture fixture, Base b, decimal desconto = 0m)
    {
        var venda = await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(
            b.CaixaId, null, new[] { (b.ProdutoId, 2m, 9.50m, 0m, 0m) }, new[] { (b.FormaId, 19m - desconto) });
        if (desconto > 0)
        {
            await using var context = fixture.CriarContexto();
            (await context.Vendas.SingleAsync(v => v.Id == venda.Id)).Desconto = desconto;
            await context.SaveChangesAsync();
        }
        return venda;
    }

    // ---- os dados ----

    [Fact]
    public async Task DetalheTrazItensPagamentosETotalLiquido()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b, desconto: 1m);

        var detalhe = (await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id))!;

        var item = Assert.Single(detalhe.Itens);
        Assert.Equal("Refrigerante Cola 2L", item.Descricao);
        Assert.Equal("2 un", item.QuantidadeTexto);
        Assert.Equal(9.50m, item.PrecoUnitario);
        Assert.Equal(19.00m, item.Total);
        Assert.NotEqual("—", item.Codigo);                                   // o código que a tela mostra para o produto

        var pagamento = Assert.Single(detalhe.Pagamentos);
        Assert.Equal("ESPÉCIE", pagamento.Forma);
        Assert.Equal(18.00m, pagamento.Valor);

        Assert.Equal(1m, detalhe.Desconto);
        Assert.Equal(18.00m, detalhe.Resumo.Total);                          // total líquido = itens − desconto
        Assert.Equal("Carlos Silva", detalhe.Resumo.OperadorNome);
    }

    [Fact]
    public async Task QuantidadeFracionadaEUnidadeApareceNoTextoDoItem()
    {
        var item = new ItemDetalhe("789", "Banana", 0.333m, "KG", 5.99m, 1.99m);
        Assert.Equal("0,333 kg", item.QuantidadeTexto);
        Assert.Equal("4 un", new ItemDetalhe("1", "X", 4m, null, 1m, 4m).QuantidadeTexto);   // sem unidade cadastrada: "un"
        await Task.CompletedTask;
    }

    [Fact]
    public async Task VendaInexistenteDevolveNull()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearBaseAsync(fixture);

        Assert.Null(await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(Guid.NewGuid()));
    }

    // ---- a requisição ----

    [Fact]
    public async Task RequisicaoMostraMetodoUrlCabecalhosComTokenMascaradoECorpoEmJson()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);

        var detalhe = (await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id))!;

        Assert.Null(detalhe.MotivoSemRequisicao);
        var texto = detalhe.Requisicao!;
        Assert.StartsWith("POST https://exemplo.softcomshop.com.br/", texto);
        Assert.Contains("/vendas", texto.Split(Environment.NewLine)[0]);
        Assert.Contains("Api-Version: v2", texto);
        Assert.Contains("Authorization: Bearer ••••••••", texto);           // o token de verdade NUNCA aparece
        Assert.Contains($"\"guid\": \"{venda.Id}\"", texto);
        Assert.Contains("\"numero_documento\": \"01-000001\"", texto);
        Assert.Contains("\"api_nome_pagamento\": \"ESPÉCIE\"", texto);      // acento como acento, não É
        Assert.Contains("\"produto_empresa_grade_id\": 206", texto);
    }

    [Fact]
    public async Task ACorpoMostradoEIgualAoQueOEnvioRealMandaAApi()
    {
        // O ponto do modal: o que ele mostra não pode divergir do que a API recebe. Envia de verdade (com um servidor de
        // mentira) e compara, campo a campo, o corpo capturado com o JSON exibido.
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);
        string? corpoEnviado = null;
        var http = FakeHttpMessageHandler.CriarHttpClient(requisicao =>
        {
            corpoEnviado = requisicao.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": { "id": 999 } }""", Encoding.UTF8, "application/json") };
        });
        var envio = new VendaSyncService(fixture.CriarContexto, new SoftcomApiClient(http));
        var antes = (await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id))!;

        var resultado = await envio.SincronizarVendaAsync(venda.Id, "token-secreto-de-verdade");

        Assert.True(resultado.Sucesso);
        var requisicao = antes.Requisicao!;
        var jsonExibido = requisicao[(requisicao.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal) + 4)..];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(corpoEnviado!)!, JsonNode.Parse(jsonExibido)!));
        Assert.DoesNotContain("token-secreto-de-verdade", requisicao);
    }

    [Fact]
    public async Task VendaSincronizadaMostraOIdQueAApiDeu()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);
        await using (var context = fixture.CriarContexto())
        {
            var v = await context.Vendas.SingleAsync();
            v.SyncStatus = SyncStatus.Sincronizado;
            v.VendaIdExterno = 4521;
            await context.SaveChangesAsync();
        }

        var detalhe = (await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id))!;

        Assert.Equal(4521, detalhe.VendaIdExterno);
    }

    [Fact]
    public async Task SemAEmpresaSincronizadaExplicaOMotivoEmVezDeMostrarJsonIncompleto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture, comEmpresa: false);
        var venda = await RegistrarAsync(fixture, b);

        var detalhe = (await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id))!;

        Assert.Null(detalhe.Requisicao);
        Assert.Contains("Ainda não dá para montar a requisição", detalhe.MotivoSemRequisicao);
        Assert.Contains("Empresa", detalhe.MotivoSemRequisicao);
    }

    [Fact]
    public async Task UrlDaApiVaziaOuInvalidaNaoDerrubaOModalExplicaOMotivo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);
        await using (var context = fixture.CriarContexto())
        {
            (await context.ConfiguracoesSincronizacao.SingleAsync()).UrlApi = string.Empty;   // dispositivo sem vínculo válido
            await context.SaveChangesAsync();
        }

        var detalhe = (await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id))!;

        Assert.Null(detalhe.Requisicao);
        Assert.Contains("Não foi possível montar a requisição", detalhe.MotivoSemRequisicao);
        Assert.Single(detalhe.Itens);                                          // o resto do detalhe continua aparecendo
    }

    [Fact]
    public async Task VendaDescartadaNaoMostraRequisicaoPoisNaoEEnviada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var b = await SemearBaseAsync(fixture);
        var venda = await RegistrarAsync(fixture, b);
        await using (var context = fixture.CriarContexto())
        {
            (await context.Vendas.SingleAsync()).SyncStatus = SyncStatus.Descartada;
            await context.SaveChangesAsync();
        }

        var detalhe = (await new VendaLocalService(fixture.CriarContexto).ObterDetalheAsync(venda.Id))!;

        Assert.Null(detalhe.Requisicao);
        Assert.Contains("descartada", detalhe.MotivoSemRequisicao);
    }

    // ---- o modal (ViewModel) ----

    private static async Task<(ListaPedidosViewModel Lista, Base B)> AbrirListaAsync(SqliteInMemoryFixture fixture)
    {
        var b = await SemearBaseAsync(fixture);
        await RegistrarAsync(fixture, b);
        var lista = new ListaPedidosViewModel(new VendaLocalService(fixture.CriarContexto), b.CaixaId);
        await lista.IniciarAsync();
        return (lista, b);
    }

    [Fact]
    public async Task DetalhesAbreOModalEFecharOFecha()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (lista, _) = await AbrirListaAsync(fixture);
        Assert.False(lista.DetalheAberto);
        Assert.False(await lista.FecharDetalheCommand.CanExecute.FirstAsync());   // Esc com o modal fechado não faz nada

        await lista.DetalhesCommand.Execute(lista.Vendas.Single());

        Assert.True(lista.DetalheAberto);
        Assert.Equal("Detalhes do Pedido #1", lista.DetalheTitulo);
        Assert.Equal("Carlos Silva (Caixa " + lista.RotuloCaixa[^2..] + ")", lista.DetalheOperador);
        Assert.Equal(lista.Vendas.Single(), lista.VendaSelecionada);              // continua selecionando a linha (é onde mora o descarte)
        Assert.True(await lista.FecharDetalheCommand.CanExecute.FirstAsync());

        await lista.FecharDetalheCommand.Execute();

        Assert.False(lista.DetalheAberto);
        Assert.Null(lista.Detalhe);
    }

    [Fact]
    public async Task ModalAbertoAcompanhaASincronizacao()
    {
        using var fixture = new SqliteInMemoryFixture();
        var (lista, _) = await AbrirListaAsync(fixture);
        await lista.DetalhesCommand.Execute(lista.Vendas.Single());
        Assert.Contains("Ainda não enviada", lista.DetalheNotaRequisicao);

        await using (var context = fixture.CriarContexto())
        {
            var v = await context.Vendas.SingleAsync();
            v.SyncStatus = SyncStatus.Sincronizado;                             // um ciclo enviou a venda com o modal aberto
            v.VendaIdExterno = 12;
            await context.SaveChangesAsync();
        }
        await lista.AtualizarAposSincronizacaoAsync();

        Assert.Equal(SyncStatus.Sincronizado, lista.Detalhe!.Resumo.SyncStatus);
        Assert.Contains("aceita pela API", lista.DetalheNotaRequisicao);
        Assert.Equal("Id na API: 12", lista.DetalheIdNaApi);
    }

    [Fact]
    public async Task DetalhesDoPainelPrincipalChegaComOModalAberto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            var produto = new Produto { Nome = "Refrigerante Cola 2L", PrecoVenda = 9.50m, IdExterno = 206, ProdutoIdApi = 77 };
            var forma = new FormaPagamento { Nome = "ESPÉCIE", Tipo = "ESPECIE", CodigoNfce = "01", IdExterno = 5 };
            context.AddRange(produto, forma);
            await context.SaveChangesAsync();
            var venda = new Venda { CaixaId = await context.Caixas.Select(c => c.Id).FirstAsync(), DataHora = DateTime.Now, NumeroPedido = 1 };
            venda.Itens.Add(new ItemVenda { VendaId = venda.Id, ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 9.50m });
            venda.Pagamentos.Add(new PagamentoVenda { VendaId = venda.Id, FormaPagamentoId = forma.Id, Valor = 9.50m });
            context.Vendas.Add(venda);
            await context.SaveChangesAsync();
        }
        await shell.IrParaDashboardCommand.Execute();
        var painel = (DashboardViewModel)shell.CurrentViewModel!;
        var escolhida = painel.UltimasVendas.Single();

        var chegou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t == Tela.ListaPedidos).FirstAsync().ToTask();
        await painel.DetalhesCommand.Execute(escolhida);
        await chegou;

        var lista = Assert.IsType<ListaPedidosViewModel>(shell.CurrentViewModel);
        Assert.True(lista.DetalheAberto);
        Assert.Equal(escolhida.Id, lista.Detalhe!.Resumo.Id);
    }
}

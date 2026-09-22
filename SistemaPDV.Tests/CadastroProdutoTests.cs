using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Cadastro de produto pelo modal (nome, código, categoria, preço, custo): criação local + o modal. Sem estoque — a API não
// grava estoque no cadastro. O envio à API está em CatalogSyncServiceProdutoNovoTests.
public class CadastroProdutoTests
{
    private static async Task<int> SemearGrupoAsync(SqliteInMemoryFixture fixture, string nome = "Mercearia", int idExterno = 7)
    {
        await using var context = fixture.CriarContexto();
        context.Grupos.Add(new Grupo { Nome = nome, IdExterno = idExterno });
        await context.SaveChangesAsync();
        return idExterno;
    }

    private static NovoProdutoDados Dados(int? grupo = 7, string? nome = "Café Torrado 500g", string? codigo = "7890001", string? preco = "12,50", string? custo = null) =>
        new(nome, codigo, grupo, preco, custo);

    // ---- o serviço ----

    [Fact]
    public async Task CriaProdutoLocalPendenteSemIdExternoComOsDadosDoModal()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarProdutoAsync(Dados(nome: "  Café Torrado 500g  "));

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Equal(resultado.ProdutoId, produto.Id);
        Assert.Equal("Café Torrado 500g", produto.Nome);
        Assert.Equal(7, produto.GrupoId);
        Assert.Equal(12.50m, produto.PrecoVenda);
        Assert.Equal(0, produto.EstoqueAtual);      // sem campo de estoque no cadastro: a API não grava estoque aqui
        Assert.Equal(SyncStatus.PendenteSync, produto.SyncStatus);
        Assert.Null(produto.IdExterno);
        Assert.Null(produto.ProdutoIdApi);
    }

    [Theory]
    [InlineData("7891234567890", "7891234567890", null)]    // 13 dígitos: código de barras
    [InlineData("78912345", "78912345", null)]              // 8 dígitos (EAN-8): código de barras
    [InlineData("7890001", null, "7890001")]                // 7 dígitos: curto demais para EAN, vira referência (o do protótipo)
    [InlineData("CAFE-500", null, "CAFE-500")]              // com letras: referência
    [InlineData("  ", null, null)]                          // vazio: sem código
    public async Task CodigoViraCodigoDeBarrasOuReferencia(string digitado, string? barras, string? referencia)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarProdutoAsync(Dados(codigo: digitado));

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Equal(barras, produto.CodigoBarras);
        Assert.Equal(referencia, produto.Referencia);
    }

    [Fact]
    public async Task CodigoSemBarrasMostraNaTelaOQueFoiDigitado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);
        await service.CriarProdutoAsync(Dados(codigo: "7890001"));

        var linha = Assert.Single(await service.ListarProdutosAsync(""));

        Assert.Equal("7890001", linha.Codigo);       // a tabela e o card não podem mostrar "—" para um código digitado
    }

    [Theory]
    [InlineData("12,50", 12.50)]
    [InlineData("12.50", 12.50)]                    // o placeholder do protótipo usa ponto
    [InlineData("1.234,50", 1234.50)]
    public async Task PrecoAceitaVirgulaOuPonto(string digitado, double esperado)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);

        await service.CriarProdutoAsync(Dados(preco: digitado));

        using var leitura = fixture.CriarContexto();
        Assert.Equal((decimal)esperado, leitura.Produtos.Single().PrecoVenda);
    }

    [Theory]
    [InlineData("8,00", 8.00)]
    [InlineData("8.5", 8.5)]
    public async Task PrecoDeCustoInformadoFicaNoProduto(string digitado, double esperado)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);

        var resultado = await new CadastroLocalService(fixture.CriarContexto).CriarProdutoAsync(Dados(custo: digitado));

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal((decimal)esperado, leitura.Produtos.Single().PrecoCompra);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PrecoDeCustoEhOpcionalEFicaNuloQuandoNaoInformado(string? digitado)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);

        var resultado = await new CadastroLocalService(fixture.CriarContexto).CriarProdutoAsync(Dados(custo: digitado));

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.Produtos.Single().PrecoCompra);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1.234")]
    [InlineData("1000000,01")]
    public async Task PrecoDeCustoInvalidoNaoGravaNada(string digitado)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);

        var resultado = await new CadastroLocalService(fixture.CriarContexto).CriarProdutoAsync(Dados(custo: digitado));

        Assert.False(resultado.Sucesso);
        Assert.Contains("custo", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Produtos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1.234")]                           // ambíguo (milhar?): recusado em vez de adivinhado
    [InlineData("1000000,01")]
    public async Task PrecoInvalidoOuZeroNaoGravaNada(string? preco)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarProdutoAsync(Dados(preco: preco));

        Assert.False(resultado.Sucesso);
        Assert.Contains("preço", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Produtos);
    }

    [Fact]
    public async Task NomeVazioOuLongoDemaisNaoGrava()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);

        Assert.False((await service.CriarProdutoAsync(Dados(nome: "  "))).Sucesso);
        Assert.False((await service.CriarProdutoAsync(Dados(nome: new string('a', 151)))).Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Produtos);
    }

    [Fact]
    public async Task CodigoLongoDemaisNaoGrava()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarProdutoAsync(Dados(codigo: "ABCDEFGHIJKLMNOPQRSTU"));   // 21 caracteres

        Assert.False(resultado.Sucesso);
        Assert.Contains("código", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CategoriaEhObrigatoriaEPrecisaExistir()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);

        var semCategoria = await service.CriarProdutoAsync(Dados(grupo: null));
        var inexistente = await service.CriarProdutoAsync(Dados(grupo: 999));

        Assert.False(semCategoria.Sucesso);
        Assert.Contains("categoria", semCategoria.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.False(inexistente.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Produtos);
    }

    [Fact]
    public async Task NomeOuCodigoRepetidoNaoDuplica()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);
        await service.CriarProdutoAsync(Dados(nome: "Café", codigo: "7891234567890"));

        var mesmoNome = await service.CriarProdutoAsync(Dados(nome: "CAFÉ".ToLowerInvariant(), codigo: "7899999999999"));
        var mesmoNomeMaiusculo = await service.CriarProdutoAsync(Dados(nome: "café", codigo: "7899999999998"));
        var mesmoBarras = await service.CriarProdutoAsync(Dados(nome: "Outro", codigo: "7891234567890"));

        Assert.False(mesmoNome.Sucesso);
        Assert.False(mesmoNomeMaiusculo.Sucesso);
        Assert.Contains("nome", mesmoNome.Mensagem);
        Assert.False(mesmoBarras.Sucesso);
        Assert.Contains("código de barras", mesmoBarras.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Single(leitura.Produtos);
    }

    [Fact]
    public async Task ReferenciaRepetidaNaoDuplica()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var service = new CadastroLocalService(fixture.CriarContexto);
        await service.CriarProdutoAsync(Dados(nome: "Café", codigo: "REF-1"));

        var resultado = await service.CriarProdutoAsync(Dados(nome: "Outro", codigo: "REF-1"));

        Assert.False(resultado.Sucesso);
    }

    [Fact]
    public async Task CategoriasSaoOsGruposComParNaApiOrdenadosPorNome()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Grupos.Add(new Grupo { Nome = "Mercearia", IdExterno = 7 });
            context.Grupos.Add(new Grupo { Nome = "bebidas", IdExterno = 3 });
            context.Grupos.Add(new Grupo { Nome = "Sem par na API", IdExterno = null });
            await context.SaveChangesAsync();
        }
        var service = new CadastroLocalService(fixture.CriarContexto);

        var categorias = await service.ListarCategoriasAsync();

        Assert.Equal(new[] { "bebidas", "Mercearia" }, categorias.Select(c => c.Nome));
        Assert.Equal(new[] { 3, 7 }, categorias.Select(c => c.GrupoId));
    }

    [Fact]
    public async Task ReenviarFalhasTambemDevolveOsProdutosQueFalharam()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Produtos.Add(new Produto { Nome = "Falhou", SyncStatus = SyncStatus.FalhaSync, TentativasEnvio = 8, ProximaTentativaEm = DateTime.UtcNow.AddHours(1) });
            context.Produtos.Add(new Produto { Nome = "Da API", SyncStatus = SyncStatus.FalhaSync, IdExterno = 5, TentativasEnvio = 3 });   // não é criado aqui
            await context.SaveChangesAsync();
        }
        var service = new CadastroLocalService(fixture.CriarContexto);

        var reenviados = await service.ReenviarFalhasAsync();

        Assert.Equal(1, reenviados);
        using var leitura = fixture.CriarContexto();
        var falhou = leitura.Produtos.Single(p => p.Nome == "Falhou");
        Assert.Equal(0, falhou.TentativasEnvio);
        Assert.Null(falhou.ProximaTentativaEm);
        Assert.Equal(3, leitura.Produtos.Single(p => p.Nome == "Da API").TentativasEnvio);
    }

    [Fact]
    public async Task ProdutoPendenteEntraNoContadorDaFilaEProdutoDaApiNao()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Produtos.Add(new Produto { Nome = "Novo aqui", SyncStatus = SyncStatus.PendenteSync });
            context.Produtos.Add(new Produto { Nome = "Da API", SyncStatus = SyncStatus.Sincronizado, IdExterno = 5 });
            await context.SaveChangesAsync();
        }

        Assert.Equal(1, await new DashboardService(fixture.CriarContexto).ContarPendentesAsync());
    }

    // ---- o modal (ProdutoFormViewModel, dentro de Cadastros) ----

    private static CadastrosViewModel CriarViewModel(SqliteInMemoryFixture fixture) => new(new CadastroLocalService(fixture.CriarContexto));

    private static bool PodeSalvar(CadastrosViewModel viewModel)
    {
        var pode = false;
        viewModel.FormProduto.SalvarCommand.CanExecute.Subscribe(v => pode = v);
        return pode;
    }

    [Fact]
    public async Task NovoProdutoAbreOModalLimpoComAsCategoriasENinguemEscolhido()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        Assert.False(viewModel.ModalAberto);

        await viewModel.NovoProdutoCommand.Execute();

        Assert.True(viewModel.FormProduto.Aberto);
        Assert.True(viewModel.ModalAberto);                          // a tela de trás fica desabilitada
        Assert.True(viewModel.ProdutosAtiva);
        Assert.Equal(new[] { "Mercearia" }, viewModel.FormProduto.Categorias.Select(c => c.Nome));
        Assert.Null(viewModel.FormProduto.CategoriaSelecionada);     // nada de categoria "adivinhada"
        Assert.False(viewModel.FormProduto.SemCategorias);
    }

    [Fact]
    public async Task SemGruposSincronizadosOModalAvisaEOBotaoFicaDesabilitado()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);

        await viewModel.NovoProdutoCommand.Execute();
        viewModel.FormProduto.Nome = "Café";
        viewModel.FormProduto.Preco = "10";

        Assert.True(viewModel.FormProduto.SemCategorias);
        Assert.False(PodeSalvar(viewModel));                         // sem categoria não há o que escolher
    }

    [Fact]
    public async Task SalvarPrecisaDeNomeCategoriaEPreco()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.NovoProdutoCommand.Execute();
        var form = viewModel.FormProduto;
        Assert.False(PodeSalvar(viewModel));

        form.Nome = "Café";
        Assert.False(PodeSalvar(viewModel));
        form.CategoriaSelecionada = form.Categorias[0];
        Assert.False(PodeSalvar(viewModel));                         // falta o preço
        form.Preco = "12,50";
        Assert.True(PodeSalvar(viewModel));
        form.Nome = "   ";
        Assert.False(PodeSalvar(viewModel));
    }

    [Fact]
    public async Task OCustoNaoEObrigatorioParaSalvarEVaiJuntoQuandoInformado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.NovoProdutoCommand.Execute();
        var form = viewModel.FormProduto;
        form.Nome = "Café";
        form.CategoriaSelecionada = form.Categorias[0];
        form.Preco = "12,50";
        Assert.True(PodeSalvar(viewModel));                          // sem custo, salva do mesmo jeito

        form.PrecoCusto = "8,00";
        await form.SalvarCommand.Execute();

        using var leitura = fixture.CriarContexto();
        Assert.Equal(8.00m, leitura.Produtos.Single().PrecoCompra);
    }

    [Fact]
    public async Task ReabrirOModalLimpaOCusto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.NovoProdutoCommand.Execute();
        viewModel.FormProduto.PrecoCusto = "8,00";

        viewModel.FormProduto.CancelarCommand.Execute().Subscribe();
        await viewModel.NovoProdutoCommand.Execute();

        Assert.Equal(string.Empty, viewModel.FormProduto.PrecoCusto);
    }

    [Fact]
    public async Task SalvarGravaFechaOModalAvisaERecarregaAListaComOProdutoPendente()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        viewModel.Busca = "zzz";                                     // uma busca ativa esconderia o novo produto
        await viewModel.IniciarAsync();
        await viewModel.NovoProdutoCommand.Execute();
        var form = viewModel.FormProduto;
        form.Nome = "Café Torrado 500g";
        form.Codigo = "7890001";
        form.CategoriaSelecionada = form.Categorias[0];
        form.Preco = "12,50";

        await form.SalvarCommand.Execute();

        Assert.False(form.Aberto);
        Assert.False(viewModel.ModalAberto);
        Assert.NotNull(viewModel.MensagemProdutoSalvo);
        Assert.Equal(string.Empty, viewModel.Busca);
        var linha = Assert.Single(viewModel.Produtos);
        Assert.Equal("Café Torrado 500g", linha.Nome);
        Assert.Equal("Mercearia", linha.Categoria);
        Assert.Equal(SyncStatus.PendenteSync, linha.SyncStatus);
        Assert.Equal(12.50m, linha.PrecoVenda);
        Assert.Equal(1, viewModel.Contagem.Produtos);
    }

    [Fact]
    public async Task ErroDeValidacaoMantemOModalAbertoComOQueFoiDigitado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.NovoProdutoCommand.Execute();
        var form = viewModel.FormProduto;
        form.Nome = "Café";
        form.CategoriaSelecionada = form.Categorias[0];
        form.Preco = "0";                                            // habilita o botão, mas o serviço recusa

        await form.SalvarCommand.Execute();

        Assert.True(form.Aberto);
        Assert.Contains("preço", form.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Café", form.Nome);
        Assert.Equal("0", form.Preco);
        Assert.Null(viewModel.MensagemProdutoSalvo);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Produtos);
    }

    [Fact]
    public async Task CancelarDescartaTudoEAProximaAberturaVemLimpa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGrupoAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.NovoProdutoCommand.Execute();
        var form = viewModel.FormProduto;
        form.Nome = "Café";
        form.Preco = "10";
        form.CategoriaSelecionada = form.Categorias[0];

        await form.CancelarCommand.Execute();

        Assert.False(form.Aberto);
        Assert.False(viewModel.ModalAberto);
        await viewModel.NovoProdutoCommand.Execute();
        Assert.Equal(string.Empty, form.Nome);
        Assert.Equal(string.Empty, form.Preco);
        Assert.Null(form.CategoriaSelecionada);
        Assert.Null(form.Mensagem);
    }

    [Fact]
    public async Task ModalDeClienteETambemContaComoModalAberto()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);

        await viewModel.NovoClienteCommand.Execute();
        Assert.True(viewModel.ModalAberto);

        await viewModel.FecharFormularioClienteCommand.Execute();
        Assert.False(viewModel.ModalAberto);
    }
}

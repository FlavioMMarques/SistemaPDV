using System.Reactive.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Cadastros no visual do protótipo: três abas (Produtos, Clientes, Operadores), categoria vinda dos grupos sincronizados,
// telefone e cidade/UF formatados, operadores com perfil/caixa/situação e as contagens das abas.
public class CadastrosVisualTests
{
    private static async Task SemearGruposEProdutosAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.Grupos.AddRange(new Grupo { IdExterno = 1, Nome = "Mercearia" }, new Grupo { IdExterno = 2, Nome = "Bebidas" });
        context.Produtos.AddRange(
            new Produto { Nome = "Arroz 1kg", CodigoBarras = "789100010", PrecoVenda = 6.89m, EstoqueAtual = 85, UnidadeMedida = "UN", GrupoId = 1, IdExterno = 10 },
            new Produto { Nome = "Refrigerante 2L", Sku = "REF-2L", PrecoVenda = 9.50m, EstoqueAtual = 110, UnidadeMedida = "UN", GrupoId = 2, IdExterno = 11 },
            new Produto { Nome = "Produto sem grupo", PrecoVenda = 1m, EstoqueAtual = 1, UnidadeMedida = "KG", IdExterno = 12 },
            new Produto { Nome = "Produto de grupo que sumiu", PrecoVenda = 1m, EstoqueAtual = 1, GrupoId = 99, IdExterno = 13 });
        await context.SaveChangesAsync();
    }

    // ---- produtos: categoria e código ----

    [Fact]
    public async Task ProdutoTrazCodigoCategoriaEUnidade()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGruposEProdutosAsync(fixture);

        var produtos = await new CadastroLocalService(fixture.CriarContexto).ListarProdutosAsync(null);

        var arroz = produtos.Single(p => p.Nome == "Arroz 1kg");
        Assert.Equal("789100010", arroz.Codigo);                    // código de barras
        Assert.Equal("Mercearia", arroz.Categoria);
        Assert.Equal("UN", arroz.Unidade);
        Assert.Equal("REF-2L", produtos.Single(p => p.Nome == "Refrigerante 2L").Codigo);   // sem código de barras: o SKU
        Assert.Equal("12", produtos.Single(p => p.Nome == "Produto sem grupo").Codigo);      // sem nenhum dos dois: o id da API
    }

    [Fact]
    public async Task ProdutoSemGrupoOuComGrupoQueSumiuFicaSemCategoriaSemQuebrar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGruposEProdutosAsync(fixture);

        var produtos = await new CadastroLocalService(fixture.CriarContexto).ListarProdutosAsync(null);

        Assert.Null(produtos.Single(p => p.Nome == "Produto sem grupo").Categoria);
        Assert.Null(produtos.Single(p => p.Nome == "Produto de grupo que sumiu").Categoria);   // GrupoId 99 não existe mais
    }

    [Fact]
    public async Task BuscaAchaPorNomeDeCategoria()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGruposEProdutosAsync(fixture);
        var servico = new CadastroLocalService(fixture.CriarContexto);

        Assert.Equal("Arroz 1kg", Assert.Single(await servico.ListarProdutosAsync("mercearia")).Nome);
        Assert.Equal("Refrigerante 2L", Assert.Single(await servico.ListarProdutosAsync("BEBIDAS")).Nome);
        Assert.Equal("Arroz 1kg", Assert.Single(await servico.ListarProdutosAsync("arroz")).Nome);   // e continua achando pelo nome
        Assert.Empty(await servico.ListarProdutosAsync("categoria-que-nao-existe"));
    }

    [Fact]
    public async Task CatalogoDoPdvPreencheACategoriaEOCardCaiNaUnidadeQuandoNaoHa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGruposEProdutosAsync(fixture);

        var produtos = await new CatalogoLocalService(fixture.CriarContexto).ListarProdutosDisponiveisAsync();

        Assert.Equal("Mercearia", produtos.Single(p => p.Nome == "Arroz 1kg").CategoriaOuUnidade);
        Assert.Equal("KG", produtos.Single(p => p.Nome == "Produto sem grupo").CategoriaOuUnidade);        // como o card era antes
        Assert.Equal("UN", produtos.Single(p => p.Nome == "Produto de grupo que sumiu").CategoriaOuUnidade);   // sem unidade cadastrada: "UN"
    }

    // ---- clientes: telefone e cidade/UF ----

    [Theory]
    [InlineData("83", "999990000", "(83) 99999-0000")]     // celular com 9 dígitos, DDD à parte
    [InlineData("83", "32214589", "(83) 3221-4589")]       // fixo com 8 dígitos, DDD à parte
    [InlineData(null, "32214589", "3221-4589")]            // sem DDD: só o número
    [InlineData("83", "83999990000", "(83) 99999-0000")]   // o DDD já veio dentro do número (11 dígitos): não duplica
    [InlineData("83", "(83) 3221-4589", "(83) 3221-4589")] // já formatado (10 dígitos): fica igual, sem "(83) (83)"
    [InlineData(null, "8332214589", "(83) 3221-4589")]     // 10 dígitos sem DDD à parte
    [InlineData("83", "12345", "12345")]                   // fora do padrão: aparece como veio, sem inventar máscara
    [InlineData("83", null, null)]                         // sem número: nada
    [InlineData("83", "  ", null)]
    public void TelefoneEFormatadoSoQuandoHaPadraoClaro(string? ddd, string? telefone, string? esperado) =>
        Assert.Equal(esperado, CadastroLocalService.FormatarTelefone(ddd, telefone));

    [Theory]
    [InlineData("João Pessoa", "PB", "João Pessoa - PB")]
    [InlineData("João Pessoa", null, "João Pessoa")]
    [InlineData(null, "PB", "PB")]
    [InlineData(" ", " ", null)]
    [InlineData(null, null, null)]
    public void CidadeEUfSoAparecemOQueHouver(string? cidade, string? uf, string? esperado) =>
        Assert.Equal(esperado, CadastroLocalService.CidadeUf(cidade, uf));

    [Fact]
    public async Task ClienteTrazIdDaApiTelefoneECidadeEClienteNovoAindaNaoTemId()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.AddRange(
                new Cliente { Nome = "Padaria", IdExterno = 2, ContatoDdd = "83", ContatoTelefone = "32214589", Cidade = "João Pessoa", Uf = "PB", SyncStatus = SyncStatus.Sincronizado },
                new Cliente { Nome = "Maria (nova)", SyncStatus = SyncStatus.PendenteSync });
            await context.SaveChangesAsync();
        }

        var clientes = await new CadastroLocalService(fixture.CriarContexto).ListarClientesAsync(null);

        var padaria = clientes.Single(c => c.Nome == "Padaria");
        Assert.Equal(2, padaria.IdExterno);
        Assert.Equal("(83) 3221-4589", padaria.Telefone);
        Assert.Equal("João Pessoa - PB", padaria.CidadeUf);
        var nova = clientes.Single(c => c.Nome == "Maria (nova)");
        Assert.Null(nova.IdExterno);                                           // a tela mostra "novo"
        Assert.Null(nova.Telefone);
        Assert.Null(nova.CidadeUf);
    }

    // ---- operadores ----

    private static async Task SemearOperadoresAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        var carlos = new Funcionario { Nome = "Carlos Silva", IdExterno = 2, PdvKeyHash = "h" };
        var mariana = new Funcionario { Nome = "Mariana Souza", IdExterno = 3, PdvKeyHash = "h" };
        var gerente = new Funcionario { Nome = "Gerente Geral", IdExterno = 99, Supervisor = true, PdvKeyHash = "h" };
        var antigo = new Funcionario { Nome = "Antigo", IdExterno = 7, Desativado = true, PdvKeyHash = "h" };
        var semChave = new Funcionario { Nome = "Sem Chave", IdExterno = 8 };
        context.Funcionarios.AddRange(carlos, mariana, gerente, antigo, semChave);
        await context.SaveChangesAsync();
        context.Caixas.AddRange(
            new Models.Caixa { FuncionarioId = carlos.Id, DataCaixa = new DateOnly(2026, 9, 21), Turno = 1, DataAbertura = DateTime.Now, TrocoInicial = 0m, Status = StatusCaixa.Aberto },
            new Models.Caixa { FuncionarioId = mariana.Id, DataCaixa = new DateOnly(2026, 9, 20), Turno = 1, DataAbertura = DateTime.Now, TrocoInicial = 0m, Status = StatusCaixa.Fechado });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task OperadoresTrazemPerfilCodigoCaixaAbertoESituacao()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearOperadoresAsync(fixture);

        var operadores = await new CadastroLocalService(fixture.CriarContexto).ListarOperadoresAsync(null);

        var carlos = operadores.Single(o => o.Nome == "Carlos Silva");
        Assert.Equal("OP-02", carlos.Codigo);                                 // o id da API, com dois dígitos
        Assert.Equal("Operador de Caixa", carlos.Perfil);
        Assert.Equal("Caixa 01", carlos.CaixaAberto);
        Assert.Equal(SituacaoOperador.Ativo, carlos.Situacao);
        Assert.Equal("Ativo", carlos.RotuloSituacao);

        Assert.Null(operadores.Single(o => o.Nome == "Mariana Souza").CaixaAberto);   // o caixa dela está FECHADO: não conta como aberto
        var gerente = operadores.Single(o => o.Nome == "Gerente Geral");
        Assert.Equal("Supervisor / Gerente", gerente.Perfil);
        Assert.True(gerente.Supervisor);
        Assert.Equal("OP-99", gerente.Codigo);
        Assert.Equal(SituacaoOperador.Desativado, operadores.Single(o => o.Nome == "Antigo").Situacao);
        Assert.Equal(SituacaoOperador.SemChaveDoPdv, operadores.Single(o => o.Nome == "Sem Chave").Situacao);
        Assert.Equal("Sem chave do PDV", operadores.Single(o => o.Nome == "Sem Chave").RotuloSituacao);
    }

    [Fact]
    public async Task OperadoresAtivosVemPrimeiroEDepoisPorNomeEABuscaFiltraPeloNome()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearOperadoresAsync(fixture);
        var servico = new CadastroLocalService(fixture.CriarContexto);

        var todos = await servico.ListarOperadoresAsync(null);

        Assert.Equal("Antigo", todos[^1].Nome);                                // o desativado por último
        Assert.Equal(todos.Take(4).Select(o => o.Nome).OrderBy(n => n), todos.Take(4).Select(o => o.Nome));
        Assert.Equal("Mariana Souza", Assert.Single(await servico.ListarOperadoresAsync("mariana")).Nome);
        Assert.Empty(await servico.ListarOperadoresAsync("ninguem"));
    }

    [Fact]
    public async Task OResumoDoOperadorNaoTemCpfNemChave()
    {
        // Proteção de dado: a projeção que a tela recebe simplesmente não tem esses campos.
        var campos = typeof(OperadorResumo).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(campos, c => c.Contains("Cpf", StringComparison.OrdinalIgnoreCase) || c.Contains("PdvKey", StringComparison.OrdinalIgnoreCase));
        await Task.CompletedTask;
    }

    // ---- contagens das abas ----

    [Fact]
    public async Task ContagemDasAbasEOTotalIndependenteDaBuscaEDoCorteDaLista()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearGruposEProdutosAsync(fixture);
        await SemearOperadoresAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            for (var i = 0; i < CadastroLocalService.LimiteLista + 30; i++)
                context.Clientes.Add(new Cliente { Nome = $"Cliente {i:000}" });
            await context.SaveChangesAsync();
        }
        var viewModel = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto));
        viewModel.Busca = "Cliente 001";
        await viewModel.BuscarCommand.Execute();

        Assert.Equal("Produtos ( 4 )", viewModel.RotuloProdutos);
        Assert.Equal($"Clientes ( {CadastroLocalService.LimiteLista + 30} )", viewModel.RotuloClientes);   // o total, não o corte de 200 nem o filtro
        Assert.Equal("Operadores de Caixa ( 5 )", viewModel.RotuloOperadores);
    }

    // ---- as abas (ViewModel) ----

    [Fact]
    public async Task AbreNaAbaDeProdutosETrocaDeAbaPelosComandos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto));
        Assert.Equal(AbaCadastros.Produtos, viewModel.AbaAtual);
        Assert.True(viewModel.ProdutosAtiva);
        Assert.False(viewModel.ClientesAtiva);

        await viewModel.SelecionarAbaCommand.Execute(AbaCadastros.Operadores);

        Assert.True(viewModel.OperadoresAtiva);
        Assert.False(viewModel.ProdutosAtiva);
    }

    [Fact]
    public async Task NovoClienteLevaAAbaDeClientesEAbreOFormularioQueFechaPeloX()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto));
        Assert.False(viewModel.FormularioClienteAberto);                       // a tela abre limpa

        await viewModel.NovoClienteCommand.Execute();

        Assert.True(viewModel.ClientesAtiva);
        Assert.True(viewModel.FormularioClienteAberto);

        await viewModel.FecharFormularioClienteCommand.Execute();
        Assert.False(viewModel.FormularioClienteAberto);
        Assert.True(viewModel.ClientesAtiva);                                  // fechar o formulário não troca de aba
    }

    [Fact]
    public async Task MensagemDeVazioNaoMandaCadastrarProdutoNemOperadorPoisSoOClienteSeCadastra()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto));
        await viewModel.IniciarAsync();

        Assert.Contains("cadastre o primeiro", viewModel.MensagemVazioClientes);
        Assert.DoesNotContain("cadastre", viewModel.MensagemVazioProdutos);
        Assert.Contains("sincronizado ainda", viewModel.MensagemVazioProdutos);
        Assert.Contains("sincronizado ainda", viewModel.MensagemVazioOperadores);
    }

    [Fact]
    public async Task AtualizarAposSincronizacaoRecarregaOperadoresEContagens()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto));
        await viewModel.IniciarAsync();
        Assert.Empty(viewModel.Operadores);

        await SemearOperadoresAsync(fixture);                                  // a sincronização trouxe os funcionários
        await viewModel.AtualizarAposSincronizacaoAsync();

        Assert.Equal(5, viewModel.Operadores.Count);
        Assert.Equal("Operadores de Caixa ( 5 )", viewModel.RotuloOperadores);
    }

    // ---- as migrations ----

    [Fact]
    public void MigrationsDeixamOBancoExatamenteComoOModelo()
    {
        // As migrations foram geradas à mão (numa cópia do projeto): se o modelo ganhar uma coluna/tabela sem migration
        // correspondente, este teste falha em vez de o app quebrar no computador do operador ao abrir o banco.
        using var conexao = new SqliteConnection("Data Source=:memory:");
        conexao.Open();
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conexao).Options);

        context.Database.Migrate();

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.True(context.Grupos.Count() >= 0);                              // a tabela Grupos existe depois de migrar
    }
}

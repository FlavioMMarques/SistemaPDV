using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

public class CadastrosViewModelTests
{
    private const string CpfValido = "529.982.247-25";

    private static async Task SemearAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.Clientes.Add(new Cliente { Nome = "Maria Souza", SyncStatus = SyncStatus.Sincronizado, IdExterno = 1 });
        context.Clientes.Add(new Cliente { Nome = "Pedro Alves", SyncStatus = SyncStatus.Sincronizado, IdExterno = 2 });
        context.Produtos.Add(new Produto { Nome = "Refrigerante 2L", PrecoVenda = 9.90m, IdExterno = 10 });
        context.Produtos.Add(new Produto { Nome = "Pão Francês", PrecoVenda = 0.80m, IdExterno = 11 });
        await context.SaveChangesAsync();
    }

    private static CadastrosViewModel CriarViewModel(SqliteInMemoryFixture fixture) =>
        new(new CadastroLocalService(fixture.CriarContexto));

    [Fact]
    public async Task IniciarCarregaClientesEProdutos()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var viewModel = CriarViewModel(fixture);

        await viewModel.IniciarAsync();

        Assert.Equal(2, viewModel.Clientes.Count);
        Assert.Equal(2, viewModel.Produtos.Count);
        Assert.Null(viewModel.MensagemVazioClientes);
        Assert.Null(viewModel.MensagemVazioProdutos);
    }

    [Fact]
    public async Task BuscarFiltraAsDuasListasPeloMesmoTexto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();

        viewModel.Busca = "pe";
        await viewModel.BuscarCommand.Execute();

        Assert.Equal("Pedro Alves", Assert.Single(viewModel.Clientes).Nome);
        Assert.Empty(viewModel.Produtos);
    }

    [Fact]
    public async Task SemDadosMostraMensagemDeEstadoVazio()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);

        await viewModel.IniciarAsync();

        Assert.False(string.IsNullOrWhiteSpace(viewModel.MensagemVazioClientes));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.MensagemVazioProdutos));
    }

    [Fact]
    public async Task BuscaSemResultadoTemMensagemDiferenteDeSemDados()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();

        viewModel.Busca = "zzz";
        await viewModel.BuscarCommand.Execute();

        Assert.Contains("busca", viewModel.MensagemVazioClientes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CriarClienteGravaLimpaOFormularioEMostraNaListaComoPendente()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();

        viewModel.NovoNome = "Ana Lima";
        viewModel.NovoCpfCnpj = CpfValido;
        await viewModel.CriarClienteCommand.Execute();

        var novo = Assert.Single(viewModel.Clientes, c => c.Nome == "Ana Lima");
        Assert.Equal(SyncStatus.PendenteSync, novo.SyncStatus);
        Assert.Equal(string.Empty, viewModel.NovoNome);
        Assert.Equal(string.Empty, viewModel.NovoCpfCnpj);
        Assert.False(viewModel.MensagemFormEhErro);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.MensagemForm));
    }

    [Fact]
    public async Task CriarClienteComBuscaAtivaLimpaABuscaParaONovoAparecer()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        viewModel.Busca = "maria";
        await viewModel.IniciarAsync();

        viewModel.NovoNome = "Ana Lima";
        viewModel.NovoCpfCnpj = CpfValido;
        await viewModel.CriarClienteCommand.Execute();

        Assert.Equal(string.Empty, viewModel.Busca);
        Assert.Contains(viewModel.Clientes, c => c.Nome == "Ana Lima");
    }

    [Fact]
    public async Task DocumentoInvalidoMostraErroMantemOFormularioENaoGrava()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();

        viewModel.NovoNome = "Ana Lima";
        viewModel.NovoCpfCnpj = "123.456.789-00";
        await viewModel.CriarClienteCommand.Execute();

        Assert.True(viewModel.MensagemFormEhErro);
        Assert.Contains("inválido", viewModel.MensagemForm);
        Assert.Equal("Ana Lima", viewModel.NovoNome);
        Assert.Equal("123.456.789-00", viewModel.NovoCpfCnpj);
        Assert.Equal(2, viewModel.Clientes.Count);
    }

    [Fact]
    public void CriarClienteFicaDesabilitadoSemNomeOuSemDocumento()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);

        var podeCriar = false;
        using var inscricao = viewModel.CriarClienteCommand.CanExecute.Subscribe(valor => podeCriar = valor);
        Assert.False(podeCriar);

        viewModel.NovoNome = "Ana";
        Assert.False(podeCriar);                 // só o nome: falta o CPF/CNPJ (os dois têm * no modal)

        viewModel.NovoCpfCnpj = CpfValido;
        Assert.True(podeCriar);

        viewModel.NovoNome = "   ";
        Assert.False(podeCriar);

        viewModel.NovoNome = "Ana";
        viewModel.NovoCpfCnpj = "  ";
        Assert.False(podeCriar);
    }

    [Fact]
    public async Task ModalAbreComACidadeDaEmpresaELimpoENovaAberturaTrazDeNovo()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1, Cidade = "João Pessoa", Uf = "PB" });
            await context.SaveChangesAsync();
        }
        var viewModel = CriarViewModel(fixture);

        await viewModel.NovoClienteCommand.Execute();

        Assert.True(viewModel.FormularioClienteAberto);
        Assert.Equal("João Pessoa - PB", viewModel.NovoEndereco.CidadeUf);
        Assert.Equal(string.Empty, viewModel.NovoNome);

        viewModel.NovoNome = "Ana";
        viewModel.NovoTelefone = "(83) 99999-8888";
        viewModel.NovoEndereco.CidadeUf = "Campina Grande - PB";
        await viewModel.FecharFormularioClienteCommand.Execute();       // cancelar descarta tudo

        Assert.False(viewModel.FormularioClienteAberto);
        Assert.Equal(string.Empty, viewModel.NovoNome);
        Assert.Equal(string.Empty, viewModel.NovoTelefone);
        Assert.Null(viewModel.MensagemForm);

        await viewModel.NovoClienteCommand.Execute();
        Assert.Equal("João Pessoa - PB", viewModel.NovoEndereco.CidadeUf);        // volta a trazer a da empresa
    }

    [Fact]
    public async Task SalvarComTodosOsCamposFechaOModalEGravaTelefoneEmailECidade()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();
        await viewModel.NovoClienteCommand.Execute();

        viewModel.NovoNome = "Ana Lima";
        viewModel.NovoCpfCnpj = CpfValido;
        viewModel.NovoTelefone = "(83) 99999-8888";
        viewModel.NovoEmail = "ana@empresa.com.br";
        viewModel.NovoEndereco.CidadeUf = "João Pessoa - PB";
        await viewModel.CriarClienteCommand.Execute();

        Assert.False(viewModel.FormularioClienteAberto);                 // salvou: o modal fecha
        Assert.False(viewModel.MensagemFormEhErro);
        Assert.NotNull(viewModel.MensagemFormSucesso);                   // e o aviso fica na tela de trás
        var novo = Assert.Single(viewModel.Clientes, c => c.Nome == "Ana Lima");
        Assert.Equal("(83) 99999-8888", novo.Telefone);
        Assert.Equal("João Pessoa - PB", novo.CidadeUf);
        using var leitura = fixture.CriarContexto();
        Assert.Equal("ana@empresa.com.br", leitura.Clientes.Single(c => c.Nome == "Ana Lima").ContatoEmail);
    }

    [Fact]
    public async Task TelefoneInvalidoMantemOModalAbertoComOErroELimpaSoAoCorrigir()
    {
        using var fixture = new SqliteInMemoryFixture();
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();
        await viewModel.NovoClienteCommand.Execute();
        viewModel.NovoNome = "Ana Lima";
        viewModel.NovoCpfCnpj = CpfValido;
        viewModel.NovoTelefone = "123";

        await viewModel.CriarClienteCommand.Execute();

        Assert.True(viewModel.FormularioClienteAberto);
        Assert.True(viewModel.MensagemFormEhErro);
        Assert.Contains("Telefone", viewModel.MensagemFormErro);
        Assert.Equal("123", viewModel.NovoTelefone);                     // não perde o que digitou
        Assert.DoesNotContain(viewModel.Clientes, c => c.Nome == "Ana Lima");
    }

    [Fact]
    public async Task ListaCortadaNoLimiteAvisaOOperador()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            for (var i = 0; i < CadastroLocalService.LimiteLista + 1; i++)
                context.Clientes.Add(new Cliente { Nome = $"Cliente {i:D4}" });
            await context.SaveChangesAsync();
        }
        var viewModel = CriarViewModel(fixture);

        await viewModel.IniciarAsync();

        Assert.True(viewModel.ClientesCortados);
        Assert.False(viewModel.ProdutosCortados);
    }

    [Fact]
    public async Task AtualizarAposSincronizacaoRecarregaSemMexerNoFormularioNemNaBuscaDigitada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var viewModel = CriarViewModel(fixture);
        viewModel.Busca = "maria";
        await viewModel.BuscarCommand.Execute();
        Assert.Single(viewModel.Clientes);

        // O operador já digitou outra busca (sem apertar Enter) e está preenchendo o
        // formulário; enquanto isso, o ciclo de sincronização adiciona uma "Maria".
        viewModel.Busca = "pedro";
        viewModel.NovoNome = "Ana em digitacao";
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.Add(new Cliente { Nome = "Maria Nova", SyncStatus = SyncStatus.Sincronizado, IdExterno = 3 });
            await context.SaveChangesAsync();
        }

        await ((IAtualizavelPorSincronizacao)viewModel).AtualizarAposSincronizacaoAsync();

        Assert.Equal(2, viewModel.Clientes.Count);                 // recarregou com a busca APLICADA ("maria")
        Assert.All(viewModel.Clientes, c => Assert.Contains("Maria", c.Nome));
        Assert.Equal("pedro", viewModel.Busca);                    // o texto digitado ficou como estava
        Assert.Equal("Ana em digitacao", viewModel.NovoNome);      // e o formulário também
    }

    [Fact]
    public async Task ReenviarFalhasDevolveOClienteQueDesistiuAFilaEAvisa()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.Add(new Cliente { Nome = "Ana Lima", SyncStatus = SyncStatus.FalhaSync, TentativasEnvio = 8, UltimoErroSync = "422 (parou de tentar)" });
            await context.SaveChangesAsync();
        }
        var viewModel = CriarViewModel(fixture);
        await viewModel.IniciarAsync();

        await viewModel.ReenviarFalhasCommand.Execute();

        await using var leitura = fixture.CriarContexto();
        Assert.Equal(0, leitura.Clientes.Single().TentativasEnvio);
        Assert.Contains("1", viewModel.MensagemReenvio);
    }
}

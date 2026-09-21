using System.Net;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Cadastros (Task 49): leitura com busca sobre o banco local + criação local de
// cliente. Criar NÃO chama a rede — só grava PendenteSync; quem envia é o outbox
// (CatalogSyncService.SincronizarClienteNovoAsync, Task 48).
[SupportedOSPlatform("windows")]
public class CadastroLocalServiceTests
{
    private const string CpfValido = "529.982.247-25";
    private const string CnpjValido = "11.222.333/0001-81";

    private static async Task SemearClientesAsync(SqliteInMemoryFixture fixture, params (string Nome, string? Doc, SyncStatus Status)[] clientes)
    {
        await using var context = fixture.CriarContexto();
        foreach (var (nome, doc, status) in clientes)
            context.Clientes.Add(new Cliente { Nome = nome, CpfCnpj = doc, SyncStatus = status });
        await context.SaveChangesAsync();
    }

    private static async Task SemearProdutosAsync(SqliteInMemoryFixture fixture, params (string Nome, string? Barras, string? Sku)[] produtos)
    {
        await using var context = fixture.CriarContexto();
        var idExterno = 1;
        foreach (var (nome, barras, sku) in produtos)
            context.Produtos.Add(new Produto { Nome = nome, CodigoBarras = barras, Sku = sku, PrecoVenda = 5m, IdExterno = idExterno++ });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ListaTodosOsClientesInclusiveOsAindaPendentes()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearClientesAsync(fixture, ("Maria", null, SyncStatus.Sincronizado), ("João", null, SyncStatus.PendenteSync));
        var service = new CadastroLocalService(fixture.CriarContexto);

        var clientes = await service.ListarClientesAsync(null);

        Assert.Equal(2, clientes.Count);
        Assert.Contains(clientes, c => c.Nome == "João" && c.SyncStatus == SyncStatus.PendenteSync);
    }

    [Fact]
    public async Task BuscaDeClienteIgnoraMaiusculas()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearClientesAsync(fixture, ("Maria Souza", null, SyncStatus.Sincronizado), ("Pedro", null, SyncStatus.Sincronizado));
        var service = new CadastroLocalService(fixture.CriarContexto);

        var clientes = await service.ListarClientesAsync("MARIA");

        Assert.Equal("Maria Souza", Assert.Single(clientes).Nome);
    }

    [Fact]
    public async Task BuscaDeClientePorDocumentoAceitaPontuacao()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearClientesAsync(fixture, ("Maria", "52998224725", SyncStatus.Sincronizado), ("Pedro", "11222333000181", SyncStatus.Sincronizado));
        var service = new CadastroLocalService(fixture.CriarContexto);

        var clientes = await service.ListarClientesAsync("529.982");

        Assert.Equal("Maria", Assert.Single(clientes).Nome);
    }

    [Fact]
    public async Task CaracteresCoringaNaBuscaSaoLiteraisNaoPadrao()
    {
        // "%" e "_" são curingas do LIKE — digitados pelo operador têm que ser texto.
        using var fixture = new SqliteInMemoryFixture();
        await SemearClientesAsync(fixture, ("Maria", null, SyncStatus.Sincronizado), ("100% Fruta", null, SyncStatus.Sincronizado));
        var service = new CadastroLocalService(fixture.CriarContexto);

        Assert.Equal("100% Fruta", Assert.Single(await service.ListarClientesAsync("%")).Nome);
        Assert.Empty(await service.ListarClientesAsync("_"));
    }

    [Fact]
    public async Task ListaDeClientesTemLimiteParaNaoCarregarOBancoInteiro()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            for (var i = 0; i < CadastroLocalService.LimiteLista + 50; i++)
                context.Clientes.Add(new Cliente { Nome = $"Cliente {i:D4}" });
            await context.SaveChangesAsync();
        }
        var service = new CadastroLocalService(fixture.CriarContexto);

        var clientes = await service.ListarClientesAsync(null);

        Assert.Equal(CadastroLocalService.LimiteLista, clientes.Count);
    }

    [Fact]
    public async Task BuscaDeProdutoPorNomeCodigoDeBarrasOuSku()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearProdutosAsync(fixture, ("Refrigerante", "7891000100103", "REF-01"), ("Pão", "7890000000017", "PAO-01"));
        var service = new CadastroLocalService(fixture.CriarContexto);

        Assert.Equal("Refrigerante", Assert.Single(await service.ListarProdutosAsync("refri")).Nome);
        Assert.Equal("Pão", Assert.Single(await service.ListarProdutosAsync("7890000000017")).Nome);
        Assert.Equal("Pão", Assert.Single(await service.ListarProdutosAsync("PAO-01")).Nome);
        Assert.Equal(2, (await service.ListarProdutosAsync("")).Count);
    }

    [Fact]
    public async Task CriaClienteLocalPendenteComDocumentoSoComDigitos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("  Maria Souza  ", CpfValido));

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(cliente.Id, resultado.ClienteId);
        Assert.Equal("Maria Souza", cliente.Nome);
        Assert.Equal("52998224725", cliente.CpfCnpj);
        Assert.Equal(TipoPessoa.Fisica, cliente.Pessoa);
        Assert.Equal(SyncStatus.PendenteSync, cliente.SyncStatus);
        Assert.Null(cliente.IdExterno);
    }

    [Fact]
    public async Task DocumentoComQuatorzeDigitosViraPessoaJuridica()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        await service.CriarClienteAsync(new NovoClienteDados("Padaria do Zé", CnpjValido));

        using var leitura = fixture.CriarContexto();
        Assert.Equal(TipoPessoa.Juridica, leitura.Clientes.Single().Pessoa);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task DocumentoEhObrigatorio(string? documento)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Cliente de balcão", documento));

        Assert.False(resultado.Sucesso);
        Assert.Contains("CPF", resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Clientes);
    }

    [Fact]
    public async Task GravaTelefoneEmailECidadeDoModal()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados(
            "Ana Lima", CpfValido, "(83) 99999-8888", "  ana@empresa.com.br ", "João Pessoa - PB"));

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal("83", cliente.ContatoDdd);
        Assert.Equal("999998888", cliente.ContatoTelefone);
        Assert.Equal("Ana Lima", cliente.ContatoNome);          // a API exige nome dentro do contato
        Assert.Equal("ana@empresa.com.br", cliente.ContatoEmail);
        Assert.Equal("João Pessoa", cliente.Cidade);
        Assert.Equal("PB", cliente.Uf);
    }

    [Theory]
    [InlineData("(83) 99999-8888", "83", "999998888")]
    [InlineData("83999998888", "83", "999998888")]
    [InlineData("83 3221-4589", "83", "32214589")]              // fixo: 8 dígitos
    [InlineData("+55 (83) 99999-8888", "83", "999998888")]      // com código do país
    public async Task TelefoneAceitaOsFormatosUsuais(string digitado, string ddd, string numero)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, digitado));

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(ddd, cliente.ContatoDdd);
        Assert.Equal(numero, cliente.ContatoTelefone);
    }

    [Theory]
    [InlineData("99999-8888")]        // sem DDD
    [InlineData("(00) 99999-8888")]   // DDD inexistente
    [InlineData("abc")]
    [InlineData("(83) 99999-88x8")]
    public async Task TelefoneInvalidoNaoGravaNada(string digitado)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, digitado));

        Assert.False(resultado.Sucesso);
        Assert.Contains("Telefone", resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Clientes);
    }

    [Theory]
    [InlineData("sem-arroba")]
    [InlineData("a@b")]
    [InlineData("a b@c.com")]
    public async Task EmailInvalidoNaoGravaNada(string email)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, null, email));

        Assert.False(resultado.Sucesso);
        Assert.Contains("E-mail", resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Clientes);
    }

    [Theory]
    [InlineData("João Pessoa - PB", "João Pessoa", "PB")]
    [InlineData("Campina Grande/pb", "Campina Grande", "PB")]
    [InlineData("Cabedelo", "Cabedelo", null)]                  // sem UF reconhecível: tudo é a cidade
    public async Task CidadeUfSeparaAUfQuandoHouver(string digitado, string cidade, string? uf)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, null, null, digitado));

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(cidade, cliente.Cidade);
        Assert.Equal(uf, cliente.Uf);
    }

    [Fact]
    public async Task CamposOpcionaisVaziosFicamNulos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, "  ", "", "   "));

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Null(cliente.ContatoDdd);
        Assert.Null(cliente.ContatoTelefone);
        Assert.Null(cliente.ContatoNome);
        Assert.Null(cliente.ContatoEmail);
        Assert.Null(cliente.Cidade);
        Assert.Null(cliente.Uf);
    }

    [Fact]
    public async Task CidadeUfPadraoVemDaEmpresaDoAparelho()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);
        Assert.Equal(string.Empty, await service.ObterCidadeUfPadraoAsync());   // empresa ainda não sincronizada

        await using (var context = fixture.CriarContexto())
        {
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1, Cidade = "João Pessoa", Uf = "PB" });
            await context.SaveChangesAsync();
        }

        Assert.Equal("João Pessoa - PB", await service.ObterCidadeUfPadraoAsync());
    }

    [Theory]
    [InlineData("", CpfValido)]
    [InlineData("   ", null)]
    [InlineData("Maria", "123.456.789-00")] // dígitos verificadores errados
    [InlineData("Maria", "11111111111")]    // sequência repetida
    [InlineData("Maria", "1234")]           // tamanho impossível
    [InlineData("Maria", "abc")]            // sem dígito nenhum
    [InlineData("Maria", "529.982.247-25x")] // letra no meio do documento
    public async Task DadosInvalidosNaoGravamNada(string nome, string? documento)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados(nome, documento));

        Assert.False(resultado.Sucesso);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Mensagem));
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Clientes);
    }

    [Fact]
    public async Task MensagemDeDocumentoInvalidoNaoRepeteODocumento()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Maria", "123.456.789-00"));

        Assert.DoesNotContain("123", resultado.Mensagem);
    }

    [Fact]
    public async Task NomeMuitoLongoEhRecusadoEmVezDeEstourarOBanco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados(new string('a', 151), null));

        Assert.False(resultado.Sucesso);
    }

    [Fact]
    public async Task DocumentoJaCadastradoNaoDuplica()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);
        await service.CriarClienteAsync(new NovoClienteDados("Maria", CpfValido));

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Maria de novo", "52998224725"));

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Single(leitura.Clientes);
    }

    [Fact]
    public async Task ClienteCriadoLocalmenteNaoSomeNemDuplicaQuandoOIdExternoChega()
    {
        // Critério da Task 49: cria local -> push (Task 48) devolve id 77 -> o catálogo
        // puxado depois traz o MESMO id 77 e tem que atualizar a linha, não somar outra.
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
            await context.SaveChangesAsync();
        }
        var cadastro = new CadastroLocalService(fixture.CriarContexto);
        var criado = await cadastro.CriarClienteAsync(new NovoClienteDados("Maria Souza", CpfValido));

        var respostas = new Queue<string>(new[]
        {
            """{ "data": { "id": 77 } }""",
            """{ "current_page": 1, "data": [ { "id": 77, "nome": "MARIA SOUZA", "pessoa": "FISICA", "cpf_cnpj": "52998224725", "bloqueado": "0" } ], "next_page_url": null, "total": 1, "date_sync": 1758000000 }""",
        });
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(respostas.Dequeue(), Encoding.UTF8, "application/json"),
        });
        var sync = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

        await sync.SincronizarClienteNovoAsync(criado.ClienteId!.Value, "token-fake");
        await sync.SincronizarClientesAsync("token-fake");

        var clientes = await cadastro.ListarClientesAsync(null);
        var cliente = Assert.Single(clientes);
        Assert.Equal(criado.ClienteId, cliente.Id);
        Assert.Equal(SyncStatus.Sincronizado, cliente.SyncStatus);
    }
}

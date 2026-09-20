using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Push de cliente criado localmente pra POST .../clientes/clientes. Envia dado
// pessoal (nome, CPF/CNPJ) e usa o token — por isso os "casos de abuso" (documento
// inválido, http, resposta hostil) são testes de primeira classe aqui, não extras.
[SupportedOSPlatform("windows")]
public class CatalogSyncServiceClienteNovoTests
{
    private const string CpfValido = "529.982.247-25";
    private const string CnpjValido = "11.222.333/0001-81";

    private static HttpResponseMessage Resposta(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture, string urlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1")
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = urlApi });
        await context.SaveChangesAsync();
    }

    private static async Task<int> SemearClienteAsync(
        SqliteInMemoryFixture fixture, string nome = "Maria Souza", string? documento = CpfValido,
        TipoPessoa pessoa = TipoPessoa.Fisica, string? razaoSocial = null, SyncStatus status = SyncStatus.PendenteSync)
    {
        await using var context = fixture.CriarContexto();
        var cliente = new Cliente { Nome = nome, CpfCnpj = documento, Pessoa = pessoa, RazaoSocial = razaoSocial, SyncStatus = status };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();
        return cliente.Id;
    }

    private static CatalogSyncService CriarService(SqliteInMemoryFixture fixture, HttpClient httpClient) =>
        new(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

    [Fact]
    public async Task SucessoGravaIdExternoESincronizado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, """{ "data": { "id": 99 } }"""));

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.True(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(99, cliente.IdExterno);
        Assert.Equal(SyncStatus.Sincronizado, cliente.SyncStatus);
        Assert.Null(cliente.UltimoErroSync);
    }

    [Fact]
    public async Task PayloadTemSoOQueAApiPrecisaEDocumentoSoComDigitos()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        HttpRequestMessage? requisicao = null;
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            requisicao = req;
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta(HttpStatusCode.OK, """{ "data": { "id": 1 } }""");
        });

        await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.Equal(HttpMethod.Post, requisicao!.Method);
        Assert.Equal("https://exemplo.softcomshop.com.br/softauth/api/v2/clientes/clientes", requisicao.RequestUri!.ToString());
        Assert.Equal("Bearer", requisicao.Headers.Authorization!.Scheme);
        Assert.Contains("\"pessoa\":\"FISICA\"", corpo);
        Assert.Contains("\"nome\":\"Maria Souza\"", corpo);
        Assert.Contains("\"cpf_cnpj\":\"52998224725\"", corpo);
        // Números, não strings (contrato do Swagger) — e os defaults decididos na spec.
        Assert.Contains("\"contribuinte_icms\":9", corpo);
        Assert.Contains("\"indicador_finalidade\":0", corpo);
        // Nada de campo interno local vazando pra fora.
        Assert.DoesNotContain("SyncStatus", corpo);
        Assert.DoesNotContain("UltimoErro", corpo);
        Assert.DoesNotContain("token-fake", corpo);
    }

    [Fact]
    public async Task ClienteJuridicoSemRazaoSocialUsaONomeComoRazaoSocial()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture, nome: "Padaria do Zé", documento: CnpjValido, pessoa: TipoPessoa.Juridica);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta(HttpStatusCode.OK, """{ "data": { "id": 1 } }""");
        });

        await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        // Decodifica o JSON em vez de comparar texto: acentos saem escapados (é),
        // o que é JSON válido e a API decodifica normalmente.
        using var json = System.Text.Json.JsonDocument.Parse(corpo!);
        Assert.Equal("JURIDICA", json.RootElement.GetProperty("pessoa").GetString());
        Assert.Equal("Padaria do Zé", json.RootElement.GetProperty("razao_social").GetString());
    }

    [Fact]
    public async Task PessoaEhDerivadaDoDocumentoNaoDoCampoLocal()
    {
        // Um CNPJ marcado como Fisica localmente geraria 422 (razao_social obrigatória
        // quando JURIDICA) — o documento validado é a fonte da verdade.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture, documento: CnpjValido, pessoa: TipoPessoa.Fisica);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta(HttpStatusCode.OK, """{ "data": { "id": 1 } }""");
        });

        await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.Contains("\"pessoa\":\"JURIDICA\"", corpo);
    }

    [Fact]
    public async Task ClienteSemDocumentoEnviaSemCpfCnpj()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture, documento: null);
        string? corpo = null;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta(HttpStatusCode.OK, """{ "data": { "id": 1 } }""");
        });

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.True(resultado.Sucesso);
        Assert.DoesNotContain("cpf_cnpj", corpo);
    }

    [Fact]
    public async Task DocumentoInvalidoNaoChamaAApiEMarcaFalhaSemEcoarODocumento()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture, documento: "123.456.789-00");
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, "{}"); });

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(SyncStatus.FalhaSync, cliente.SyncStatus);
        Assert.Contains("CPF/CNPJ", cliente.UltimoErroSync);
        // O erro fica gravado e pode aparecer na UI/log: nunca repete o documento.
        Assert.DoesNotContain("123", cliente.UltimoErroSync);
        Assert.DoesNotContain("123", resultado.Mensagem);
    }

    [Fact]
    public async Task Erro422MarcaFalhaComMensagemDaApiEContinuaElegivel()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            Resposta(HttpStatusCode.UnprocessableEntity, """{ "errors": { "nome": ["Nome inválido."] } }"""));

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(SyncStatus.FalhaSync, cliente.SyncStatus);
        Assert.Contains("Nome inválido", cliente.UltimoErroSync);
        Assert.Null(cliente.IdExterno);
    }

    [Fact]
    public async Task Conflito409NaoViraSincronizadoPoisNaoTemosOIdExterno()
    {
        // Diferente de venda (guid) e caixa: o 409 aqui não devolve o id do cliente
        // que já existe — marcar Sincronizado deixaria IdExterno nulo e qualquer
        // venda pra esse cliente travada pra sempre.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            Resposta(HttpStatusCode.Conflict, """{ "errors": { "message": ["Cliente já cadastrado."] } }"""));

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(SyncStatus.FalhaSync, cliente.SyncStatus);
        Assert.Null(cliente.IdExterno);
    }

    [Theory]
    [InlineData("""{ "data": {} }""")]
    [InlineData("""{ "data": { "id": 0 } }""")]
    [InlineData("""{ "data": { "id": -5 } }""")]
    [InlineData("""{}""")]
    public async Task RespostaOkSemIdValidoMarcaFalha(string corpoResposta)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, corpoResposta));

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Null(leitura.Clientes.Single().IdExterno);
        Assert.Equal(SyncStatus.FalhaSync, leitura.Clientes.Single().SyncStatus);
    }

    [Fact]
    public async Task RespostaOkQueNaoEhJsonNaoLancaExcecao()
    {
        // Portal cativo de wi-fi de loja responde 200 com HTML no lugar da API.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, "<html>Faça login no wi-fi</html>"));

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.FalhaSync, leitura.Clientes.Single().SyncStatus);
    }

    [Fact]
    public async Task ErroGiganteDaApiEhTruncadoAntesDeIrProBanco()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var id = await SemearClienteAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ =>
            Resposta(HttpStatusCode.InternalServerError, new string('z', 200_000)));

        await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        using var leitura = fixture.CriarContexto();
        Assert.True(leitura.Clientes.Single().UltimoErroSync!.Length <= 500);
    }

    [Fact]
    public async Task ApiEmHttpNaoLoopbackNaoEnviaNadaENaoMarcaOCliente()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, "http://exemplo.softcomshop.com.br/registrar?client_id=1");
        var id = await SemearClienteAsync(fixture);
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, "{}"); });

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.False(resultado.Sucesso);
        Assert.False(chamou);
        // Problema de configuração, não do cliente: continua PendenteSync, sem erro gravado nele.
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(SyncStatus.PendenteSync, cliente.SyncStatus);
        Assert.Null(cliente.UltimoErroSync);
    }

    [Fact]
    public async Task ApiEmHttpLoopbackEhPermitidaParaDesenvolvimento()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, "http://localhost:73/registrar?client_id=1");
        var id = await SemearClienteAsync(fixture);
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => Resposta(HttpStatusCode.OK, """{ "data": { "id": 7 } }"""));

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.True(resultado.Sucesso);
    }

    [Fact]
    public async Task ClienteQueJaTemIdExternoNaoEhReenviado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        int id;
        await using (var context = fixture.CriarContexto())
        {
            var cliente = new Cliente { Nome = "Já sincronizado", IdExterno = 5, SyncStatus = SyncStatus.Sincronizado };
            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            id = cliente.Id;
        }
        var chamou = false;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(_ => { chamou = true; return Resposta(HttpStatusCode.OK, "{}"); });

        var resultado = await CriarService(fixture, httpClient).SincronizarClienteNovoAsync(id, "token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(0, resultado.Quantidade);
        Assert.False(chamou);
    }

    [Fact]
    public async Task LoteEnviaSoOsPendentesEUmErroNaoImpedeOsOutros()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClienteAsync(fixture, nome: "Doc inválido", documento: "000.000.000-00");
        await SemearClienteAsync(fixture, nome: "Boa Cliente", documento: CpfValido);
        await SemearClienteAsync(fixture, nome: "Falhou antes", documento: CnpjValido, pessoa: TipoPessoa.Juridica, status: SyncStatus.FalhaSync);
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.Add(new Cliente { Nome = "Veio da API", IdExterno = 3, SyncStatus = SyncStatus.Sincronizado });
            await context.SaveChangesAsync();
        }
        var nomesEnviados = new List<string>();
        var proximoId = 100;
        var httpClient = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            var corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            nomesEnviados.Add(corpo);
            return Resposta(HttpStatusCode.OK, $$"""{ "data": { "id": {{proximoId++}} } }""");
        });

        var resultado = await CriarService(fixture, httpClient).SincronizarClientesNovosPendentesAsync("token-fake");

        Assert.True(resultado.Sucesso);
        Assert.Equal(2, resultado.Quantidade);
        Assert.Equal(2, nomesEnviados.Count);
        Assert.DoesNotContain(nomesEnviados, c => c.Contains("Veio da API"));
        Assert.DoesNotContain(nomesEnviados, c => c.Contains("Doc inválido"));
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.FalhaSync, leitura.Clientes.Single(c => c.Nome == "Doc inválido").SyncStatus);
        Assert.Equal(SyncStatus.Sincronizado, leitura.Clientes.Single(c => c.Nome == "Boa Cliente").SyncStatus);
        Assert.Equal(SyncStatus.Sincronizado, leitura.Clientes.Single(c => c.Nome == "Falhou antes").SyncStatus);
    }
}

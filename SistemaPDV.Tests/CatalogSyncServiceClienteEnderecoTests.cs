using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// O objeto "endereco" do POST de cliente novo: CEP, logradouro, número, bairro, complemento e o "c_cidade" (código IBGE da cidade —
// o que a API pede no lugar do nome). Só vai o que existe; código que não tem 7 dígitos não vai.
[SupportedOSPlatform("windows")]
public class CatalogSyncServiceClienteEnderecoTests
{
    private const string CpfValido = "52998224725";

    private static HttpResponseMessage Resposta(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task<int> SemearAsync(SqliteInMemoryFixture fixture, Action<Cliente> ajustar)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1" });
        var cliente = new Cliente { Nome = "Maria Souza", CpfCnpj = CpfValido, SyncStatus = SyncStatus.PendenteSync };
        ajustar(cliente);
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();
        return cliente.Id;
    }

    // Envia o cliente e devolve o corpo da requisição.
    private static async Task<string> EnviarAsync(SqliteInMemoryFixture fixture, int id)
    {
        string? corpo = null;
        var http = FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            corpo = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Resposta("""{ "data": { "id": 1 } }""");
        });
        var service = new CatalogSyncService(fixture.CriarContexto, new SoftcomApiClient(http), new SegredoProtector(), new SoftcomAuthService(http, new SegredoProtector()));

        await service.SincronizarClienteNovoAsync(id, "token-fake");

        return corpo!;
    }

    [Fact]
    public async Task ClienteComEnderecoEnviaOObjetoEnderecoComOCodigoIbgeDaCidade()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearAsync(fixture, c =>
        {
            c.Cep = "58039000"; c.Endereco = "Avenida Epitácio Pessoa"; c.Numero = "1200"; c.Bairro = "Miramar";
            c.Complemento = "Sala 3"; c.Cidade = "João Pessoa"; c.Uf = "PB"; c.CodigoCidade = "2507507";
        });

        var corpo = await EnviarAsync(fixture, id);

        using var json = JsonDocument.Parse(corpo);
        var endereco = json.RootElement.GetProperty("endereco");
        Assert.Equal("58039000", endereco.GetProperty("cep").GetString());
        Assert.Equal("Avenida Epitácio Pessoa", endereco.GetProperty("endereco").GetString());
        Assert.Equal("1200", endereco.GetProperty("numero").GetString());
        Assert.Equal("Miramar", endereco.GetProperty("bairro").GetString());
        Assert.Equal("Sala 3", endereco.GetProperty("complemento").GetString());
        Assert.Equal("2507507", endereco.GetProperty("c_cidade").GetString());   // o código IBGE, não o nome da cidade
        Assert.False(endereco.TryGetProperty("cidade", out _));
    }

    [Fact]
    public async Task ClienteSemEnderecoNaoEnviaOObjetoEndereco()
    {
        using var fixture = new SqliteInMemoryFixture();
        // Só o NOME da cidade (sem código, sem CEP): não há o que mandar — a API pede o código, não o nome.
        var id = await SemearAsync(fixture, c => { c.Cidade = "João Pessoa"; c.Uf = "PB"; });

        var corpo = await EnviarAsync(fixture, id);

        using var json = JsonDocument.Parse(corpo);
        Assert.False(json.RootElement.TryGetProperty("endereco", out _));
    }

    [Fact]
    public async Task EnderecoParcialEnviaSoOQueExisteECodigoInvalidoNaoVai()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearAsync(fixture, c => { c.Endereco = "Rua das Flores"; c.CodigoCidade = "123"; });

        var corpo = await EnviarAsync(fixture, id);

        using var json = JsonDocument.Parse(corpo);
        var endereco = json.RootElement.GetProperty("endereco");
        Assert.Equal("Rua das Flores", endereco.GetProperty("endereco").GetString());
        Assert.False(endereco.TryGetProperty("c_cidade", out _));   // 3 dígitos não é código IBGE: a API o rejeitaria
        Assert.False(endereco.TryGetProperty("cep", out _));
        Assert.False(endereco.TryGetProperty("numero", out _));
    }

    [Fact]
    public async Task EnderecoEContatoConvivemNoMesmoCorpo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var id = await SemearAsync(fixture, c =>
        {
            c.ContatoNome = "Maria Souza"; c.ContatoDdd = "83"; c.ContatoTelefone = "999998888";
            c.Cep = "58039000"; c.CodigoCidade = "2507507";
        });

        var corpo = await EnviarAsync(fixture, id);

        using var json = JsonDocument.Parse(corpo);
        Assert.Equal("83", json.RootElement.GetProperty("contato").GetProperty("ddd").GetString());
        Assert.Equal("2507507", json.RootElement.GetProperty("endereco").GetProperty("c_cidade").GetString());
    }
}

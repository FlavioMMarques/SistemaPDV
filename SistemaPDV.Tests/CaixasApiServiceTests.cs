using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Consulta (só leitura) dos caixas da API: GET .../financeiro/caixa-funcoes. O que importa: a URL com os filtros, o nome do
// operador resolvido pelo cadastro local, o formato tolerante da resposta e cada falha com um texto que o operador entende.
[SupportedOSPlatform("windows")]
public class CaixasApiServiceTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private const string DuasLinhas = """
        { "data": [
            { "id": 24, "api_device_id": "PARAMETRIZADOR", "operador_id": 2, "turno": 2, "data_abertura": "2025-09-29 08:00:00",
              "data_caixa": "2025-09-29", "data_fechamento": null, "usuario_abertura_id": 2, "usuario_fechamento_id": null },
            { "id": 30, "api_device_id": "PDV-01", "operador_id": 9, "turno": 1, "data_abertura": "2025-09-30 07:30:00",
              "data_caixa": "2025-09-30", "data_fechamento": "2025-09-30 17:45:10", "usuario_abertura_id": 9, "usuario_fechamento_id": 9 } ],
          "current_page": 1, "last_page": 3, "per_page": 15, "total": 40 }
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task SemearAsync(SqliteInMemoryFixture fixture, string? urlApi = UrlApi)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
        {
            UrlApi = urlApi,
            ApiClienteId = urlApi is null ? null : "1",
            ApiClienteSecretProtegido = urlApi is null ? null : new SegredoProtector().Proteger("segredo"),
        });
        context.Funcionarios.Add(new Funcionario { Nome = "Carlos Silva", IdExterno = 2 });
        await context.SaveChangesAsync();
    }

    // Responde ao token e à listagem; guarda a URL (caminho + consulta) de cada chamada à listagem.
    private sealed class ApiFake
    {
        public List<string> Listagens { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? Listagem { get; init; }
        public bool TokenFalha { get; init; }

        public HttpClient Cliente() => FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            var caminho = req.RequestUri!.AbsolutePath;
            if (caminho.EndsWith("/authentication/token"))
                return TokenFalha ? Json(HttpStatusCode.Unauthorized, """{ "error": "invalid_client" }""")
                                  : Json(HttpStatusCode.OK, """{ "data": { "token": "token-fake" } }""");

            Listagens.Add(req.RequestUri.PathAndQuery);
            return Listagem?.Invoke(req) ?? Json(HttpStatusCode.OK, DuasLinhas);
        });
    }

    private static CaixasApiService CriarService(SqliteInMemoryFixture fixture, HttpClient http)
    {
        var protetor = new SegredoProtector();
        return new CaixasApiService(fixture.CriarContexto, new SoftcomApiClient(http), new SoftcomAuthService(http, protetor));
    }

    [Fact]
    public async Task SemFiltrosPedeAPrimeiraPaginaSemDatasEDeixaAApiUsarOsUltimos7Dias()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake();

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, somenteFechados: false, pagina: 1);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal("/softauth/api/v2/financeiro/caixa-funcoes?per_page=15&page=1", Assert.Single(api.Listagens));
    }

    [Fact]
    public async Task FiltrosViramParametrosDaConsulta()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake();

        await CriarService(fixture, api.Cliente()).ConsultarAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 21), somenteFechados: true, pagina: 3);

        Assert.Equal(
            "/softauth/api/v2/financeiro/caixa-funcoes?per_page=15&page=3&data_inicial=2026-09-01&data_final=2026-09-21&fechado=1",
            Assert.Single(api.Listagens));
    }

    [Fact]
    public async Task MostraOsCaixasComONomeDoOperadorLocalEOMaisRecentePrimeiro()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);

        var resultado = await CriarService(fixture, new ApiFake().Cliente()).ConsultarAsync(null, null, false, 1);

        var pagina = resultado.Pagina!;
        Assert.Equal(new[] { 30, 24 }, pagina.Linhas.Select(l => l.Id));
        var aberto = pagina.Linhas.Single(l => l.Id == 24);
        Assert.Equal("Carlos Silva", aberto.Operador);                      // operador_id 2 = Funcionario.IdExterno 2
        Assert.Equal(2, aberto.Turno);
        Assert.Equal(new DateOnly(2025, 9, 29), aberto.DataCaixa);
        Assert.Equal(new DateTime(2025, 9, 29, 8, 0, 0), aberto.Abertura);
        Assert.Null(aberto.Fechamento);
        Assert.False(aberto.Fechado);
        Assert.Equal("PARAMETRIZADOR", aberto.Dispositivo);
        var fechado = pagina.Linhas.Single(l => l.Id == 30);
        Assert.Equal("Operador 9", fechado.Operador);                       // não está no cadastro local
        Assert.True(fechado.Fechado);
        Assert.Equal(new DateTime(2025, 9, 30, 17, 45, 10), fechado.Fechamento);
    }

    [Fact]
    public async Task InformaAPaginaAtualOTotalDePaginasEODeRegistros()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);

        var resultado = await CriarService(fixture, new ApiFake().Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.Equal(1, resultado.Pagina!.PaginaAtual);
        Assert.Equal(3, resultado.Pagina.TotalPaginas);
        Assert.Equal(40, resultado.Pagina.Total);
    }

    [Fact]
    public async Task AceitaARespostaEmbrulhadaNumArrayENumerosEmTexto()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake
        {
            Listagem = _ => Json(HttpStatusCode.OK,
                """[ { "data": [ { "id": "24", "operador_id": "2", "turno": "3", "data_caixa": "2025-09-29" } ], "current_page": "1", "last_page": "1", "total": "1" } ]"""),
        };

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        var linha = Assert.Single(resultado.Pagina!.Linhas);
        Assert.Equal(24, linha.Id);
        Assert.Equal("Carlos Silva", linha.Operador);
        Assert.Equal(3, linha.Turno);
        Assert.Null(linha.Abertura);                                        // campo ausente = sem data, sem quebrar
    }

    [Fact]
    public async Task ListaVaziaEhSucessoComZeroLinhas()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake { Listagem = _ => Json(HttpStatusCode.OK, """{ "data": [], "current_page": 1, "last_page": 1, "total": 0 }""") };

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.True(resultado.Sucesso);
        Assert.Empty(resultado.Pagina!.Linhas);
        Assert.Equal(0, resultado.Pagina.Total);
    }

    [Fact]
    public async Task DataInicialDepoisDaFinalNaoChamaAApi()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake();

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 1), false, 1);

        Assert.False(resultado.Sucesso);
        Assert.Contains("data inicial", resultado.Mensagem);
        Assert.Empty(api.Listagens);
    }

    [Fact]
    public async Task SemVinculoAvisaParaVincularENaoChamaAApi()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new ApiFake();

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.False(resultado.Sucesso);
        Assert.Contains("vinculado", resultado.Mensagem);
        Assert.Empty(api.Listagens);
    }

    [Fact]
    public async Task UrlSemHttpsNaoEnviaOTokenNemConsulta()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture, urlApi: "http://exemplo.softcomshop.com.br/registrar?client_id=1");
        var api = new ApiFake();

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.False(resultado.Sucesso);
        Assert.Contains("HTTPS", resultado.Mensagem);
        Assert.Empty(api.Listagens);
    }

    [Fact]
    public async Task FalhaNoTokenViraMensagemSemChamarAListagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake { TokenFalha = true };

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.False(resultado.Sucesso);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Mensagem));
        Assert.Empty(api.Listagens);
    }

    [Fact]
    public async Task Erro401DizQueASessaoExpirou()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake { Listagem = _ => Json(HttpStatusCode.Unauthorized, "{}") };

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.False(resultado.Sucesso);
        Assert.Contains("expirou", resultado.Mensagem);
    }

    [Fact]
    public async Task Erro422OuOutroMostraOMotivoDaApi()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake { Listagem = _ => Json((HttpStatusCode)422, """{ "errors": { "data_inicial": ["A data inicial é inválida."] } }""") };

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.False(resultado.Sucesso);
        Assert.Contains("data inicial é inválida", resultado.Mensagem);
    }

    [Theory]
    [InlineData("isto nao e json")]
    [InlineData("""{ "foo": "bar" }""")]       // JSON de outro formato: sem "data", vira lista vazia, nunca exceção
    [InlineData("123")]
    public async Task RespostaEstranhaNuncaLanca(string corpo)
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake { Listagem = _ => Json(HttpStatusCode.OK, corpo) };

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.True(resultado.Sucesso || !string.IsNullOrWhiteSpace(resultado.Mensagem));
    }

    [Fact]
    public async Task SemConexaoAvisaSemDerrubarATela()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);
        var api = new ApiFake { Listagem = _ => throw new HttpRequestException("host não é conhecido") };

        var resultado = await CriarService(fixture, api.Cliente()).ConsultarAsync(null, null, false, 1);

        Assert.False(resultado.Sucesso);
        Assert.Contains("conexão", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NaoGravaNadaNoBancoLocal()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearAsync(fixture);

        await CriarService(fixture, new ApiFake().Cliente()).ConsultarAsync(null, null, false, 1);

        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Caixas);                                       // é só a visão da nuvem
    }
}

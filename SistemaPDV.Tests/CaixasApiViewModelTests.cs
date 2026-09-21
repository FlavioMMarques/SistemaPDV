using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Web;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// A tela "Caixas no SoftcomShop" (modal em Configurações): abre já consultando, filtros dd/mm/aaaa, paginação que repete o filtro
// aplicado e erros que não apagam a tabela. Usa o serviço de verdade sobre uma API falsa.
[SupportedOSPlatform("windows")]
public class CaixasApiViewModelTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    // 3 páginas, 2 caixas por página; devolve a página pedida (current_page acompanha o "page" da consulta).
    private static string Pagina(int pagina) => $$"""
        { "data": [
            { "id": {{pagina * 10 + 2}}, "operador_id": 2, "turno": 1, "data_caixa": "2026-09-20", "data_abertura": "2026-09-20 08:00:00" },
            { "id": {{pagina * 10 + 1}}, "operador_id": 2, "turno": 2, "data_caixa": "2026-09-20", "data_abertura": "2026-09-20 09:00:00",
              "data_fechamento": "2026-09-20 18:00:00" } ],
          "current_page": {{pagina}}, "last_page": 3, "total": 6 }
        """;

    private sealed class Api
    {
        public List<string> Consultas { get; } = new();
        public Func<int, HttpResponseMessage>? Resposta { get; set; }

        public HttpClient Cliente() => FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/authentication/token"))
                return Json(HttpStatusCode.OK, """{ "data": { "token": "token-fake" } }""");

            Consultas.Add(req.RequestUri.Query);
            var pagina = int.Parse(HttpUtility.ParseQueryString(req.RequestUri.Query)["page"]!);
            return Resposta?.Invoke(pagina) ?? Json(HttpStatusCode.OK, Pagina(pagina));
        });
    }

    private static async Task<CaixasApiViewModel> CriarAsync(SqliteInMemoryFixture fixture, Api api)
    {
        await using (var context = fixture.CriarContexto())
        {
            context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
            {
                UrlApi = UrlApi, ApiClienteId = "1", ApiClienteSecretProtegido = new SegredoProtector().Proteger("segredo"),
            });
            context.Funcionarios.Add(new Funcionario { Nome = "Carlos Silva", IdExterno = 2 });
            await context.SaveChangesAsync();
        }

        var http = api.Cliente();
        var protetor = new SegredoProtector();
        return new CaixasApiViewModel(new CaixasApiService(fixture.CriarContexto, new SoftcomApiClient(http), new SoftcomAuthService(http, protetor)));
    }

    private static bool Pode(ReactiveUI.ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> comando)
    {
        var pode = false;
        comando.CanExecute.Subscribe(v => pode = v);
        return pode;
    }

    [Fact]
    public async Task AbrirJaConsultaOsUltimos7DiasSemFiltroEMostraATabela()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var viewModel = await CriarAsync(fixture, api);
        Assert.False(viewModel.Aberto);

        await viewModel.AbrirAsync();

        Assert.True(viewModel.Aberto);
        Assert.Equal("?per_page=15&page=1", Assert.Single(api.Consultas));       // sem datas: a API usa os últimos 7 dias
        Assert.Equal(2, viewModel.Linhas.Count);
        Assert.Equal("Carlos Silva", viewModel.Linhas[0].Operador);
        Assert.Null(viewModel.Mensagem);
        Assert.False(viewModel.SemResultados);
        Assert.Equal("Página 1 de 3 • 6 caixa(s)", viewModel.ResumoPagina);
        Assert.False(viewModel.Carregando);
    }

    [Fact]
    public async Task FiltrosDeDataEFechadosViramAConsultaEFormatosComUmDigitoValem()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var viewModel = await CriarAsync(fixture, api);
        await viewModel.AbrirAsync();
        api.Consultas.Clear();

        viewModel.DataInicial = "1/9/2026";
        viewModel.DataFinal = "21/09/2026";
        viewModel.SomenteFechados = true;
        await viewModel.ConsultarCommand.Execute();

        Assert.Equal("?per_page=15&page=1&data_inicial=2026-09-01&data_final=2026-09-21&fechado=1", Assert.Single(api.Consultas));
    }

    [Theory]
    [InlineData("2026-09-01", "")]        // formato errado
    [InlineData("31/02/2026", "")]        // dia que não existe
    [InlineData("abc", "")]
    [InlineData("", "32/09/2026")]
    public async Task DataInvalidaMostraOErroENaoChamaAApi(string inicial, string final)
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var viewModel = await CriarAsync(fixture, api);
        await viewModel.AbrirAsync();
        api.Consultas.Clear();

        viewModel.DataInicial = inicial;
        viewModel.DataFinal = final;
        await viewModel.ConsultarCommand.Execute();

        Assert.Contains("inválida", viewModel.Mensagem);
        Assert.Empty(api.Consultas);
        Assert.Equal(2, viewModel.Linhas.Count);                                  // a tabela anterior continua na tela
    }

    [Fact]
    public async Task PaginacaoAvancaEVoltaRepetindoOFiltroAplicadoNaoOQueFoiDigitadoDepois()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var viewModel = await CriarAsync(fixture, api);
        await viewModel.AbrirAsync();
        viewModel.DataInicial = "01/09/2026";
        viewModel.SomenteFechados = true;
        await viewModel.ConsultarCommand.Execute();                               // página 1 com o filtro
        api.Consultas.Clear();

        viewModel.DataInicial = "15/09/2026";                                     // digitou outra data, mas NÃO consultou
        await viewModel.ProximaCommand.Execute();

        Assert.Equal("?per_page=15&page=2&data_inicial=2026-09-01&fechado=1", Assert.Single(api.Consultas));
        Assert.Equal(2, viewModel.PaginaAtual);
        Assert.Equal("Página 2 de 3 • 6 caixa(s)", viewModel.ResumoPagina);

        await viewModel.AnteriorCommand.Execute();
        Assert.Equal(1, viewModel.PaginaAtual);
    }

    [Fact]
    public async Task AnteriorFicaDesabilitadoNaPrimeiraEProximaNaUltima()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var viewModel = await CriarAsync(fixture, api);
        await viewModel.AbrirAsync();

        Assert.False(Pode(viewModel.AnteriorCommand));
        Assert.True(Pode(viewModel.ProximaCommand));

        await viewModel.ProximaCommand.Execute();
        await viewModel.ProximaCommand.Execute();                                 // página 3 de 3

        Assert.Equal(3, viewModel.PaginaAtual);
        Assert.True(Pode(viewModel.AnteriorCommand));
        Assert.False(Pode(viewModel.ProximaCommand));
    }

    [Fact]
    public async Task ErroDeConsultaMostraOMotivoEMantemATabelaAnterior()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var viewModel = await CriarAsync(fixture, api);
        await viewModel.AbrirAsync();
        api.Resposta = _ => Json(HttpStatusCode.InternalServerError, """{ "errors": "Servidor indisponível." }""");

        await viewModel.ProximaCommand.Execute();

        Assert.Contains("Servidor indisponível", viewModel.Mensagem);
        Assert.Equal(2, viewModel.Linhas.Count);                                  // não apagou o que já estava na tela
        Assert.Equal(1, viewModel.PaginaAtual);                                   // e não avançou de página
        Assert.False(viewModel.Carregando);
        Assert.False(viewModel.SemResultados);
    }

    [Fact]
    public async Task ConsultaSemCaixasAvisaEmVezDeTabelaEmBranco()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api { Resposta = _ => Json(HttpStatusCode.OK, """{ "data": [], "current_page": 1, "last_page": 1, "total": 0 }""") };
        var viewModel = await CriarAsync(fixture, api);

        await viewModel.AbrirAsync();

        Assert.True(viewModel.SemResultados);
        Assert.Empty(viewModel.Linhas);
        Assert.Null(viewModel.Mensagem);
    }

    [Fact]
    public async Task AbrirDeNovoComecaLimpoEFecharEsconde()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var viewModel = await CriarAsync(fixture, api);
        await viewModel.AbrirAsync();
        viewModel.DataInicial = "01/09/2026";
        viewModel.SomenteFechados = true;

        await viewModel.FecharCommand.Execute();
        Assert.False(viewModel.Aberto);

        api.Consultas.Clear();
        await viewModel.AbrirAsync();

        Assert.Equal(string.Empty, viewModel.DataInicial);
        Assert.False(viewModel.SomenteFechados);
        Assert.Equal("?per_page=15&page=1", Assert.Single(api.Consultas));
    }

    // ---- o botão em Configurações ----

    [Fact]
    public void BotaoDeConsultaSoFicaHabilitadoQuandoOServicoExiste()
    {
        using var fixture = new SqliteInMemoryFixture();
        var protetor = new SegredoProtector();
        var http = new Api().Cliente();
        var configuracao = new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(http, protetor), protetor);
        var servico = new CaixasApiService(fixture.CriarContexto, new SoftcomApiClient(http), new SoftcomAuthService(http, protetor));

        var sem = new ConfiguracoesViewModel(configuracao);
        var com = new ConfiguracoesViewModel(configuracao, caixasApiService: servico);

        Assert.False(sem.Caixas.Disponivel);
        Assert.False(Pode(sem.AbrirCaixasCommand));
        Assert.True(com.Caixas.Disponivel);
        Assert.True(Pode(com.AbrirCaixasCommand));
    }

    [Fact]
    public async Task BotaoDeConfiguracoesAbreAConsulta()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new Api();
        var caixas = await CriarAsync(fixture, api);          // já semeou a configuração
        var protetor = new SegredoProtector();
        var http = api.Cliente();
        var configuracao = new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(http, protetor), protetor);
        var servico = new CaixasApiService(fixture.CriarContexto, new SoftcomApiClient(http), new SoftcomAuthService(http, protetor));
        var viewModel = new ConfiguracoesViewModel(configuracao, caixasApiService: servico);

        await viewModel.AbrirCaixasCommand.Execute();

        Assert.True(viewModel.Caixas.Aberto);
        Assert.Equal(2, viewModel.Caixas.Linhas.Count);
    }
}

using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Os ciclos (o que roda a cada tick) são métodos públicos e testáveis; o
// DispatcherTimer que os dispara é só a "cola" (Iniciar/Parar) e não é testado aqui —
// precisa de um Dispatcher real, então fica pra conferência manual (dotnet run).
[SupportedOSPlatform("windows")]
public class SincronizacaoBackgroundServiceTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";
    private const string CpfValido = "529.982.247-25";
    private const string PaginaVazia = """{ "current_page": 1, "data": [], "next_page_url": null, "total": 0, "date_sync": 1758000000 }""";

    // Registra cada requisição (método + caminho) e responde por rota — um "servidor"
    // mínimo. `sobrescrever` deixa o teste trocar a resposta de uma rota específica.
    private sealed class ApiFake
    {
        private readonly List<string> requisicoes = new();
        private readonly object trava = new();

        public Func<HttpRequestMessage, HttpResponseMessage?>? Sobrescrever { get; init; }
        public IReadOnlyList<string> Requisicoes { get { lock (trava) return requisicoes.ToList(); } }
        public int Contar(string trecho) => Requisicoes.Count(r => r.Contains(trecho));

        public HttpClient CriarHttpClient() => FakeHttpMessageHandler.CriarHttpClient(requisicao =>
        {
            lock (trava) requisicoes.Add($"{requisicao.Method} {requisicao.RequestUri!.AbsolutePath}");

            if (Sobrescrever?.Invoke(requisicao) is { } resposta)
                return resposta;

            var caminho = requisicao.RequestUri!.AbsolutePath;
            if (caminho.EndsWith("/authentication/token"))
                return Json(HttpStatusCode.OK, """{ "data": { "token": "token-fake" } }""");
            if (requisicao.Method == HttpMethod.Post && caminho.EndsWith("/clientes/clientes"))
                return Json(HttpStatusCode.OK, """{ "data": { "id": 55 } }""");
            if (requisicao.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, PaginaVazia);

            return Json(HttpStatusCode.NotFound, "{}");
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture, string? urlApi = UrlApi)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao
        {
            UrlApi = urlApi,
            ApiClienteId = urlApi is null ? null : "1",
            ApiClienteSecretProtegido = urlApi is null ? null : new SegredoProtector().Proteger("segredo"),
        });
        await context.SaveChangesAsync();
    }

    private static async Task<int> SemearClientePendenteAsync(SqliteInMemoryFixture fixture, string nome = "Maria Souza")
    {
        await using var context = fixture.CriarContexto();
        var cliente = new Cliente { Nome = nome, CpfCnpj = DocumentoValidator.SoDigitos(CpfValido), SyncStatus = SyncStatus.PendenteSync };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();
        return cliente.Id;
    }

    private static async Task SemearCaixaPendenteAsync(SqliteInMemoryFixture fixture)
    {
        await using (var context = fixture.CriarContexto())
        {
            context.Funcionarios.Add(new Funcionario { Nome = "Carlos Silva", IdExterno = 2 });
            await context.SaveChangesAsync();
        }

        await using var leitura = fixture.CriarContexto();
        var funcionarioId = leitura.Funcionarios.Single().Id;
        await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 20), 1, 10m);
    }

    private static SincronizacaoBackgroundService CriarService(SqliteInMemoryFixture fixture, HttpClient httpClient)
    {
        var apiClient = new SoftcomApiClient(httpClient);
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);
        return new SincronizacaoBackgroundService(
            fixture.CriarContexto,
            authService,
            new CatalogSyncService(fixture.CriarContexto, apiClient, segredoProtector, authService),
            new CaixaSyncService(fixture.CriarContexto, apiClient),
            new VendaSyncService(fixture.CriarContexto, apiClient));
    }

    // ---- pausado sem configuração ----

    [Fact]
    public async Task SemConfiguracaoPreenchidaNenhumCicloFazNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, urlApi: null);
        await SemearClientePendenteAsync(fixture);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloRapidoAsync();
        await service.ExecutarCicloCatalogoAsync();

        Assert.Empty(api.Requisicoes);
        Assert.Equal(EstadoConexao.Desconhecida, service.Estado);
    }

    [Fact]
    public async Task SemNenhumaLinhaDeConfiguracaoTambemNaoQuebra()
    {
        using var fixture = new SqliteInMemoryFixture();
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloRapidoAsync();

        Assert.Empty(api.Requisicoes);
    }

    // ---- ciclo do catálogo (5 min) ----

    [Fact]
    public async Task CicloDoCatalogoAutenticaEBuscaOsCincoRecursosEFicaOnline()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloCatalogoAsync();

        Assert.Equal(1, api.Contar("/authentication/token"));
        Assert.Equal(5, api.Requisicoes.Count(r => r.StartsWith("GET")));
        Assert.Equal(EstadoConexao.Online, service.Estado);
    }

    [Fact]
    public async Task FalhaEmUmRecursoDoCatalogoNaoEscondeOProblemaOnlineComFalhasEMostraQual()
    {
        // Autenticar não basta: se os funcionários não vêm, ninguém loga — e o header
        // mostrava 🟢 mesmo assim, sem pista nenhuma do que estava errado.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake
        {
            Sobrescrever = r => r.RequestUri!.AbsolutePath.EndsWith("/api/v2/funcionarios")
                ? Json(HttpStatusCode.InternalServerError, """{ "message": "erro no servidor" }""")
                : null,
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloCatalogoAsync();

        Assert.Equal(EstadoConexao.OnlineComFalhas, service.Estado);
        Assert.Contains("Funcionários", service.MensagemUltimoCiclo);
        Assert.Contains("500", service.MensagemUltimoCiclo);
    }

    [Fact]
    public async Task CatalogoQueSoFalhouParcialmenteNaoEhTentadoACada30Segundos()
    {
        // O catch-up de 30 s é só pro app recém-aberto/vinculado; falha parcial espera o
        // ritmo de 5 min (senão um endpoint quebrado faria o catálogo inteiro rodar sem parar).
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake
        {
            Sobrescrever = r => r.RequestUri!.AbsolutePath.EndsWith("/api/v2/funcionarios")
                ? Json(HttpStatusCode.InternalServerError, "{}")
                : null,
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloRapidoAsync();
        var getsAposPrimeiro = api.Requisicoes.Count(r => r.StartsWith("GET"));
        await service.ExecutarCicloRapidoAsync();

        Assert.Equal(getsAposPrimeiro, api.Requisicoes.Count(r => r.StartsWith("GET")));
    }

    [Fact]
    public async Task OnlineDoCatalogoMostraQuantosItensCadaRecursoTrouxe()
    {
        // 🟢 com o banco vazio é indiagnosticável: a dica diz se a API respondeu "0 itens".
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, new ApiFake().CriarHttpClient());

        await service.ExecutarCicloCatalogoAsync();

        Assert.Equal(EstadoConexao.Online, service.Estado);
        Assert.Contains("Funcionários 0", service.MensagemUltimoCiclo);
        Assert.Contains("Produtos 0", service.MensagemUltimoCiclo);
    }

    [Fact]
    public async Task FalhaDeRedeNoCatalogoViraOfflineSemLancar()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake { Sobrescrever = _ => throw new HttpRequestException("sem rede") };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloCatalogoAsync();

        Assert.Equal(EstadoConexao.Offline, service.Estado);
        Assert.False(string.IsNullOrWhiteSpace(service.MensagemUltimoCiclo));
    }

    [Fact]
    public async Task VoltaAFicarOnlineNoCicloSeguinteQuandoARedeVolta()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var semRede = true;
        var api = new ApiFake { Sobrescrever = _ => semRede ? throw new HttpRequestException("sem rede") : null };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloCatalogoAsync();
        Assert.Equal(EstadoConexao.Offline, service.Estado);

        semRede = false;
        await service.ExecutarCicloCatalogoAsync();
        Assert.Equal(EstadoConexao.Online, service.Estado);
    }

    [Fact]
    public async Task ExcecaoInesperadaNoCicloNaoEscapaEViraOffline()
    {
        // InvalidOperationException não é das que o SoftcomAuthService trata (só HttpRequest/
        // TaskCanceled/Json) — sobe até o ciclo, que não pode deixá-la escapar.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake { Sobrescrever = _ => throw new InvalidOperationException("falha inesperada") };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloCatalogoAsync();

        Assert.Equal(EstadoConexao.Offline, service.Estado);
        Assert.Contains("falha inesperada", service.MensagemUltimoCiclo);
    }

    [Fact]
    public async Task UrlInseguraNaoEnviaNadaEFicaOfflineComMensagem()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, urlApi: "http://exemplo.softcomshop.com.br/registrar?client_id=1");
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloCatalogoAsync();

        Assert.Empty(api.Requisicoes);
        Assert.Equal(EstadoConexao.Offline, service.Estado);
        Assert.Contains("HTTPS", service.MensagemUltimoCiclo);
    }

    // ---- ciclo do outbox (30 s) ----

    [Fact]
    public async Task OutboxSemPendenciasNaoFazNenhumaChamadaDeRede()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        // Nem o token: a cada 30 s, sem nada a enviar, não vale gastar uma requisição.
        Assert.Empty(api.Requisicoes);
    }

    [Fact]
    public async Task OutboxIgnoraItensEmEsperaOuQueDesistiramSemNemPedirToken()
    {
        // Sem isso, um único cliente com erro permanente faria o app autenticar a cada 30 s
        // pra nada (a política de retentativa já o tirou da fila de envio).
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.AddRange(
                new Cliente { Nome = "em espera", SyncStatus = SyncStatus.FalhaSync, TentativasEnvio = 2, ProximaTentativaEm = DateTime.UtcNow.AddHours(1) },
                new Cliente { Nome = "desistiu", SyncStatus = SyncStatus.FalhaSync, TentativasEnvio = PoliticaRetentativa.MaximoTentativas });
            await context.SaveChangesAsync();
        }
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        Assert.Empty(api.Requisicoes);
    }

    [Fact]
    public async Task OutboxEnviaClientePendenteEFicaOnline()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClientePendenteAsync(fixture);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(55, cliente.IdExterno);
        Assert.Equal(SyncStatus.Sincronizado, cliente.SyncStatus);
        Assert.Equal(EstadoConexao.Online, service.Estado);
    }

    [Fact]
    public async Task OutboxEnviaAberturaDepoisVendasEPorUltimoOFechamentoDoCaixa()
    {
        // O fechamento resume o caixa: se ele chegar à API ANTES das vendas, a API fecha um caixa "sem vendas".
        // Ordem certa: abrir -> vendas -> fechar (o caixa aberto é o que as vendas referenciam).
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        int caixaId, produtoId, formaId;
        await using (var context = fixture.CriarContexto())
        {
            var configuracao = context.ConfiguracoesSincronizacao.Single();
            configuracao.ClienteConsumidorFinalIdExterno = 1;
            var funcionario = new Funcionario { Nome = "Carlos", IdExterno = 2 };
            var produto = new Produto { Nome = "Refri", PrecoVenda = 10m, IdExterno = 10, ProdutoIdApi = 100 };
            var forma = new FormaPagamento { Nome = "PIX", Tipo = "CARTEIRA_DIGITAL", CodigoNfce = "17", IdExterno = 5 };
            context.AddRange(funcionario, produto, forma);
            context.Empresas.Add(new Empresa { RazaoSocial = "Softcom", Cnpj = "12345678000199", IdExterno = 1 });
            await context.SaveChangesAsync();
            var caixaLocal = (await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(funcionario.Id, new DateOnly(2026, 9, 20), 1, 10m)).Valor!;
            (caixaId, produtoId, formaId) = (caixaLocal.Id, produto.Id, forma.Id);
        }
        await new VendaService(fixture.CriarContexto).RegistrarVendaLocalAsync(caixaId, null, new[] { (produtoId, 1m, 10m, 0m, 0m) }, new[] { (formaId, 10m) });
        await new CaixaService(fixture.CriarContexto).FecharCaixaLocalAsync(caixaId, 10m, new[] { (formaId, 10m) }, Array.Empty<(string, decimal)>());
        var api = new ApiFake
        {
            Sobrescrever = r =>
            {
                var caminho = r.RequestUri!.AbsolutePath;
                if (caminho.EndsWith("/caixa-funcoes/abrir")) return Json(HttpStatusCode.OK, """{ "data": { "success": { "id": 28 } } }""");
                if (caminho.EndsWith("/caixa-funcoes/fechar")) return Json(HttpStatusCode.OK, "{}");
                if (caminho.EndsWith("/vendas")) return Json(HttpStatusCode.OK, """{ "data": { "id": 500 } }""");
                return null;
            },
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        var posts = api.Requisicoes.Where(r => r.StartsWith("POST") && !r.Contains("authentication")).Select(r => r[(r.LastIndexOf('/') + 1)..]).ToList();
        Assert.Equal(new[] { "abrir", "vendas", "fechar" }, posts);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.Sincronizado, leitura.Caixas.Single().SyncStatus);   // fechamento confirmado
        Assert.Equal(SyncStatus.Sincronizado, leitura.Vendas.Single().SyncStatus);
    }

    [Fact]
    public async Task OutboxEnviaCaixaPendente()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCaixaPendenteAsync(fixture);
        var api = new ApiFake
        {
            Sobrescrever = r => r.RequestUri!.AbsolutePath.EndsWith("/caixa-funcoes/abrir")
                ? Json(HttpStatusCode.OK, """{ "data": { "success": { "id": 24 } } }""")
                : null,
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        using var leitura = fixture.CriarContexto();
        Assert.True(leitura.Caixas.Single().AberturaSincronizada);
        Assert.Equal(24, leitura.Caixas.Single().IdExterno);
    }

    [Fact]
    public async Task FalhaNoEnvioDeClienteNaoImpedeOEnvioDoCaixa()
    {
        // Cada etapa do outbox é isolada: um cliente rejeitado (422) não pode travar
        // o caixa que o operador está esperando confirmar.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClientePendenteAsync(fixture);
        await SemearCaixaPendenteAsync(fixture);
        var api = new ApiFake
        {
            Sobrescrever = r =>
            {
                var caminho = r.RequestUri!.AbsolutePath;
                if (caminho.EndsWith("/clientes/clientes"))
                    return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": { "cpf_cnpj": ["invalido"] } }""");
                if (caminho.EndsWith("/caixa-funcoes/abrir"))
                    return Json(HttpStatusCode.OK, """{ "data": { "success": { "id": 24 } } }""");
                return null;
            },
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.FalhaSync, leitura.Clientes.Single().SyncStatus);
        Assert.True(leitura.Caixas.Single().AberturaSincronizada);
    }

    [Fact]
    public async Task ExcecaoNaEtapaDeCaixaNaoEscapaDoCicloNemDesfazOQueJaFoiEnviado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClientePendenteAsync(fixture);
        await SemearCaixaPendenteAsync(fixture);
        var api = new ApiFake
        {
            Sobrescrever = r => r.RequestUri!.AbsolutePath.EndsWith("/caixa-funcoes/abrir")
                ? throw new InvalidOperationException("erro inesperado na etapa de caixa")
                : null,
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        using var leitura = fixture.CriarContexto();
        Assert.Equal(SyncStatus.Sincronizado, leitura.Clientes.Single().SyncStatus);
        Assert.False(leitura.Caixas.Single().AberturaSincronizada);
    }

    [Fact]
    public async Task VariosCaixasPendentesSaoEnviadosNoMesmoCiclo()
    {
        // SincronizarCaixaPendenteAsync trata UM caixa por chamada; sem repetir, dois
        // caixas pendentes levariam dois ciclos (60 s) pra confirmar.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearCaixaPendenteAsync(fixture);
        await using (var context = fixture.CriarContexto())
        {
            var funcionarioId = context.Funcionarios.Single().Id;
            await new CaixaService(fixture.CriarContexto).AbrirCaixaLocalAsync(funcionarioId, new DateOnly(2026, 9, 21), 1, 10m);
        }
        var proximoId = 24;
        var api = new ApiFake
        {
            Sobrescrever = r => r.RequestUri!.AbsolutePath.EndsWith("/caixa-funcoes/abrir")
                ? Json(HttpStatusCode.OK, $$"""{ "data": { "success": { "id": {{proximoId++}} } } }""")
                : null,
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        using var leitura = fixture.CriarContexto();
        Assert.All(leitura.Caixas, c => Assert.True(c.AberturaSincronizada));
    }

    [Fact]
    public async Task FalhaDeRedeNoOutboxFicaOfflineEMantemAsPendencias()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClientePendenteAsync(fixture);
        var api = new ApiFake { Sobrescrever = _ => throw new HttpRequestException("sem rede") };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloOutboxAsync();

        Assert.Equal(EstadoConexao.Offline, service.Estado);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal(SyncStatus.PendenteSync, cliente.SyncStatus);
        Assert.Null(cliente.IdExterno);
    }

    // ---- ritmo rápido: catálogo inicial + outbox ----

    [Fact]
    public async Task CicloRapidoPuxaOCatalogoNaPrimeiraVezENaoRepeteNasSeguintes()
    {
        // Sem isso, logo depois de vincular o dispositivo o operador esperaria até 5 min
        // pelos funcionários (e ninguém consegue logar sem eles).
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloRapidoAsync();
        var getsAposPrimeiro = api.Requisicoes.Count(r => r.StartsWith("GET"));
        await service.ExecutarCicloRapidoAsync();

        Assert.Equal(5, getsAposPrimeiro);
        Assert.Equal(5, api.Requisicoes.Count(r => r.StartsWith("GET")));
    }

    [Fact]
    public async Task CicloRapidoSemConfiguracaoNaoMarcaCatalogoComoFeitoEPuxaQuandoVincular()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture, urlApi: null);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());
        await service.ExecutarCicloRapidoAsync();
        Assert.Empty(api.Requisicoes);

        await using (var context = fixture.CriarContexto())
        {
            var configuracao = context.ConfiguracoesSincronizacao.Single();
            configuracao.UrlApi = UrlApi;
            configuracao.ApiClienteId = "1";
            configuracao.ApiClienteSecretProtegido = new SegredoProtector().Proteger("segredo");
            await context.SaveChangesAsync();
        }
        await service.ExecutarCicloRapidoAsync();

        Assert.Equal(5, api.Requisicoes.Count(r => r.StartsWith("GET")));
    }

    [Fact]
    public async Task CatalogoInicialQueFalhaPorRedeEhTentadoDeNovoNoCicloRapido()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var semRede = true;
        var api = new ApiFake { Sobrescrever = _ => semRede ? throw new HttpRequestException("sem rede") : null };
        var service = CriarService(fixture, api.CriarHttpClient());

        await service.ExecutarCicloRapidoAsync();
        semRede = false;
        await service.ExecutarCicloRapidoAsync();

        Assert.Equal(EstadoConexao.Online, service.Estado);
        Assert.Equal(5, api.Requisicoes.Count(r => r.StartsWith("GET")));
    }
    // ---- aviso de dados alterados (as listas se atualizam sozinhas) ----

    [Fact]
    public async Task OutboxQueEnviouAlgoAvisaQueOsDadosMudaram()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClientePendenteAsync(fixture);
        var service = CriarService(fixture, new ApiFake().CriarHttpClient());
        var avisos = 0;
        using var inscricao = service.DadosAlterados.Subscribe(_ => avisos++);

        await service.ExecutarCicloOutboxAsync();

        Assert.Equal(1, avisos);
    }

    [Fact]
    public async Task OutboxQueFalhouTambemAvisaPoisOStatusMudouParaFalha()
    {
        // 🟡 -> 🔴 também é mudança que a tela precisa mostrar.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClientePendenteAsync(fixture);
        var api = new ApiFake
        {
            Sobrescrever = r => r.RequestUri!.AbsolutePath.EndsWith("/clientes/clientes")
                ? Json(HttpStatusCode.UnprocessableEntity, """{ "errors": { "nome": ["invalido"] } }""")
                : null,
        };
        var service = CriarService(fixture, api.CriarHttpClient());
        var avisos = 0;
        using var inscricao = service.DadosAlterados.Subscribe(_ => avisos++);

        await service.ExecutarCicloOutboxAsync();

        Assert.Equal(1, avisos);
    }

    [Fact]
    public async Task OutboxOciosoNaoAvisaNada()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, new ApiFake().CriarHttpClient());
        var avisos = 0;
        using var inscricao = service.DadosAlterados.Subscribe(_ => avisos++);

        await service.ExecutarCicloOutboxAsync();

        Assert.Equal(0, avisos);
    }

    [Fact]
    public async Task CatalogoSoAvisaQuandoTrouxeAlgumItem()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var comItem = false;
        var api = new ApiFake
        {
            Sobrescrever = r => comItem && r.RequestUri!.AbsolutePath.EndsWith("/clientes/clientes") && r.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, """{ "current_page": 1, "data": [ { "id": 5, "nome": "Novo Cliente", "pessoa": "FISICA", "bloqueado": "0" } ], "next_page_url": null, "total": 1, "date_sync": 1758000000 }""")
                : null,
        };
        var service = CriarService(fixture, api.CriarHttpClient());
        var avisos = 0;
        using var inscricao = service.DadosAlterados.Subscribe(_ => avisos++);

        await service.ExecutarCicloCatalogoAsync();
        Assert.Equal(0, avisos);

        comItem = true;
        await service.ExecutarCicloCatalogoAsync();
        Assert.Equal(1, avisos);
    }


    // ---- concorrência ----

    [Fact]
    public async Task CicloEmAndamentoFazOSegundoSerIgnoradoEmVezDeSobrepor()
    {
        // Ticks de 30 s e de 5 min podem coincidir, e um ciclo lento pode passar do
        // próximo tick. Dois ciclos ao mesmo tempo disputariam o SQLite e (clientes)
        // fariam push e pull da mesma tabela juntos.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        await SemearClientePendenteAsync(fixture);
        using var entrou = new ManualResetEventSlim();
        using var liberar = new ManualResetEventSlim();
        var api = new ApiFake
        {
            Sobrescrever = r =>
            {
                if (r.RequestUri!.AbsolutePath.EndsWith("/authentication/token"))
                {
                    entrou.Set();
                    liberar.Wait(TimeSpan.FromSeconds(10));
                }
                return null;
            },
        };
        var service = CriarService(fixture, api.CriarHttpClient());

        var primeiro = Task.Run(() => service.ExecutarCicloOutboxAsync());
        Assert.True(entrou.Wait(TimeSpan.FromSeconds(10)));

        await service.ExecutarCicloOutboxAsync();   // deve voltar na hora, sem 2º token
        await service.ExecutarCicloCatalogoAsync(); // catálogo também espera a vez
        Assert.Equal(1, api.Contar("/authentication/token"));

        liberar.Set();
        await primeiro;
        Assert.Equal(1, api.Contar("/authentication/token"));
    }

    [Fact]
    public async Task CancelamentoPropagaEmVezDeVirarOffline()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, new ApiFake().CriarHttpClient());
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecutarCicloCatalogoAsync(cancelamento.Token));

        Assert.Equal(EstadoConexao.Desconhecida, service.Estado);
    }

    [Fact]
    public async Task SolicitarAgoraRodaOCicloRapidoSemEsperarOTimer()
    {
        // Usado logo depois de vincular o dispositivo: sem isso, o Login recusaria a
        // chave por até 30 s (os funcionários ainda não foram baixados).
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());

        service.SolicitarAgora();

        var limite = DateTime.UtcNow.AddSeconds(10);
        while (service.Estado != EstadoConexao.Online && DateTime.UtcNow < limite)
            await Task.Delay(20);
        Assert.Equal(EstadoConexao.Online, service.Estado);
        Assert.Equal(5, api.Requisicoes.Count(r => r.StartsWith("GET")));
    }

    [Fact]
    public void PararSemTerIniciadoNaoQuebra()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = CriarService(fixture, new ApiFake().CriarHttpClient());

        service.Parar();
        service.Parar();
    }

    // ---- indicador ----

    [Fact]
    public async Task EstadoEmiteCadaMudancaParaQuemObserva()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var api = new ApiFake();
        var service = CriarService(fixture, api.CriarHttpClient());
        var recebidos = new List<EstadoConexao>();
        using var inscricao = service.EstadoConexaoAlterada.Subscribe(recebidos.Add);

        await service.ExecutarCicloCatalogoAsync();

        // BehaviorSubject: quem assina depois já recebe o estado atual (Desconhecida) primeiro.
        Assert.Equal(new[] { EstadoConexao.Desconhecida, EstadoConexao.Online }, recebidos);
    }
}

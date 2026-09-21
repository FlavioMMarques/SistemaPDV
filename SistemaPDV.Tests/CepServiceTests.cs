using System.Net;
using System.Net.Http;
using System.Text;
using SistemaPDV.Services;

namespace SistemaPDV.Tests;

// Consulta de CEP (ViaCEP): o endereço e o código IBGE da cidade (o "c_cidade" da API), e cada falha com um texto que deixa o
// operador seguir manualmente. A resposta do serviço externo é dado não confiável — nunca lança.
public class CepServiceTests
{
    private const string Resposta58039 = """
        { "cep": "58039-000", "logradouro": "Avenida Epitácio Pessoa", "complemento": "", "bairro": "Miramar",
          "localidade": "João Pessoa", "uf": "PB", "ibge": "2507507", "gia": "", "ddd": "83", "siafi": "2051" }
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static (CepService Servico, List<string> Urls) Criar(Func<HttpRequestMessage, HttpResponseMessage> resposta)
    {
        var urls = new List<string>();
        var http = FakeHttpMessageHandler.CriarHttpClient(req => { urls.Add(req.RequestUri!.ToString()); return resposta(req); });
        return (new CepService(http), urls);
    }

    [Theory]
    [InlineData("58039000", "58039000")]
    [InlineData("58039-000", "58039000")]
    [InlineData(" 58.039-000 ", "58039000")]
    public void NormalizaOsFormatosUsuais(string digitado, string esperado)
    {
        Assert.True(CepService.TentarNormalizar(digitado, out var cep));
        Assert.Equal(esperado, cep);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5803900")]        // 7 dígitos
    [InlineData("580390000")]      // 9 dígitos
    [InlineData("5803900a")]
    [InlineData("58039/000")]
    public void RecusaCepMalFormado(string? digitado)
    {
        Assert.False(CepService.TentarNormalizar(digitado, out _));
    }

    [Fact]
    public async Task BuscaOEnderecoECodigoDoIbge()
    {
        var (servico, urls) = Criar(_ => Json(HttpStatusCode.OK, Resposta58039));

        var resultado = await servico.BuscarAsync("58039-000");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal("https://viacep.com.br/ws/58039000/json/", Assert.Single(urls));   // HTTPS, só os 8 dígitos
        var e = resultado.Endereco!;
        Assert.Equal("58039000", e.Cep);
        Assert.Equal("Avenida Epitácio Pessoa", e.Logradouro);
        Assert.Equal("Miramar", e.Bairro);
        Assert.Equal("João Pessoa", e.Cidade);
        Assert.Equal("PB", e.Uf);
        Assert.Equal("2507507", e.CodigoCidade);
    }

    [Fact]
    public async Task CepMalFormadoNaoChamaOServico()
    {
        var (servico, urls) = Criar(_ => Json(HttpStatusCode.OK, Resposta58039));

        var resultado = await servico.BuscarAsync("123");

        Assert.False(resultado.Sucesso);
        Assert.Contains("CEP inválido", resultado.Mensagem);
        Assert.Empty(urls);
    }

    [Theory]
    [InlineData("""{ "erro": true }""")]
    [InlineData("""{ "erro": "true" }""")]
    public async Task CepInexistenteDizQueNaoFoiEncontrado(string corpo)
    {
        var (servico, _) = Criar(_ => Json(HttpStatusCode.OK, corpo));

        var resultado = await servico.BuscarAsync("99999999");

        Assert.False(resultado.Sucesso);
        Assert.Contains("não encontrado", resultado.Mensagem);
    }

    [Fact]
    public async Task CepGeralSemLogradouroNemBairroAindaVale()
    {
        var (servico, _) = Criar(_ => Json(HttpStatusCode.OK,
            """{ "cep": "58900-000", "logradouro": "", "bairro": "", "localidade": "Cajazeiras", "uf": "PB", "ibge": "2503704" }"""));

        var resultado = await servico.BuscarAsync("58900000");

        Assert.True(resultado.Sucesso);
        Assert.Equal(string.Empty, resultado.Endereco!.Logradouro);
        Assert.Equal("Cajazeiras", resultado.Endereco.Cidade);
        Assert.Equal("2503704", resultado.Endereco.CodigoCidade);
    }

    [Theory]
    [InlineData("""{ "localidade": "João Pessoa", "uf": "PB", "ibge": "" }""")]          // sem código
    [InlineData("""{ "localidade": "João Pessoa", "uf": "PB", "ibge": "123" }""")]         // tamanho errado
    [InlineData("""{ "localidade": "João Pessoa", "uf": "PB" }""")]                        // campo ausente
    public async Task CodigoInvalidoOuAusenteEDescartadoEACidadeSegue(string corpo)
    {
        var (servico, _) = Criar(_ => Json(HttpStatusCode.OK, corpo));

        var resultado = await servico.BuscarAsync("58039000");

        Assert.True(resultado.Sucesso);
        Assert.Equal(string.Empty, resultado.Endereco!.CodigoCidade);   // nunca um código pela metade (a API o rejeitaria)
        Assert.Equal("João Pessoa", resultado.Endereco.Cidade);
    }

    [Fact]
    public async Task RespostaSemCidadeNaoServe()
    {
        var (servico, _) = Criar(_ => Json(HttpStatusCode.OK, """{ "logradouro": "Rua X", "uf": "PB", "ibge": "2507507" }"""));

        var resultado = await servico.BuscarAsync("58039000");

        Assert.False(resultado.Sucesso);
        Assert.Contains("cidade", resultado.Mensagem);
    }

    [Theory]
    [InlineData("isto nao e json")]
    [InlineData("[1, 2]")]
    [InlineData("123")]
    public async Task RespostaEstranhaNuncaLanca(string corpo)
    {
        var (servico, _) = Criar(_ => Json(HttpStatusCode.OK, corpo));

        var resultado = await servico.BuscarAsync("58039000");

        Assert.False(resultado.Sucesso);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Mensagem));
    }

    [Fact]
    public async Task CamposGigantesSaoLimitados()
    {
        var (servico, _) = Criar(_ => Json(HttpStatusCode.OK,
            $$"""{ "localidade": "{{new string('a', 5000)}}", "uf": "PB", "ibge": "2507507", "bairro": "{{new string('b', 5000)}}" }"""));

        var resultado = await servico.BuscarAsync("58039000");

        Assert.Equal(150, resultado.Endereco!.Cidade.Length);
        Assert.Equal(150, resultado.Endereco.Bairro.Length);
    }

    [Fact]
    public async Task ErroHttpOrientaAPreencherManualmente()
    {
        var (servico, _) = Criar(_ => Json(HttpStatusCode.InternalServerError, "{}"));

        var resultado = await servico.BuscarAsync("58039000");

        Assert.False(resultado.Sucesso);
        Assert.Contains("manualmente", resultado.Mensagem);
    }

    [Fact]
    public async Task SemConexaoOrientaAPreencherManualmenteSemLancar()
    {
        var (servico, _) = Criar(_ => throw new HttpRequestException("host não é conhecido"));

        var resultado = await servico.BuscarAsync("58039000");

        Assert.False(resultado.Sucesso);
        Assert.Contains("manualmente", resultado.Mensagem);
    }
}

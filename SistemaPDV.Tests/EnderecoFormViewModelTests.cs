using System.Net;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using SistemaPDV.Services;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// O bloco de endereço do modal de cliente: Buscar CEP preenche o endereço e a cidade e VINCULA o código IBGE da cidade; trocar a cidade
// à mão desvincula (um código de outra cidade seria pior que nenhum); falha da consulta nunca apaga o que já foi digitado.
public class EnderecoFormViewModelTests
{
    private const string CpfValido = "529.982.247-25";

    private const string Resposta58039 = """
        { "cep": "58039-000", "logradouro": "Avenida Epitácio Pessoa", "complemento": "", "bairro": "Miramar",
          "localidade": "João Pessoa", "uf": "PB", "ibge": "2507507" }
        """;

    private sealed class ViaCepFalso
    {
        public List<string> Consultas { get; } = new();
        public Func<string, HttpResponseMessage> Resposta { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Resposta58039, Encoding.UTF8, "application/json") };

        public CepService Servico() => new(FakeHttpMessageHandler.CriarHttpClient(req =>
        {
            Consultas.Add(req.RequestUri!.ToString());
            return Resposta(req.RequestUri.ToString());
        }));
    }

    private static bool PodeBuscar(EnderecoFormViewModel endereco)
    {
        var pode = false;
        endereco.BuscarCepCommand.CanExecute.Subscribe(v => pode = v);
        return pode;
    }

    [Fact]
    public async Task BuscarPreencheOEnderecoAFormataOCepEVinculaOCodigoDaCidade()
    {
        var api = new ViaCepFalso();
        var endereco = new EnderecoFormViewModel(api.Servico()) { Cep = "58039000" };

        await endereco.BuscarCepCommand.Execute();

        Assert.Equal("https://viacep.com.br/ws/58039000/json/", Assert.Single(api.Consultas));
        Assert.Equal("58039-000", endereco.Cep);                          // volta formatado
        Assert.Equal("Avenida Epitácio Pessoa", endereco.Logradouro);
        Assert.Equal("Miramar", endereco.Bairro);
        Assert.Equal("João Pessoa - PB", endereco.CidadeUf);
        Assert.Equal("2507507", endereco.CodigoCidadeParaSalvar);
        Assert.Contains("2507507", endereco.TextoCodigoCidade);
        Assert.False(endereco.Buscando);
        Assert.Null(endereco.MensagemCepErro);
        Assert.NotNull(endereco.MensagemCepAviso);
    }

    [Fact]
    public async Task BuscarNaoApagaONumeroNemOComplementoJaDigitados()
    {
        var endereco = new EnderecoFormViewModel(new ViaCepFalso().Servico()) { Cep = "58039000", Numero = "1200", Complemento = "Sala 3" };

        await endereco.BuscarCepCommand.Execute();

        Assert.Equal("1200", endereco.Numero);
        Assert.Equal("Sala 3", endereco.Complemento);
    }

    [Fact]
    public async Task CepGeralSemLogradouroNemBairroNaoApagaOQueJaFoiDigitado()
    {
        var api = new ViaCepFalso
        {
            Resposta = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "logradouro": "", "bairro": "", "localidade": "Cajazeiras", "uf": "PB", "ibge": "2503704" }""", Encoding.UTF8, "application/json"),
            },
        };
        var endereco = new EnderecoFormViewModel(api.Servico()) { Cep = "58900000", Logradouro = "Rua digitada", Bairro = "Centro" };

        await endereco.BuscarCepCommand.Execute();

        Assert.Equal("Rua digitada", endereco.Logradouro);
        Assert.Equal("Centro", endereco.Bairro);
        Assert.Equal("Cajazeiras - PB", endereco.CidadeUf);
        Assert.Equal("2503704", endereco.CodigoCidadeParaSalvar);
    }

    [Fact]
    public async Task TrocarACidadeAMaoDepoisDaBuscaDesvinculaOCodigo()
    {
        var endereco = new EnderecoFormViewModel(new ViaCepFalso().Servico()) { Cep = "58039000" };
        await endereco.BuscarCepCommand.Execute();
        Assert.Equal("2507507", endereco.CodigoCidadeParaSalvar);

        endereco.CidadeUf = "Campina Grande - PB";        // outra cidade: o código de João Pessoa não vale mais

        Assert.Equal(string.Empty, endereco.CodigoCidadeParaSalvar);
        Assert.Contains("sem código", endereco.TextoCodigoCidade);

        endereco.CidadeUf = "joão pessoa - pb ";            // voltou à cidade da consulta (maiúsculas e espaços não contam)
        Assert.Equal("2507507", endereco.CodigoCidadeParaSalvar);
    }

    [Fact]
    public void CidadeDigitadaSemBuscaNaoTemCodigo()
    {
        var endereco = new EnderecoFormViewModel(new ViaCepFalso().Servico()) { CidadeUf = "João Pessoa - PB" };   // a da empresa, por exemplo

        Assert.Equal(string.Empty, endereco.CodigoCidadeParaSalvar);
        Assert.Contains("sem código", endereco.TextoCodigoCidade);
    }

    [Fact]
    public async Task CepNaoEncontradoMostraOErroEMantemOQueJaEstavaDigitado()
    {
        var api = new ViaCepFalso
        {
            Resposta = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "erro": true }""", Encoding.UTF8, "application/json") },
        };
        var endereco = new EnderecoFormViewModel(api.Servico()) { Cep = "99999999", Logradouro = "Rua digitada", CidadeUf = "João Pessoa - PB" };

        await endereco.BuscarCepCommand.Execute();

        Assert.Contains("não encontrado", endereco.MensagemCepErro);
        Assert.Null(endereco.MensagemCepAviso);
        Assert.Equal("Rua digitada", endereco.Logradouro);
        Assert.Equal("João Pessoa - PB", endereco.CidadeUf);
        Assert.Equal(string.Empty, endereco.CodigoCidadeParaSalvar);
        Assert.False(endereco.Buscando);
    }

    [Fact]
    public async Task SemInternetOrientaAPreencherManualmente()
    {
        var api = new ViaCepFalso { Resposta = _ => throw new HttpRequestException("host não é conhecido") };
        var endereco = new EnderecoFormViewModel(api.Servico()) { Cep = "58039000" };

        await endereco.BuscarCepCommand.Execute();

        Assert.Contains("manualmente", endereco.MensagemCepErro);
        Assert.False(endereco.Buscando);
    }

    [Fact]
    public async Task CepMalFormadoNaoChamaOServico()
    {
        var api = new ViaCepFalso();
        var endereco = new EnderecoFormViewModel(api.Servico()) { Cep = "123" };

        await endereco.BuscarCepCommand.Execute();

        Assert.Contains("CEP inválido", endereco.MensagemCepErro);
        Assert.Empty(api.Consultas);
    }

    [Fact]
    public async Task BuscaBemSucedidaAvisaAoModalParaLevarOFocoAoNumero()
    {
        var endereco = new EnderecoFormViewModel(new ViaCepFalso().Servico()) { Cep = "58039000" };
        var avisos = 0;
        using var assinatura = endereco.CepEncontrado.Subscribe(_ => avisos++);

        await endereco.BuscarCepCommand.Execute();
        endereco.Cep = "99999999";   // uma busca que falha não avisa
        endereco.CidadeUf = "";

        Assert.Equal(1, avisos);
    }

    [Fact]
    public void BotaoBuscarSoHabilitaComCepDigitadoEComOServico()
    {
        var com = new EnderecoFormViewModel(new ViaCepFalso().Servico());
        var sem = new EnderecoFormViewModel(null) { Cep = "58039000" };

        Assert.False(PodeBuscar(com));                    // sem CEP digitado
        com.Cep = "58039000";
        Assert.True(PodeBuscar(com));
        com.Cep = "   ";
        Assert.False(PodeBuscar(com));
        Assert.False(sem.CepDisponivel);
        Assert.True(com.CepDisponivel);
    }

    [Fact]
    public async Task LimparZeraTudoInclusiveOCodigoDaCidade()
    {
        var endereco = new EnderecoFormViewModel(new ViaCepFalso().Servico()) { Cep = "58039000", Numero = "10" };
        await endereco.BuscarCepCommand.Execute();

        endereco.Limpar();

        Assert.Equal(string.Empty, endereco.Cep);
        Assert.Equal(string.Empty, endereco.Logradouro);
        Assert.Equal(string.Empty, endereco.Numero);
        Assert.Equal(string.Empty, endereco.Bairro);
        Assert.Equal(string.Empty, endereco.CidadeUf);
        Assert.Equal(string.Empty, endereco.CodigoCidadeParaSalvar);
        Assert.Null(endereco.MensagemCep);
    }

    // ---- dentro do modal de cliente (CadastrosViewModel) ----

    [Fact]
    public async Task SalvarClienteComEnderecoBuscadoGravaTudoIncluindoOCodigoDaCidade()
    {
        using var fixture = new SqliteInMemoryFixture();
        var cadastros = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto), new ViaCepFalso().Servico());
        await cadastros.IniciarAsync();
        await cadastros.NovoClienteCommand.Execute();
        cadastros.NovoNome = "Ana Lima";
        cadastros.NovoCpfCnpj = CpfValido;
        cadastros.NovoEndereco.Cep = "58039000";
        await cadastros.NovoEndereco.BuscarCepCommand.Execute();
        cadastros.NovoEndereco.Numero = "1200";
        cadastros.NovoEndereco.Complemento = "Sala 3";

        await cadastros.CriarClienteCommand.Execute();

        Assert.False(cadastros.FormularioClienteAberto);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal("58039000", cliente.Cep);
        Assert.Equal("Avenida Epitácio Pessoa", cliente.Endereco);
        Assert.Equal("1200", cliente.Numero);
        Assert.Equal("Sala 3", cliente.Complemento);
        Assert.Equal("Miramar", cliente.Bairro);
        Assert.Equal("João Pessoa", cliente.Cidade);
        Assert.Equal("PB", cliente.Uf);
        Assert.Equal("2507507", cliente.CodigoCidade);
    }

    [Fact]
    public async Task SalvarComACidadeTrocadaAMaoGravaOEnderecoMasSemOCodigo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var cadastros = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto), new ViaCepFalso().Servico());
        await cadastros.NovoClienteCommand.Execute();
        cadastros.NovoNome = "Ana Lima";
        cadastros.NovoCpfCnpj = CpfValido;
        cadastros.NovoEndereco.Cep = "58039000";
        await cadastros.NovoEndereco.BuscarCepCommand.Execute();
        cadastros.NovoEndereco.CidadeUf = "Campina Grande - PB";

        await cadastros.CriarClienteCommand.Execute();

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal("Campina Grande", cliente.Cidade);
        Assert.Null(cliente.CodigoCidade);                 // o código era de João Pessoa
    }

    [Fact]
    public async Task CancelarOuSalvarLimpaOEnderecoParaAProximaAbertura()
    {
        using var fixture = new SqliteInMemoryFixture();
        var cadastros = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto), new ViaCepFalso().Servico());
        await cadastros.NovoClienteCommand.Execute();
        cadastros.NovoEndereco.Cep = "58039000";
        await cadastros.NovoEndereco.BuscarCepCommand.Execute();

        await cadastros.FecharFormularioClienteCommand.Execute();

        Assert.Equal(string.Empty, cadastros.NovoEndereco.Cep);
        Assert.Equal(string.Empty, cadastros.NovoEndereco.Logradouro);
        Assert.Equal(string.Empty, cadastros.NovoEndereco.CodigoCidadeParaSalvar);
    }

    [Fact]
    public async Task ErroDeValidacaoDoEnderecoMantemOModalAbertoComOsCampos()
    {
        using var fixture = new SqliteInMemoryFixture();
        var cadastros = new CadastrosViewModel(new CadastroLocalService(fixture.CriarContexto), new ViaCepFalso().Servico());
        await cadastros.NovoClienteCommand.Execute();
        cadastros.NovoNome = "Ana Lima";
        cadastros.NovoCpfCnpj = CpfValido;
        cadastros.NovoEndereco.Cep = "12345";           // não buscou: CEP curto demais para salvar

        await cadastros.CriarClienteCommand.Execute();

        Assert.True(cadastros.FormularioClienteAberto);
        Assert.Contains("CEP", cadastros.MensagemFormErro);
        Assert.Equal("12345", cadastros.NovoEndereco.Cep);
    }
}

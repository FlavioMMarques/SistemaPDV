using SistemaPDV.Services;

namespace SistemaPDV.Tests;

// Endereço completo do cliente (CEP, logradouro, número, bairro, complemento) e o código IBGE da cidade, gravados só no banco local.
public class CadastroClienteEnderecoTests
{
    private const string CpfValido = "529.982.247-25";

    [Fact]
    public async Task GravaOEnderecoCompletoEOCodigoDaCidade()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados(
            "Ana Lima", CpfValido, CidadeUf: "João Pessoa - PB", Cep: "58039-000", Logradouro: " Avenida Epitácio Pessoa ",
            Numero: "1200", Complemento: "Sala 3", Bairro: "Miramar", CodigoCidade: "2507507"));

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Equal("58039000", cliente.Cep);                       // só dígitos, como a API devolve
        Assert.Equal("Avenida Epitácio Pessoa", cliente.Endereco);
        Assert.Equal("1200", cliente.Numero);
        Assert.Equal("Sala 3", cliente.Complemento);
        Assert.Equal("Miramar", cliente.Bairro);
        Assert.Equal("João Pessoa", cliente.Cidade);
        Assert.Equal("PB", cliente.Uf);
        Assert.Equal("2507507", cliente.CodigoCidade);
    }

    [Fact]
    public async Task EnderecoEmBrancoFicaNulo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, Cep: " ", Logradouro: "", Numero: "  ", Complemento: null, Bairro: " "));

        using var leitura = fixture.CriarContexto();
        var cliente = leitura.Clientes.Single();
        Assert.Null(cliente.Cep);
        Assert.Null(cliente.Endereco);
        Assert.Null(cliente.Numero);
        Assert.Null(cliente.Complemento);
        Assert.Null(cliente.Bairro);
        Assert.Null(cliente.CodigoCidade);
    }

    [Theory]
    [InlineData("5803900")]
    [InlineData("58039-00a")]
    [InlineData("580390000")]
    public async Task CepMalFormadoNaoGravaNada(string cep)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, Cep: cep));

        Assert.False(resultado.Sucesso);
        Assert.Contains("CEP", resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Clientes);
    }

    [Theory]
    [InlineData(151, 1, 1, 1, "endereço")]
    [InlineData(1, 21, 1, 1, "número")]
    [InlineData(1, 1, 81, 1, "complemento")]
    [InlineData(1, 1, 1, 81, "bairro")]
    public async Task CamposDeEnderecoLongosDemaisNaoGravam(int logradouro, int numero, int complemento, int bairro, string campo)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido,
            Logradouro: new string('a', logradouro), Numero: new string('1', numero),
            Complemento: new string('c', complemento), Bairro: new string('b', bairro)));

        Assert.False(resultado.Sucesso);
        Assert.Contains(campo, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Empty(leitura.Clientes);
    }

    // O código IBGE só entra com 7 dígitos E com uma cidade: a API rejeitaria um código pela metade ou sem cidade.
    [Theory]
    [InlineData("João Pessoa - PB", "2507507", "2507507")]
    [InlineData("João Pessoa - PB", "25075", null)]        // tamanho errado
    [InlineData("João Pessoa - PB", "abc", null)]
    [InlineData("João Pessoa - PB", "", null)]
    [InlineData(null, "2507507", null)]                     // sem cidade
    [InlineData("  ", "2507507", null)]
    public async Task CodigoDaCidadeSoValeComSeteDigitosEComCidade(string? cidadeUf, string codigo, string? esperado)
    {
        using var fixture = new SqliteInMemoryFixture();
        var service = new CadastroLocalService(fixture.CriarContexto);

        var resultado = await service.CriarClienteAsync(new NovoClienteDados("Ana", CpfValido, CidadeUf: cidadeUf, CodigoCidade: codigo));

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.Equal(esperado, leitura.Clientes.Single().CodigoCidade);
    }
}

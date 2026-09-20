using SistemaPDV.Services;

namespace SistemaPDV.Tests;

public class ErroApiExtractorTests
{
    [Fact]
    public void ExtraiMensagemDeCampoEspecifico()
    {
        var mensagem = ErroApiExtractor.Extrair("""{ "errors": { "turno": ["O turno é obrigatório."] } }""");

        Assert.Contains("turno", mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtraiMensagemGenericaDeMessage()
    {
        var mensagem = ErroApiExtractor.Extrair("""{ "errors": { "message": ["Já existe caixa aberto para esta data/operador/turno."] } }""");

        Assert.Contains("Já existe caixa aberto", mensagem);
    }

    [Fact]
    public void CombinaVariosCamposNumaSoMensagem()
    {
        var mensagem = ErroApiExtractor.Extrair(
            """{ "errors": { "data_caixa": ["Informe data_caixa."], "message": ["Este caixa já está fechado."] } }""");

        Assert.Contains("Informe data_caixa", mensagem);
        Assert.Contains("já está fechado", mensagem);
    }

    [Fact]
    public void ConteudoSemErrorsDevolveOConteudoInteiroComoFallback()
    {
        var conteudo = """{ "message": "Access token expired." }""";

        Assert.Equal(conteudo, ErroApiExtractor.Extrair(conteudo));
    }

    [Fact]
    public void JsonInvalidoDevolveOConteudoOriginalSemLancarExcecao()
    {
        var conteudo = "isso não é json";

        Assert.Equal(conteudo, ErroApiExtractor.Extrair(conteudo));
    }

    // A resposta da API é dado não confiável: o formato pode mudar, um proxy pode
    // responder HTML no lugar do JSON, um erro de servidor pode despejar um stack
    // trace. Nenhum desses casos pode derrubar a sincronização nem encher o banco.

    [Fact]
    public void ErrorsComValorTextoEmVezDeArrayNaoLancaExcecao()
    {
        var conteudo = """{ "errors": { "message": "texto solto, não é array" } }""";

        var mensagem = Record.Exception(() => ErroApiExtractor.Extrair(conteudo));

        Assert.Null(mensagem);
        Assert.Contains("texto solto", ErroApiExtractor.Extrair(conteudo));
    }

    [Fact]
    public void ErrorsQueNaoEhObjetoNaoLancaExcecao()
    {
        var excecao = Record.Exception(() => ErroApiExtractor.Extrair("""{ "errors": "quebrou" }"""));

        Assert.Null(excecao);
    }

    [Fact]
    public void CorpoGiganteEhTruncadoNoFallback()
    {
        var conteudo = new string('x', 100_000);

        var mensagem = ErroApiExtractor.Extrair(conteudo);

        Assert.True(mensagem.Length <= ErroApiExtractor.TamanhoMaximo);
    }

    [Fact]
    public void MensagensDeCamposTambemSaoTruncadas()
    {
        var longa = new string('y', 5_000);

        var mensagem = ErroApiExtractor.Extrair($$"""{ "errors": { "campo": ["{{longa}}"] } }""");

        Assert.True(mensagem.Length <= ErroApiExtractor.TamanhoMaximo);
    }

    [Fact]
    public void IncluiONomeDoCampoParaQuemLeSaberOQueFalta()
    {
        // Resposta REAL da API de vendas (422): sem o nome do campo, a tela mostrava
        // "É obrigatório. É obrigatório. É obrigatório. É obrigatório." — indiagnosticável.
        var conteudo = """
            {"errors":{"numero_documento":["É obrigatório."],"cancelada":["É obrigatório."],
            "bloqueada":["É obrigatório."],"produtos.0.produto_empresa_grade_id":["É obrigatório."]}}
            """;

        var mensagem = ErroApiExtractor.Extrair(conteudo);

        Assert.Contains("numero_documento: É obrigatório.", mensagem);
        Assert.Contains("cancelada: É obrigatório.", mensagem);
        Assert.Contains("bloqueada: É obrigatório.", mensagem);
        Assert.Contains("produtos.0.produto_empresa_grade_id: É obrigatório.", mensagem);
    }

    [Fact]
    public void MensagemGeralSemCampoNaoLevaPrefixo()
    {
        var mensagem = ErroApiExtractor.Extrair("""{ "errors": { "message": ["Já existe caixa aberto."] } }""");

        Assert.Equal("Já existe caixa aberto.", mensagem);
    }
}

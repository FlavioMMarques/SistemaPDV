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
}

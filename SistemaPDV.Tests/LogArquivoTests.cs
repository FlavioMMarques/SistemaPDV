using SistemaPDV.Services;

namespace SistemaPDV.Tests;

// O log é o que diagnostica um PDV em campo — mas também é o lugar mais fácil de vazar segredo (mensagens de erro da
// API trazem o corpo da resposta) e de derrubar o app por engano (disco cheio, arquivo travado). Estes testes
// travam as duas garantias: nunca grava segredo/dado pessoal e nunca lança.
public class LogArquivoTests : IDisposable
{
    private readonly string pasta = Path.Combine(Path.GetTempPath(), "SistemaPDV.Tests.Log", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(pasta, recursive: true); } catch (Exception) { /* limpeza de teste */ }
    }

    private string LerTudo() => string.Join(Environment.NewLine, Directory.GetFiles(pasta, "pdv-*.log").Select(File.ReadAllText));

    // ---- gravação ----

    [Fact]
    public void GravaLinhaComHoraNivelOrigemEMensagemNoArquivoDoDia()
    {
        var log = new LogArquivo(pasta, new RelogioFalso());

        log.Registrar(NivelLog.Aviso, "Envio", "Venda não foi aceita");

        var arquivo = Assert.Single(Directory.GetFiles(pasta));
        Assert.EndsWith("pdv-20260920.log", arquivo);
        var texto = File.ReadAllText(arquivo);
        Assert.Contains("[AVISO] Envio: Venda não foi aceita", texto);
    }

    [Fact]
    public void ExcecaoVaiJuntoComTipoEMensagem()
    {
        var log = new LogArquivo(pasta);

        log.Registrar(NivelLog.Erro, "Sincronização", "Ciclo falhou", new InvalidOperationException("banco travado"));

        var texto = LerTudo();
        Assert.Contains("[ERRO]", texto);
        Assert.Contains("InvalidOperationException", texto);
        Assert.Contains("banco travado", texto);
    }

    [Fact]
    public void VariasChamadasAcumulamNoMesmoArquivo()
    {
        var log = new LogArquivo(pasta);

        log.Registrar(NivelLog.Info, "App", "um");
        log.Registrar(NivelLog.Info, "App", "dois");

        var linhas = LerTudo().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, linhas.Length);
    }

    [Fact]
    public async Task GravacoesEmParaleloNaoPerdemNemMisturamLinhas()
    {
        var log = new LogArquivo(pasta);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => log.Registrar(NivelLog.Info, "Teste", $"linha-{i}"))));

        var linhas = LerTudo().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(200, linhas.Length);
        Assert.All(linhas, l => Assert.Matches(@"^\d{4}-\d{2}-\d{2} .* \[INFO\] Teste: linha-\d+$", l));
    }

    // ---- nunca lança ----

    [Fact]
    public void PastaImpossivelDeCriarNaoLanca()
    {
        // Um ARQUIVO no lugar da pasta: Directory.CreateDirectory falha.
        Directory.CreateDirectory(Path.GetDirectoryName(pasta)!);
        File.WriteAllText(pasta, "sou um arquivo");
        var log = new LogArquivo(pasta);

        var excecao = Record.Exception(() => log.Registrar(NivelLog.Erro, "App", "não vai gravar"));

        Assert.Null(excecao);
    }

    [Fact]
    public void RegistroSemDestinoEhNoOpEnaoLanca()
    {
        var anterior = Registro.Destino;
        try
        {
            Registro.Destino = null;
            var excecao = Record.Exception(() =>
            {
                Registro.Info("x", "y");
                Registro.Aviso("x", "y");
                Registro.Erro("x", "y", new Exception("z"));
            });
            Assert.Null(excecao);
        }
        finally { Registro.Destino = anterior; }
    }

    // ---- limites ----

    [Fact]
    public void AoPassarDoLimiteDoDiaAvisaUmaVezEParaDeGravar()
    {
        var log = new LogArquivo(pasta, limiteBytesPorArquivo: 300);

        for (var i = 0; i < 50; i++)
            log.Registrar(NivelLog.Info, "Teste", $"mensagem número {i} com algum texto para encher o arquivo");

        var texto = LerTudo();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(texto, "limite de tamanho"));
        Assert.True(new FileInfo(Directory.GetFiles(pasta).Single()).Length < 1500, "o arquivo não pode crescer sem limite");
    }

    [Fact]
    public void MensagemGiganteEhCortada()
    {
        var log = new LogArquivo(pasta);

        log.Registrar(NivelLog.Erro, "Teste", new string('x', 100_000));

        Assert.True(LerTudo().Length < 5000);
        Assert.Contains("cortado", LerTudo());
    }

    [Fact]
    public void ArquivosMaisAntigosQueARetencaoSaoApagadosNaAbertura()
    {
        Directory.CreateDirectory(pasta);
        var velho = Path.Combine(pasta, "pdv-20260101.log");
        var recente = Path.Combine(pasta, "pdv-20260919.log");
        File.WriteAllText(velho, "velho");
        File.WriteAllText(recente, "recente");
        File.SetLastWriteTime(velho, new DateTime(2026, 1, 1));
        File.SetLastWriteTime(recente, new DateTime(2026, 9, 19));

        _ = new LogArquivo(pasta, new RelogioFalso(), diasDeRetencao: 14);

        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(recente));
    }

    // ---- máscara de segredos e dados pessoais ----

    [Theory]
    [InlineData("""{"access_token":"abc123SEGREDO","expires_in":3600}""", "abc123SEGREDO")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.assinatura", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("client_secret=super-secreto-123&client_id=1", "super-secreto-123")]
    [InlineData("""{"pdv_key":"1234","nome":"Carlos"}""", "1234")]
    [InlineData("hash $2y$10$abcdefghijklmnopqrstuuABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ab gravado", "abcdefghijklmnopqrstuu")]
    [InlineData("CPF inválido: 123.456.789-09", "123.456.789-09")]
    [InlineData("cpf 12345678909 duplicado", "12345678909")]
    [InlineData("CNPJ 06.220.266/0001-26 não encontrado", "06.220.266/0001-26")]
    [InlineData("cnpj 06220266000126", "06220266000126")]
    public void MascaraSegredosEDadosPessoais(string original, string naoPodeAparecer)
    {
        var mascarado = LogArquivo.Mascarar(original);

        Assert.DoesNotContain(naoPodeAparecer, mascarado);
        Assert.Contains("***", mascarado);
    }

    [Fact]
    public void MantemONomeDoCampoParaOLogFazerSentido()
    {
        Assert.Equal("""{"access_token":"***","expires_in":3600}""", LogArquivo.Mascarar("""{"access_token":"abc","expires_in":3600}"""));
    }

    [Fact]
    public void TextoSemSegredoNaoMudaESemNumerosCurtosSaoPreservados()
    {
        const string texto = "422: quantidade: deve ser maior que 0 (venda 42, 3 itens, R$ 19,80)";

        Assert.Equal(texto, LogArquivo.Mascarar(texto));
    }

    // ---- ganchos ----

    [Fact]
    public async Task FalhaDeEnvioNoOutboxVaiParaOLogSemDadoPessoal()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using var context = fixture.CriarContexto();
        var cliente = new SistemaPDV.Models.Cliente { Nome = "Maria" };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var anterior = Registro.Destino;
        try
        {
            Registro.Destino = new LogArquivo(pasta);
            await OutboxHelper.MarcarFalhaAsync(context, cliente, "422 cpf: 123.456.789-09 inválido", (c, m) => c.UltimoErroSync = m, default);
        }
        finally { Registro.Destino = anterior; }

        var texto = LerTudo();
        Assert.Contains("[AVISO] Envio: Falha ao enviar Cliente", texto);
        Assert.DoesNotContain("123.456.789-09", texto);   // o CPF do cliente não vai pro arquivo
    }

    [Theory]
    [InlineData("Access token expired.")]
    [InlineData("""{"message":"Access token expired."}""")]
    [InlineData("Token inválido para este dispositivo")]
    [InlineData("senha incorreta")]
    public void NaoMascaraAPalavraSoPorEstarNaMensagemDeErro(string mensagem)
    {
        // Sem ":" nem "=" não há valor de segredo: mascarar aqui apagaria o diagnóstico mais comum (401 de token expirado).
        Assert.Equal(mensagem, LogArquivo.Mascarar(mensagem));
    }

    [Fact]
    public void QuebraDeLinhaNaMensagemNaoForjaUmaNovaEntradaDoLog()
    {
        // Mensagens de erro vêm da API (dado externo): um "\n" seguido de um cabeçalho falso de log não pode criar
        // uma linha que pareça uma entrada legítima.
        var log = new LogArquivo(pasta);

        log.Registrar(NivelLog.Erro, "Envio", "422 recusada\n2026-01-01 00:00:00.000 [INFO] App: tudo certo, nada a ver aqui");

        var linhas = LerTudo().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, linhas.Length);
        Assert.StartsWith("    ", linhas[1]);   // continuação recuada, não uma entrada nova
    }

    [Fact]
    public void SegredoNoMeioDeUmaExcecaoNaoVazaParaOArquivo()
    {
        var log = new LogArquivo(pasta);

        log.Registrar(NivelLog.Erro, "Auth", "falha", new HttpRequestException("resposta: {\"client_secret\":\"VAZOU-ISSO\"}"));

        Assert.DoesNotContain("VAZOU-ISSO", LerTudo());
    }
}

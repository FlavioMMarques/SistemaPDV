using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SistemaPDV.Services;

public enum NivelLog
{
    Info,
    Aviso,
    Erro,
}

// Log em arquivo (um por dia: pdv-AAAAMMDD.log). Existe porque muita falha é engolida de propósito — um cliente que
// falha não pode travar o lote de envio, a exceção de um comando vira só o banner — e sem registro ninguém descobre o
// que aconteceu num PDV em campo. Regras:
//   - NUNCA lança: o log não pode virar a causa de um crash na hora da venda (disco cheio, arquivo aberto por outro
//     programa, pasta sem permissão — tudo é engolido);
//   - NUNCA grava segredo nem dado pessoal: token, client_secret, pdv_key, hash bcrypt e CPF/CNPJ são mascarados
//     (ver Mascarar) antes de ir pro arquivo — mensagens de erro da API podem trazer o corpo da resposta;
//   - tem limite: cada dia para de gravar ao passar de LimiteBytesPorArquivo e arquivos com mais de DiasDeRetencao
//     são apagados na abertura — o log não pode encher o disco de um PDV.
public class LogArquivo
{
    public const long LimitePadraoBytesPorArquivo = 5 * 1024 * 1024;
    public const int DiasDeRetencaoPadrao = 14;
    private const int TamanhoMaximoMensagem = 4000;

    private readonly string pasta;
    private readonly TimeProvider tempo;
    private readonly long limiteBytesPorArquivo;
    private readonly object trava = new();
    private string? arquivoCheio;   // dia (nome do arquivo) que já passou do limite e ganhou o aviso de "log cheio"

    public LogArquivo(
        string pasta,
        TimeProvider? tempo = null,
        long limiteBytesPorArquivo = LimitePadraoBytesPorArquivo,
        int diasDeRetencao = DiasDeRetencaoPadrao)
    {
        this.pasta = pasta;
        this.tempo = tempo ?? TimeProvider.System;
        this.limiteBytesPorArquivo = limiteBytesPorArquivo;
        LimparAntigos(diasDeRetencao);
    }

    public string Pasta => pasta;

    public void Registrar(NivelLog nivel, string origem, string mensagem, Exception? excecao = null)
    {
        try
        {
            var agora = tempo.GetLocalNow();
            var texto = new StringBuilder()
                .Append(agora.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(nivel switch { NivelLog.Erro => "ERRO", NivelLog.Aviso => "AVISO", _ => "INFO" }).Append("] ")
                .Append(origem).Append(": ").Append(Limitar(Mascarar(mensagem)));

            if (excecao is not null)
                texto.AppendLine().Append("    ").Append(Limitar(Mascarar(excecao.ToString())).Replace("\n", "\n    "));

            texto.AppendLine();

            var caminho = Path.Combine(pasta, $"pdv-{agora:yyyyMMdd}.log");
            lock (trava)
            {
                Directory.CreateDirectory(pasta);

                if (new FileInfo(caminho) is { Exists: true } arquivo && arquivo.Length >= limiteBytesPorArquivo)
                {
                    if (arquivoCheio != caminho)
                    {
                        arquivoCheio = caminho;
                        File.AppendAllText(caminho, $"{agora:yyyy-MM-dd HH:mm:ss.fff} [AVISO] Log: limite de tamanho do dia atingido — o resto do dia não será gravado.{Environment.NewLine}", Encoding.UTF8);
                    }
                    return;
                }

                File.AppendAllText(caminho, texto.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Sem ter onde registrar a falha do próprio log — engolir é o comportamento certo.
        }
    }

    private static string Limitar(string texto) =>
        texto.Length > TamanhoMaximoMensagem ? texto[..TamanhoMaximoMensagem] + "…(cortado)" : texto;

    private void LimparAntigos(int dias)
    {
        try
        {
            if (!Directory.Exists(pasta))
                return;

            var limite = tempo.GetLocalNow().DateTime.AddDays(-dias);
            foreach (var arquivo in new DirectoryInfo(pasta).GetFiles("pdv-*.log").Where(a => a.LastWriteTime < limite))
                arquivo.Delete();
        }
        catch (Exception)
        {
            // Não conseguir limpar arquivo velho não pode impedir o app de abrir.
        }
    }

    // ---- máscara de segredos e dados pessoais ----

    private const string Oculto = "***";

    private static readonly Regex[] Padroes =
    {
        // Hash bcrypt inteiro (pdv_key guardada) — antes das regras de chave/valor.
        new(@"\$2[abxy]\$\d{2}\$[./A-Za-z0-9]{53}", RegexOptions.Compiled),
        // "Bearer <token>" (header Authorization)
        new(@"(?i)Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled),
        // CNPJ (com ou sem pontuação) antes do CPF: o CNPJ contém uma sequência que o CPF também casaria.
        new(@"(?<!\d)\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}(?!\d)", RegexOptions.Compiled),
        new(@"(?<!\d)\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?!\d)", RegexOptions.Compiled),
    };

    // chave: valor / "chave":"valor" / chave=valor — o valor some, o nome da chave fica (mostra o que era).
    private static readonly Regex ChaveValor = new(
        @"(?i)(access_token|refresh_token|client_secret|pdv_key|password|senha|secret|authorization|token)([""'\s:=]+)(?:Bearer\s+)?[^\s""',;}&]+",
        RegexOptions.Compiled);

    public static string Mascarar(string texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto;

        var resultado = ChaveValor.Replace(texto, m => m.Groups[1].Value + m.Groups[2].Value + Oculto);
        foreach (var padrao in Padroes)
            resultado = padrao.Replace(resultado, Oculto);
        return resultado;
    }
}

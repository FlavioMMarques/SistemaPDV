namespace SistemaPDV.Services.Sync;

public class ResultadoSincronizacaoRecurso
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public int Quantidade { get; private init; }

    public static ResultadoSincronizacaoRecurso ComSucesso(int quantidade) =>
        new() { Sucesso = true, Quantidade = quantidade };

    public static ResultadoSincronizacaoRecurso ComFalha(string mensagem) =>
        new() { Sucesso = false, Mensagem = mensagem };
}

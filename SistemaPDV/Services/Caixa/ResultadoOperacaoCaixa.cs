namespace SistemaPDV.Services.Caixa;

public class ResultadoOperacaoCaixa<T>
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public T? Valor { get; private init; }

    public static ResultadoOperacaoCaixa<T> ComSucesso(T valor) => new() { Sucesso = true, Valor = valor };
    public static ResultadoOperacaoCaixa<T> ComFalha(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

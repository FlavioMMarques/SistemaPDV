namespace SistemaPDV.Services.Sync;

public class ResultadoSincronizacaoCompleta
{
    public bool AutenticacaoSucesso { get; init; }
    public string? MensagemAutenticacao { get; init; }

    public ResultadoSincronizacaoRecurso? FormasPagamento { get; init; }
    public ResultadoSincronizacaoRecurso? Clientes { get; init; }
    public ResultadoSincronizacaoRecurso? Produtos { get; init; }
    public ResultadoSincronizacaoRecurso? Funcionarios { get; init; }
    public ResultadoSincronizacaoRecurso? Empresa { get; init; }
    public ResultadoSincronizacaoRecurso? Cartoes { get; init; }
    public ResultadoSincronizacaoRecurso? Grupos { get; init; }

    public bool TudoComSucesso =>
        AutenticacaoSucesso &&
        (FormasPagamento?.Sucesso ?? false) &&
        (Clientes?.Sucesso ?? false) &&
        (Produtos?.Sucesso ?? false) &&
        (Funcionarios?.Sucesso ?? false) &&
        (Empresa?.Sucesso ?? false) &&
        (Cartoes?.Sucesso ?? false) &&
        (Grupos?.Sucesso ?? false);
}

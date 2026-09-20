using SistemaPDV.Models;

namespace SistemaPDV.Services;

// Linhas de leitura da tela de Cadastros — projeção enxuta em vez da entidade
// inteira (Cliente tem ~40 campos), mesmo padrão de VendaResumo.
public record ClienteResumo(int Id, string Nome, string? Documento, SyncStatus SyncStatus, string? UltimoErroSync);

public record ProdutoResumo(int Id, string Nome, string? CodigoBarras, decimal PrecoVenda, int EstoqueAtual, SyncStatus SyncStatus);

public class ResultadoCriacaoCliente
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public int? ClienteId { get; private init; }

    public static ResultadoCriacaoCliente ComSucesso(int clienteId) => new() { Sucesso = true, ClienteId = clienteId };
    public static ResultadoCriacaoCliente ComFalha(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

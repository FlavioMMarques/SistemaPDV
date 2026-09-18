namespace SistemaPDV.Models;

public class Funcionario : ISincronizavel<int>
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendenteSync;

    public required string Nome { get; set; }
    public string? Cpf { get; set; }
    public bool Supervisor { get; set; }
    public bool Desativado { get; set; }

    // Hash (SHA-256) do pdv_key sincronizado da API — nunca o valor em claro.
    // Login local compara hash contra hash, nunca recupera o original.
    public required string PdvKeyHash { get; set; }
}

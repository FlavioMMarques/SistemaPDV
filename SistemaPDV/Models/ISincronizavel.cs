namespace SistemaPDV.Models;

public interface ISincronizavel<TKey>
{
    TKey Id { get; }
    int? IdExterno { get; set; }
    SyncStatus SyncStatus { get; set; }
}

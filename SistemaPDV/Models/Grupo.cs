namespace SistemaPDV.Models;

// Grupo de produtos do SoftcomShop (GET .../produtos/grupos): a "categoria" que o produto aponta por Produto.GrupoId (ex:
// "Mercearia", "Bebidas"). Só o nome interessa ao app — mostrado no card do PDV e na tela de Cadastros. Leitura pura, como
// os cartões e as formas de pagamento: nada disto volta à API.
public class Grupo : ISincronizavel<int>
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Sincronizado;

    public string Nome { get; set; } = string.Empty;
}

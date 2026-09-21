namespace SistemaPDV.Models;

// Cartão cadastrado no SoftcomShop (GET .../financeiros/cartoes): uma combinação credenciadora × bandeira × tipo
// (crédito/débito) × parcelas. A BANDEIRA é atributo do cartão, não um catálogo à parte — a lista de bandeiras do app é
// o conjunto dos BandeiraNome distintos daqui (ver CatalogoLocalService.ListarBandeirasAsync).
//
// Nada disto é enviado de volta à API: é só leitura (catálogo), como formas de pagamento. Sem o CNPJ da credenciadora
// de propósito — o app não precisa e é um dado a menos para guardar/vazar.
public class Cartao : ISincronizavel<int>
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Sincronizado;

    public string Credenciadora { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;

    // A API manda tudo como texto e a bandeira com zero à esquerda ("02"): é um CÓDIGO, não um número.
    public string BandeiraId { get; set; } = string.Empty;
    public string BandeiraNome { get; set; } = string.Empty;

    public string Tipo { get; set; } = string.Empty;   // CREDITO, DEBITO…
    public string AliasCartao { get; set; } = string.Empty;
    public int? Dia { get; set; }
    public int? Parcelas { get; set; }
    public decimal? TaxaAdministrativa { get; set; }
}

namespace SistemaPDV.Models;

public class FormaPagamento : ISincronizavel<int>
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendenteSync;

    public required string Nome { get; set; }
    public required string Tipo { get; set; }
    public bool Padrao { get; set; }
    public string? CodigoNfce { get; set; }
    public string? CodigoTransacaoSitef { get; set; }
    public bool CarteiraDigital { get; set; }
    public int Ordem { get; set; }
    public bool PdvPos { get; set; }
    public bool PreVenda { get; set; }
    public string? AtalhoNumero { get; set; }
    public bool PermissaoSupervisor { get; set; }

    // Forma que exige escolher a bandeira (crédito e débito chegam da API com Tipo = "CARTAO"; aceita também variações como
    // "CARTAO_CREDITO"). "CARTEIRA_DIGITAL", PIX, dinheiro etc. NÃO são cartão: por isso StartsWith, e não Contains("CART").
    // Só leitura: o EF não mapeia.
    public bool EhCartao => Tipo.StartsWith("CARTAO", System.StringComparison.OrdinalIgnoreCase);

    // Dinheiro: a única forma que dá TROCO (o cliente entrega mais do que o valor lançado). Pelo código da NFC-e "01"
    // (dinheiro) e NÃO pelo Tipo: na API real o PIX manual ("PIX OFF", código 20) também vem com Tipo = "ESPECIE". Sem
    // código, cai no Tipo "ESPECIE". Só leitura: o EF não mapeia.
    public bool EhDinheiro => CodigoNfce == "01"
        || (string.IsNullOrWhiteSpace(CodigoNfce) && string.Equals(Tipo, "ESPECIE", System.StringComparison.OrdinalIgnoreCase));

    public bool EhPix => Nome.Contains("PIX", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(Tipo, "CARTEIRA_DIGITAL", System.StringComparison.OrdinalIgnoreCase);

    // Débito × crédito (os dois têm Tipo "CARTAO"): código da NFC-e "04" é débito; sem código, pelo nome.
    public bool EhDebito => CodigoNfce == "04" || Nome.Contains("DÉBITO", System.StringComparison.OrdinalIgnoreCase)
        || Nome.Contains("DEBITO", System.StringComparison.OrdinalIgnoreCase);
}

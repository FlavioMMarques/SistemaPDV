using ReactiveUI;
using SistemaPDV.Models;

namespace SistemaPDV.ViewModels;

// Um "cartão" do painel de pagamento: ícone, título e explicação de uma forma de pagamento (como no protótipo: "Pix
// Instantâneo — Chave dinâmica / QR Code"). Só apresentação: quem decide o que cada forma faz é a FormaPagamento.
public class OpcaoPagamento : ReactiveObject
{
    private bool selecionada;

    public OpcaoPagamento(FormaPagamento forma)
    {
        Forma = forma;
        (Icone, Subtitulo) = Descrever(forma);
    }

    public FormaPagamento Forma { get; }
    public string Titulo => Forma.Nome;
    public string Icone { get; }
    public string Subtitulo { get; }

    // A forma escolhida para o PRÓXIMO lançamento (o cartão fica com a borda amarela).
    public bool Selecionada
    {
        get => selecionada;
        set => this.RaiseAndSetIfChanged(ref selecionada, value);
    }

    private static (string Icone, string Subtitulo) Descrever(FormaPagamento forma)
    {
        if (forma.EhDinheiro)
            return ("💵", "Cálculo automático de troco");
        if (forma.EhCartao)
            return forma.EhDebito ? ("🏦", "Débito à vista") : ("💳", "Crédito • maquininha");
        if (forma.EhPix)
            return ("⚡", "Chave dinâmica / QR Code");

        return ("🧾", forma.Tipo.Length == 0 ? "Outra forma" : Capitalizar(forma.Tipo));
    }

    private static string Capitalizar(string texto) =>
        char.ToUpperInvariant(texto[0]) + texto[1..].ToLowerInvariant().Replace('_', ' ');
}

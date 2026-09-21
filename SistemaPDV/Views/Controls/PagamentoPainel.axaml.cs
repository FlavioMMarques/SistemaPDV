using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SistemaPDV.Views.Controls;

// Painel de pagamento da tela de venda. Único código aqui: FOCO de teclado (só apresentação — mesma exceção já feita no
// F4 do PdvView). Sem isso, ao abrir o painel com F10 o foco ficava num controle que acabou de ser desabilitado (o corpo
// da venda fica desabilitado atrás do modal) e o foco se perdia: F10/Esc não chegavam mais ao PdvView e o operador
// precisava clicar com o mouse. Agora o foco vai para a 1ª forma de pagamento ao abrir e para o campo de valor assim que
// uma forma é escolhida.
public partial class PagamentoPainel : UserControl
{
    public PagamentoPainel()
    {
        InitializeComponent();

        var campo = this.FindControl<TextBox>("ValorPagamentoTextBox")!;
        // A área de lançamento só aparece quando uma forma é escolhida: é nesse momento que o campo de valor recebe o foco.
        var area = this.FindControl<Border>("AreaLancamento")!;
        area.GetObservable(IsVisibleProperty).Subscribe(visivel =>
        {
            if (visivel)
                Dispatcher.UIThread.Post(() => { campo.Focus(); campo.SelectAll(); }, DispatcherPriority.Input);
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.NewValue is true)
            FocarPrimeiraForma(tentativasRestantes: 8);
    }

    // Os cartões das formas só existem depois do 1º layout do painel visível: se ainda não há botão, tenta de novo logo
    // depois (poucas vezes — não fica em laço se a lista de formas estiver vazia).
    private void FocarPrimeiraForma(int tentativasRestantes) =>
        Dispatcher.UIThread.Post(() =>
        {
            var primeira = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(botao => botao.Classes.Contains("opcao") && botao.IsEffectivelyEnabled);

            if (primeira is not null)
                primeira.Focus();
            else if (tentativasRestantes > 0)
                FocarPrimeiraForma(tentativasRestantes - 1);
        }, DispatcherPriority.Background);
}

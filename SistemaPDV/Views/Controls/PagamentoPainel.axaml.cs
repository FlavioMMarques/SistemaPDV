using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SistemaPDV.Views.Controls;

// Painel de pagamento da tela de venda. Único código aqui: levar o foco pro campo de valor quando o painel aparece (foco
// de teclado é só apresentação — mesma exceção já feita no F4 do PdvView), pra o operador poder digitar/confirmar sem mouse.
public partial class PagamentoPainel : UserControl
{
    public PagamentoPainel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.NewValue is true)
            Dispatcher.UIThread.Post(() =>
            {
                var campo = this.FindControl<TextBox>("ValorPagamentoTextBox");
                campo?.Focus();
                campo?.SelectAll();
            }, DispatcherPriority.Input);
    }
}

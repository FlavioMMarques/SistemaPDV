using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SistemaPDV.Views.Controls;

// Painel "Informar preço". Único código aqui: FOCO de teclado (só apresentação — mesma exceção do PagamentoPainel). O corpo da
// venda fica desabilitado atrás do painel; ao abrir, o foco vai para o campo do preço (já com o cursor pronto para digitar,
// como o leitor de código de barras deixou o operador), senão o foco se perderia num controle desabilitado. Ao fechar, a tela
// de venda devolve o foco à busca (ver PdvView).
public partial class PrecoProdutoPainel : UserControl
{
    public PrecoProdutoPainel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.NewValue is true)
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("PrecoTextBox")?.Focus(), DispatcherPriority.Background);
    }
}

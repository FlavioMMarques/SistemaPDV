using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SistemaPDV.Views.Controls;

// Modal "Cadastrar Produto". Único código aqui: FOCO de teclado (só apresentação — mesma exceção do ClienteFormPainel). A tela de
// trás fica desabilitada atrás do modal; ao abrir, o foco vai para o campo do nome, senão se perderia num controle desabilitado.
public partial class ProdutoFormPainel : UserControl
{
    public ProdutoFormPainel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.NewValue is true)
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("NomeProdutoTextBox")?.Focus(), DispatcherPriority.Background);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SistemaPDV.Views.Controls;

// Modal "Cadastrar Cliente". Único código aqui: FOCO de teclado (só apresentação — mesma exceção do PrecoProdutoPainel). A tela de
// trás fica desabilitada atrás do modal; ao abrir, o foco vai para o campo do nome, senão se perderia num controle desabilitado.
public partial class ClienteFormPainel : UserControl
{
    public ClienteFormPainel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.NewValue is true)
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("NomeTextBox")?.Focus(), DispatcherPriority.Background);
    }
}

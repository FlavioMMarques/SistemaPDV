using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SistemaPDV.Views.Controls;

// Painel lateral da fila outbox. Único código aqui: FOCO de teclado (só apresentação — mesma exceção do PagamentoPainel e do
// DetalhePedidoPainel). A tela de trás fica desabilitada enquanto o painel está aberto; ao abrir, o foco vai para o botão
// "Disparar Sincronização Agora" (a ação principal), senão se perderia num controle desabilitado.
public partial class PainelFilaOutbox : UserControl
{
    public PainelFilaOutbox()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.NewValue is true)
            Dispatcher.UIThread.Post(() => this.FindControl<Button>("SincronizarButton")?.Focus(), DispatcherPriority.Background);
    }
}

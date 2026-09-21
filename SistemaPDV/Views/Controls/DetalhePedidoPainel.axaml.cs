using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SistemaPDV.Views.Controls;

// Modal "Detalhes do pedido". Único código aqui: FOCO de teclado (só apresentação — mesma exceção do PagamentoPainel). A
// página de trás fica desabilitada enquanto o modal está aberto; sem levar o foco para dentro dele, o foco se perderia num
// controle desabilitado e o Tab/Esc não chegariam ao modal. Ao abrir, o foco vai para o botão Fechar; ao fechar, volta para
// quem o tinha (ver GuardaDeFoco).
public partial class DetalhePedidoPainel : UserControl
{
    private readonly GuardaDeFoco guardaDeFoco = new();

    public DetalhePedidoPainel()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsVisibleProperty)
            return;

        if (change.NewValue is true)
        {
            guardaDeFoco.Guardar();
            FocarFechar();
        }
        else
        {
            guardaDeFoco.Devolver();
        }
    }

    // O painel também pode nascer JÁ aberto (o operador chega pelo "Detalhes" do painel principal): aí ele não "fica
    // visível" depois de criado, então o foco também é pedido quando a tela é montada.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        guardaDeFoco.Ligar(this);

        if (IsVisible)
            FocarFechar();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        guardaDeFoco.Desligar();
        base.OnDetachedFromVisualTree(e);
    }

    private void FocarFechar() =>
        Dispatcher.UIThread.Post(() => this.FindControl<Button>("FecharButton")?.Focus(), DispatcherPriority.Background);
}

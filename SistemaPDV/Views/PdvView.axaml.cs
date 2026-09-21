using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SistemaPDV.Views.Controls;

namespace SistemaPDV.Views;

// Única exceção à regra "code-behind só InitializeComponent()" (junto com AtalhosDeTela): FOCO e TECLADO são puramente de
// apresentação (não mudam estado do ViewModel nem têm regra de negócio), e o Avalonia não tem como declarar isto em XAML.
//
// Os atalhos que são Command (F2, F10, Esc) ficam nos KeyBindings do XAML e valem mesmo sem foco graças ao
// AtalhosDeTela.Ativos; aqui só o F4, que é foco de UI (não é um Command), e o foco inicial na busca.
public partial class PdvView : UserControl, ITelaComAtalhosExtras
{
    private IDisposable? observaPainel;

    public PdvView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Cursor pronto na busca, como no protótipo — e o leitor de código de barras já digita direto nela.
        FocarBusca();

        // Painel de pagamento fechou: devolve o foco à busca (o botão que o tinha acabou de sumir).
        observaPainel = this.FindControl<PagamentoPainel>("PainelPagamento")?.GetObservable(IsVisibleProperty)
            .Subscribe(visivel => { if (!visivel) FocarBusca(); });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        observaPainel?.Dispose();
        observaPainel = null;
        base.OnDetachedFromVisualTree(e);
    }

    public bool TratarAtalho(KeyEventArgs e)
    {
        if (e.Key != Key.F4 || e.KeyModifiers != KeyModifiers.None)
            return false;

        FocarBusca();
        return true;
    }

    // O foco só vale se a tela já foi desenhada: adia um passo (mesmo padrão do painel de pagamento).
    private void FocarBusca() =>
        Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("FiltroProdutoTextBox")?.Focus(), DispatcherPriority.Background);
}

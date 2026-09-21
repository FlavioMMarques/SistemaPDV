using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SistemaPDV.Views.Controls;

namespace SistemaPDV.Views;

// Única exceção à regra "code-behind só InitializeComponent()": FOCO e TECLADO são puramente de apresentação (não mudam
// estado do ViewModel nem têm regra de negócio), e o Avalonia não tem como declarar isto em XAML.
//
// Problema que isto resolve (relato do usuário): F2/F4/F10 só funcionavam depois de clicar num controle da tela. Os
// KeyBindings do XAML só disparam quando ALGUM controle da tela tem o foco, e o foco se perde ao navegar para a tela, ao
// clicar numa área vazia ou quando o painel de pagamento fecha (o botão que tinha o foco some).
public partial class PdvView : UserControl
{
    private TopLevel? janela;
    private IDisposable? observaPainel;

    public PdvView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Reserva de atalhos no nível da JANELA, em BUBBLE: com foco dentro da tela, o KeyBinding do XAML já tratou (Handled)
        // e isto nem age; sem foco, o evento nasce na janela e chega aqui. Bubble (e não Tunnel) de propósito: quem tem o foco
        // e trata a tecla primeiro — ex: o Esc que fecha a lista aberta de um ComboBox — continua mandando, e o Esc não
        // cancela a venda por baixo dos panos.
        janela = TopLevel.GetTopLevel(this);
        janela?.AddHandler(KeyDownEvent, AtalhosSemFoco, RoutingStrategies.Bubble);

        // Cursor pronto na busca, como no protótipo — e o leitor de código de barras já digita direto nela.
        FocarBusca();

        // Painel de pagamento fechou: devolve o foco à busca (o botão que o tinha acabou de sumir).
        observaPainel = this.FindControl<PagamentoPainel>("PainelPagamento")?.GetObservable(IsVisibleProperty)
            .Subscribe(visivel => { if (!visivel) FocarBusca(); });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        janela?.RemoveHandler(KeyDownEvent, AtalhosSemFoco);
        janela = null;
        observaPainel?.Dispose();
        observaPainel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void AtalhosSemFoco(object? remetente, KeyEventArgs e)
    {
        if (e.Handled || !this.IsEffectivelyVisible)
            return;

        // Reaproveita os KeyBindings declarados no XAML (F2 Novo, F10 Avançar, Esc): uma lista só, sem duplicar as teclas.
        foreach (var atalho in KeyBindings)
        {
            if (atalho.Gesture?.Matches(e) == true && atalho.Command?.CanExecute(atalho.CommandParameter) == true)
            {
                atalho.Command.Execute(atalho.CommandParameter);
                e.Handled = true;
                return;
            }
        }

        // F4 é foco de UI puro: não tem Command (não existe "focar este controle" declarativo).
        if (e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.None)
        {
            FocarBusca();
            e.Handled = true;
        }
    }

    // O foco só vale se a tela já foi desenhada: adia um passo (mesmo padrão do painel de pagamento).
    private void FocarBusca() =>
        Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("FiltroProdutoTextBox")?.Focus(), DispatcherPriority.Background);
}

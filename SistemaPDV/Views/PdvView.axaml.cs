using Avalonia.Controls;
using Avalonia.Input;

namespace SistemaPDV.Views;

public partial class PdvView : UserControl
{
    public PdvView()
    {
        InitializeComponent();

        // Única exceção à regra "code-behind só InitializeComponent()": foco de
        // teclado é puramente de apresentação (não muda estado do ViewModel, não
        // tem regra de negócio), e o Avalonia não tem um Command declarativo pra
        // "focar este controle" — não dá pra fazer isso só com KeyBinding no XAML.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.F4)
            {
                this.FindControl<TextBox>("FiltroProdutoTextBox")?.Focus();
                e.Handled = true;
            }
        });
    }
}

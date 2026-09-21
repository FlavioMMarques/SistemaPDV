using System;
using Avalonia;
using Avalonia.Controls;

namespace SistemaPDV.Views;

public partial class ShellView : UserControl
{
    // Abaixo desta largura a barra do topo não cabe inteira: entra o modo compacto (ícones sem rótulo, sem a pílula do sistema).
    private const double LarguraMinimaBarraCompleta = 1500;

    public ShellView()
    {
        InitializeComponent();

        // Única lógica aqui, e só de apresentação (nada de estado do ViewModel): o Avalonia não tem "media query" em XAML,
        // então a largura da janela vira a classe "compacto" que os estilos do ShellView.axaml usam. A classe fica no
        // DockPanel raiz (não no UserControl): os estilos declarados no UserControl só alcançam os DESCENDENTES dele.
        var raiz = this.FindControl<DockPanel>("Raiz")!;
        this.GetObservable(BoundsProperty).Subscribe(limites =>
            raiz.Classes.Set("compacto", limites.Width > 0 && limites.Width < LarguraMinimaBarraCompleta));
    }
}

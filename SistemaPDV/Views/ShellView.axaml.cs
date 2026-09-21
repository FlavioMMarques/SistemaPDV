using System;
using Avalonia;
using Avalonia.Controls;

namespace SistemaPDV.Views;

public partial class ShellView : UserControl
{
    // A barra do topo completa (logo + pílula do sistema + navegação com rótulos + operador + conexão + sync) precisa de ~1900 px.
    // Dois degraus: abaixo de 1900 some a pílula do sistema (~250 px), o que basta até ~1650; abaixo disso somem também os
    // rótulos da navegação (ficam os ícones). Antes era um degrau só, em 1500 — e entre 1500 e 1900 a barra vazava (o operador
    // e o Configurações ficavam cortados).
    private const double LarguraDaBarraCompleta = 1900;
    private const double LarguraComRotulos = 1650;

    public ShellView()
    {
        InitializeComponent();

        // Única lógica aqui, e só de apresentação (nada de estado do ViewModel): o Avalonia não tem "media query" em XAML,
        // então a largura da janela vira as classes "estreito" e "compacto" que os estilos do ShellView.axaml usam. As classes
        // ficam no DockPanel raiz (não no UserControl): os estilos declarados no UserControl só alcançam os DESCENDENTES dele.
        var raiz = this.FindControl<DockPanel>("Raiz")!;
        this.GetObservable(BoundsProperty).Subscribe(limites =>
        {
            raiz.Classes.Set("estreito", limites.Width > 0 && limites.Width < LarguraDaBarraCompleta);
            raiz.Classes.Set("compacto", limites.Width > 0 && limites.Width < LarguraComRotulos);
        });
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Views.Controls;

// Modal "Cadastrar Cliente". Único código aqui: FOCO de teclado (só apresentação — mesma exceção do PrecoProdutoPainel). A tela de
// trás fica desabilitada atrás do modal; ao abrir, o foco vai para o campo do nome, senão se perderia num controle desabilitado. E,
// achado o CEP, o cursor vai para o Número: é o que falta digitar depois que o endereço se preenche sozinho.
public partial class ClienteFormPainel : UserControl
{
    private IDisposable? assinaturaCep;

    public ClienteFormPainel()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        assinaturaCep?.Dispose();
        assinaturaCep = (DataContext as CadastrosViewModel)?.NovoEndereco.CepEncontrado.Subscribe(_ =>
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("NumeroTextBox")?.Focus(), DispatcherPriority.Background));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.NewValue is true)
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("NomeTextBox")?.Focus(), DispatcherPriority.Background);
    }
}

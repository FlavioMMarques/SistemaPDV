using Avalonia.Controls;
using Avalonia.Threading;
using SistemaPDV.Services.Caixa;

namespace SistemaPDV.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();

        // Escolheu o nome: o cursor vai direto para a chave (o próximo passo é sempre digitá-la). Uma lista que não mudou
        // na sincronização nem chega aqui (o ViewModel não troca os itens).
        OperadoresListBox.SelectionChanged += (_, e) =>
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is OperadorLogin)
                Dispatcher.UIThread.Post(() => ChaveTextBox.Focus());   // depois de o campo ser habilitado pelo binding
        };
    }
}

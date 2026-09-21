using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SistemaPDV.Views;

// Painel modal (fila outbox, detalhes do pedido): devolve o foco a quem o tinha antes de o painel abrir.
//
// Sem isso o foco se perdia: a tela de trás fica DESABILITADA enquanto o painel está aberto, o controle focado (a caixa de
// busca do PDV, por exemplo) perde o foco, e ao fechar o painel ninguém o recebia de volta — o leitor de código de barras
// passava a digitar no vazio até o operador clicar na busca.
//
// Por que "acompanhar" o foco em vez de olhar na hora de abrir: ao abrir, a tela de trás já foi desabilitada (a mesma variável
// de estado desabilita a tela e mostra o painel) e o foco já se foi. Então o guarda vai anotando o último controle FORA do
// painel que recebeu o foco. Só apresentação (foco de teclado), como os outros usos de code-behind.
internal sealed class GuardaDeFoco
{
    private TopLevel? janela;
    private Control? painel;
    private InputElement? ultimoForaDoPainel;
    private InputElement? guardado;

    // Chamar quando o painel entra na árvore visual.
    public void Ligar(Control painelObservado)
    {
        Desligar();
        painel = painelObservado;
        janela = TopLevel.GetTopLevel(painelObservado);
        janela?.AddHandler(InputElement.GotFocusEvent, AoReceberFoco, RoutingStrategies.Bubble);
    }

    // Chamar quando o painel sai da árvore visual.
    public void Desligar()
    {
        janela?.RemoveHandler(InputElement.GotFocusEvent, AoReceberFoco);
        janela = null;
        painel = null;
        ultimoForaDoPainel = null;
        guardado = null;
    }

    // Chamar quando o painel ABRE, antes de levar o foco para dentro dele.
    public void Guardar() => guardado = ultimoForaDoPainel;

    // Chamar quando o painel FECHA. Adia um passo: a tela de trás só volta a estar habilitada depois desta mudança.
    public void Devolver()
    {
        var alvo = guardado;
        guardado = null;
        if (alvo is null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            // Se o controle saiu de cena enquanto o painel estava aberto (a tela foi trocada), não há o que devolver.
            if (TopLevel.GetTopLevel(alvo) is not null && alvo.IsEffectivelyEnabled && alvo.IsEffectivelyVisible)
                alvo.Focus();
        }, DispatcherPriority.Background);
    }

    private void AoReceberFoco(object? remetente, FocusChangedEventArgs e)
    {
        if (e.Source is InputElement elemento && painel is not null && !painel.IsVisualAncestorOf(elemento))
            ultimoForaDoPainel = elemento;
    }
}

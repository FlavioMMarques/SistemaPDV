using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SistemaPDV.Views;

// Uma tela que, além dos KeyBindings do XAML, tem atalhos que não são um Command (ex: F4 = "ir para a busca", que é foco de
// UI puro). Devolve true se tratou a tecla.
public interface ITelaComAtalhosExtras
{
    bool TratarAtalho(KeyEventArgs e);
}

// Faz os atalhos de uma tela (F2, F10, Esc…) funcionarem MESMO sem nenhum controle dela com o foco.
//
// O problema: os KeyBindings de um UserControl só disparam quando o evento de tecla sobe de um controle focado dentro dele.
// Ao navegar para a tela, clicar numa área vazia ou fechar um painel (o botão que tinha o foco some), nenhum controle tem o
// foco — a tecla nasce na janela e nunca passa pela tela (relato do usuário: "F2 só funciona depois que clico num item").
//
// A solução: enquanto a tela está na árvore visual, um handler na JANELA (em bubble, só age se a tecla ainda não foi tratada)
// reaproveita os KeyBindings declarados no XAML da própria tela — uma lista só, sem duplicar as teclas. Bubble e não Tunnel
// de propósito: quem tem o foco e já tratou a tecla (o Esc que fecha a lista aberta de um ComboBox) continua mandando.
// É removido ao sair da tela, então o F2 do Painel não vale no PDV e vice-versa.
//
// Uso: views:AtalhosDeTela.Ativos="True" no UserControl da tela.
public sealed class AtalhosDeTela : AvaloniaObject
{
    public static readonly AttachedProperty<bool> AtivosProperty =
        AvaloniaProperty.RegisterAttached<AtalhosDeTela, Control, bool>("Ativos");

    private AtalhosDeTela()
    {
    }

    private sealed class Ligacao
    {
        public TopLevel? Janela;
        public EventHandler<KeyEventArgs>? Handler;
    }

    private static readonly ConditionalWeakTable<Control, Ligacao> ligacoes = new();

    static AtalhosDeTela()
    {
        AtivosProperty.Changed.AddClassHandler<Control>((tela, e) =>
        {
            if (e.NewValue is true)
            {
                tela.AttachedToVisualTree += AoEntrarNaArvore;
                tela.DetachedFromVisualTree += AoSairDaArvore;
                if (TopLevel.GetTopLevel(tela) is not null)
                    Ligar(tela);
            }
            else
            {
                tela.AttachedToVisualTree -= AoEntrarNaArvore;
                tela.DetachedFromVisualTree -= AoSairDaArvore;
                Desligar(tela);
            }
        });
    }

    public static bool GetAtivos(Control tela) => tela.GetValue(AtivosProperty);

    public static void SetAtivos(Control tela, bool valor) => tela.SetValue(AtivosProperty, valor);

    private static void AoEntrarNaArvore(object? remetente, VisualTreeAttachmentEventArgs e)
    {
        if (remetente is Control tela)
            Ligar(tela);
    }

    private static void AoSairDaArvore(object? remetente, VisualTreeAttachmentEventArgs e)
    {
        if (remetente is Control tela)
            Desligar(tela);
    }

    private static void Ligar(Control tela)
    {
        Desligar(tela);

        var janela = TopLevel.GetTopLevel(tela);
        if (janela is null)
            return;

        var ligacao = new Ligacao { Janela = janela, Handler = (_, e) => AoApertarTecla(tela, e) };
        janela.AddHandler(InputElement.KeyDownEvent, ligacao.Handler, RoutingStrategies.Bubble);
        ligacoes.AddOrUpdate(tela, ligacao);
    }

    private static void Desligar(Control tela)
    {
        if (!ligacoes.TryGetValue(tela, out var ligacao))
            return;

        ligacao.Janela?.RemoveHandler(InputElement.KeyDownEvent, ligacao.Handler!);
        ligacoes.Remove(tela);
    }

    private static void AoApertarTecla(Control tela, KeyEventArgs e)
    {
        // Tela desabilitada (um painel modal aberto por cima, como a fila outbox) não responde a atalho: sem isso o Esc daria
        // cancelamento de venda POR BAIXO do painel.
        if (e.Handled || !tela.IsEffectivelyVisible || !tela.IsEffectivelyEnabled)
            return;

        foreach (var atalho in tela.KeyBindings)
        {
            if (atalho.Gesture?.Matches(e) == true && atalho.Command?.CanExecute(atalho.CommandParameter) == true)
            {
                atalho.Command.Execute(atalho.CommandParameter);
                e.Handled = true;
                return;
            }
        }

        if (tela is ITelaComAtalhosExtras extras && extras.TratarAtalho(e))
            e.Handled = true;
    }
}

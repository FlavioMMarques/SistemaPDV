using System;
using System.Reactive;

namespace SistemaPDV.Services;

// Rede de segurança contra crash: quando um ReactiveCommand lança (banco travado ao finalizar a venda, disco
// cheio, qualquer falha inesperada) e ninguém escuta ThrownExceptions, o ReactiveUI entrega a exceção ao
// "exception handler" global — que, sem configuração, faz debugger break + UnhandledErrorException e DERRUBA o
// aplicativo. Num PDV isso é inaceitável no meio de uma venda (Boundary "Nunca" da SPEC-pdv-ui).
//
// O handler precisa ser registrado no builder do ReactiveUI (Program.cs), ANTES de existir qualquer tela — por
// isso é um observador estático que repassa a exceção a um destino definido depois (o App aponta pro banner do
// Shell). O próprio observador nunca lança: o tratamento do erro não pode virar o segundo crash.
public static class TratamentoDeErros
{
    // Definido pelo App quando o Shell existe. null = ninguém a quem avisar (o erro é descartado, mas o app não cai).
    public static Action<Exception>? Destino { get; set; }

    public static IObserver<Exception> Observador { get; } = Observer.Create<Exception>(excecao =>
    {
        try
        {
            Destino?.Invoke(excecao);
        }
        catch (Exception)
        {
            // Sem infraestrutura de log ainda: engolir é melhor que derrubar o app por causa do aviso do erro.
        }
    });
}

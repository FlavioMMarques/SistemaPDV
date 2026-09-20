using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.Tests;

// Comando (ReactiveCommand) que lança e ninguém escuta ThrownExceptions: o ReactiveUI entrega a exceção ao
// handler global, que por padrão DERRUBA o app (debugger break + UnhandledErrorException — já derrubou o
// processo de testes uma vez). Num PDV, um banco travado ao finalizar a venda não pode fechar o app.
[Collection("RxState global")]
public class TratamentoDeErrosTests
{
    [Fact]
    public void ObservadorRepassaAExcecaoAoDestino()
    {
        var recebidas = new List<Exception>();
        TratamentoDeErros.Destino = recebidas.Add;
        try
        {
            TratamentoDeErros.Observador.OnNext(new InvalidOperationException("banco travado"));

            Assert.Equal("banco travado", Assert.Single(recebidas).Message);
        }
        finally { TratamentoDeErros.Destino = null; }
    }

    [Fact]
    public void ObservadorNuncaLancaNemSemDestinoNemComDestinoQueFalha()
    {
        TratamentoDeErros.Destino = null;
        Assert.Null(Record.Exception(() => TratamentoDeErros.Observador.OnNext(new Exception("sem destino"))));

        TratamentoDeErros.Destino = _ => throw new InvalidOperationException("falha ao exibir o erro");
        try
        {
            Assert.Null(Record.Exception(() => TratamentoDeErros.Observador.OnNext(new Exception("destino quebrado"))));
        }
        finally { TratamentoDeErros.Destino = null; }
    }

    [Fact]
    public async Task ExcecaoEmComandoChegaAoObservadorEmVezDeDerrubarOApp()
    {
        // Integração: o observador é o handler global desde a inicialização (ReactiveUiTestInitializer espelha o Program.cs).
        var recebidas = new List<Exception>();
        TratamentoDeErros.Destino = recebidas.Add;
        try
        {
            var comando = ReactiveCommand.CreateFromTask(() => Task.FromException<Unit>(new InvalidOperationException("banco travado")));

            comando.Execute().Subscribe(_ => { }, _ => { });   // quem executa também recebe o erro; o handler é o "resto"

            var limite = DateTime.UtcNow.AddSeconds(5);
            while (recebidas.Count == 0 && DateTime.UtcNow < limite)
                await Task.Delay(10);
            Assert.Contains(recebidas, e => e.Message == "banco travado");
        }
        finally { TratamentoDeErros.Destino = null; }
    }
}

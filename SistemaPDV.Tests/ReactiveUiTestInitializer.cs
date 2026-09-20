using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace SistemaPDV.Tests;

// ReactiveUI 23+ exige inicialização explícita via builder (antes acontecia sozinho
// na primeira chamada estática) — normalmente feito por .UseReactiveUI() no
// AppBuilder do Avalonia (Program.cs), que nunca roda nos testes. Sem isso,
// WhenAnyValue/ObservableForProperty lançam TypeInitializationException. Só
// WithCoreServices (sem WithPlatformServices) porque testes de ViewModel não
// rodam dentro de uma janela Avalonia real — não precisam de scheduler de UI.
internal static class ReactiveUiTestInitializer
{
    [ModuleInitializer]
    public static void Inicializar()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            // Mesmo registro do Program.cs: exceção de comando vai pro TratamentoDeErros em vez de derrubar o processo.
            .WithExceptionHandler(SistemaPDV.Services.TratamentoDeErros.Observador)
            .WithCoreServices()
            .BuildApp();
    }
}

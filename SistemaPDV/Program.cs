using Avalonia;
using ReactiveUI.Avalonia;
using System;
using SistemaPDV.Services;
using System.Runtime.Versioning;

namespace SistemaPDV;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    //
    // [SupportedOSPlatform] aqui só propaga o que já é verdade desde App (Windows-only
    // por causa do DPAPI em SegredoProtector, ver AppServices) — este é o topo real da
    // cadeia de chamada, não tem pra onde mais subir.
    [STAThread]
    [SupportedOSPlatform("windows")]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    [SupportedOSPlatform("windows")]
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(builder => builder.WithExceptionHandler(TratamentoDeErros.Observador));
}

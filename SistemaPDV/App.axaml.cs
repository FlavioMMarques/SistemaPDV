using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SistemaPDV.ViewModels;
using SistemaPDV.Views;

namespace SistemaPDV;

// [SupportedOSPlatform] na classe inteira (não só num método, diferente do padrão
// mais granular usado em CatalogSyncService) porque este é o topo da árvore de
// composição: App constrói AppServices, que exige Windows (DPAPI via
// SegredoProtector) pra sempre, sem exceção — não existe um caminho deste app que
// funcione sem essa dependência.
[SupportedOSPlatform("windows")]
public partial class App : Application
{
    // Instância única, guardada aqui (não estática/global) — Task 42 (ShellViewModel)
    // passa essa referência via construtor pro ViewModel raiz.
    public AppServices Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = new AppServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shellViewModel = new ShellViewModel(
                Services.ConfiguracaoService,
                Services.LoginOperadorService,
                Services.CaixaService,
                Services.DashboardService,
                Services.VendaService,
                Services.CatalogoLocalService);
            desktop.MainWindow = new MainWindow
            {
                DataContext = shellViewModel,
            };

            // Fire-and-forget de propósito: a tela inicial (Configurações/Login) só
            // aparece quando IniciarAsync termina, e a janela não pode ficar travada
            // esperando isso — ShellViewModel.CurrentViewModel é reativo, então a UI
            // atualiza sozinha assim que a decisão de navegação estiver pronta.
            _ = shellViewModel.IniciarAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
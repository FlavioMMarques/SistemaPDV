using System.Runtime.Versioning;
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SistemaPDV.Services;
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
        // Antes de qualquer serviço: se a criação do banco/migração falhar, o motivo já vai pro arquivo.
        IniciarLog();

        Services = new AppServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shellViewModel = new ShellViewModel(
                Services.ConfiguracaoService,
                Services.LoginOperadorService,
                Services.CaixaService,
                Services.DashboardService,
                Services.VendaService,
                Services.CatalogoLocalService,
                Services.VendaLocalService,
                Services.CadastroLocalService,
                Services.CaixasApiService,
                Services.CepService);
            desktop.MainWindow = new MainWindow
            {
                DataContext = shellViewModel,
            };

            // Fire-and-forget de propósito: a tela inicial (Configurações/Login) só
            // aparece quando IniciarAsync termina, e a janela não pode ficar travada
            // esperando isso — ShellViewModel.CurrentViewModel é reativo, então a UI
            // atualiza sozinha assim que a decisão de navegação estiver pronta.
            // Rede de segurança: exceção de comando vira o banner do Shell, não crash (ver TratamentoDeErros).
            TratamentoDeErros.Destino = excecao => Dispatcher.UIThread.Post(() => shellViewModel.ReportarErro(excecao));

            _ = shellViewModel.IniciarAsync();

            // Sincronização automática (30 s outbox / 5 min catálogo). O serviço roda os
            // ciclos fora da thread de UI e publica o estado de conexão de lá — Post leva
            // cada valor de volta pra thread de UI antes de tocar no ShellViewModel.
            var sincronizacao = Services.SincronizacaoBackgroundService;
            sincronizacao.EstadoConexaoAlterada.Subscribe(estado =>
                Dispatcher.UIThread.Post(() => shellViewModel.DefinirConexao(estado, sincronizacao.MensagemUltimoCiclo)));
            sincronizacao.DadosAlterados.Subscribe(_ =>
                Dispatcher.UIThread.Post(shellViewModel.NotificarDadosSincronizados));
            shellViewModel.DispositivoVinculado.Subscribe(_ => sincronizacao.SolicitarAgora());
            // "Sincronizar agora" da listagem de pedidos: roda o ciclo já, sem esperar os 30 s (o serviço serializa os ciclos).
            shellViewModel.SincronizacaoSolicitada.Subscribe(_ => sincronizacao.SolicitarAgora());
            // O painel da fila outbox mostra o que a sincronização fez: as linhas vêm do serviço de fundo e voltam à thread de UI.
            foreach (var atividade in sincronizacao.Log.Recentes.Reverse())
                shellViewModel.AdicionarAtividade(atividade);
            sincronizacao.Log.Novas.Subscribe(atividade =>
                Dispatcher.UIThread.Post(() => shellViewModel.AdicionarAtividade(atividade)));
            // O que o envio está fazendo agora ("Sincronizando… (Vendas)"): o painel da fila mostra e desabilita o botão enquanto dura.
            sincronizacao.AndamentoAlterado.Subscribe(andamento =>
                Dispatcher.UIThread.Post(() => shellViewModel.DefinirAndamento(andamento)));
            sincronizacao.Iniciar();
            desktop.Exit += (_, _) =>
            {
                sincronizacao.Dispose();
                Registro.Info("App", "Aplicativo encerrado");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Log em %LOCALAPPDATA%\SistemaPDV\logs — pasta do usuário (sempre gravável, mesmo com o app instalado em
    // Program Files) e fora do diretório do executável. Falhar aqui só deixa o app sem log: nunca impede de abrir.
    private static void IniciarLog()
    {
        try
        {
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SistemaPDV", "logs");
            Registro.Destino = new LogArquivo(pasta);

            // Só registram (não impedem o crash): uma exceção fatal ou de tarefa esquecida ao menos deixa rastro.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Registro.Erro("App", "Exceção fatal não tratada", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
                Registro.Erro("App", "Exceção de tarefa não observada", e.Exception);

            Registro.Info("App", $"Aplicativo iniciado (versão {typeof(App).Assembly.GetName().Version}).");
        }
        catch (Exception)
        {
            // Sem log, mas o app abre.
        }
    }
}
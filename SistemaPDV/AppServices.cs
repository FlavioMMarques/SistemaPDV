using System;
using System.Net.Http;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;

namespace SistemaPDV;

// Composition root manual (sem container de DI, decisão do usuário — ver
// tasks/plan.md, Fase 5): único lugar do app que monta os serviços de
// catalog-sync/caixa/sales prontos pra uso. Expõe só os serviços já construídos
// (nunca HttpClient/SegredoProtector/Func<AppDbContext> soltos) — ViewModel nunca
// fala com infraestrutura direto, só com o serviço que já sabe o que fazer com ela.
public class AppServices
{
    private readonly HttpClient httpClient = new();

    public ConfiguracaoService ConfiguracaoService { get; }
    public CatalogoLocalService CatalogoLocalService { get; }
    public DashboardService DashboardService { get; }
    public CatalogSyncService CatalogSyncService { get; }
    public LoginOperadorService LoginOperadorService { get; }
    public CaixaService CaixaService { get; }
    public CaixaSyncService CaixaSyncService { get; }
    public VendaService VendaService { get; }
    public VendaSyncService VendaSyncService { get; }
    public VendaLocalService VendaLocalService { get; }
    public CadastroLocalService CadastroLocalService { get; }

    [SupportedOSPlatform("windows")]
    public AppServices(string caminhoBanco = "pdv.db")
    {
        Func<AppDbContext> contextFactory = () =>
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite($"Data Source={caminhoBanco}");
            return new AppDbContext(optionsBuilder.Options);
        };

        // Garante que o arquivo .db existe e está na versão de schema mais recente
        // antes de qualquer tela tentar usá-lo — sem isso, a primeira execução num
        // computador novo não teria nenhuma tabela criada.
        using (var context = contextFactory())
        {
            context.Database.Migrate();
        }

        var apiClient = new SoftcomApiClient(httpClient);
        var segredoProtector = new SegredoProtector();
        var authService = new SoftcomAuthService(httpClient, segredoProtector);

        ConfiguracaoService = new ConfiguracaoService(contextFactory, authService, segredoProtector);
        CatalogoLocalService = new CatalogoLocalService(contextFactory);
        DashboardService = new DashboardService(contextFactory);
        CatalogSyncService = new CatalogSyncService(contextFactory, apiClient, segredoProtector, authService);
        LoginOperadorService = new LoginOperadorService(contextFactory);
        CaixaService = new CaixaService(contextFactory);
        CaixaSyncService = new CaixaSyncService(contextFactory, apiClient);
        VendaService = new VendaService(contextFactory);
        VendaSyncService = new VendaSyncService(contextFactory, apiClient);
        VendaLocalService = new VendaLocalService(contextFactory);
        CadastroLocalService = new CadastroLocalService(contextFactory);
    }
}

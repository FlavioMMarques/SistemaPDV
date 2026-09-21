using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Versioning;
using ReactiveUI;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// O cartão de estado do painel da fila: ícone + título + detalhe (nunca só cor), o que está pendente por tipo, e o "sincronizando agora"
// com o botão desabilitado enquanto dura.
[SupportedOSPlatform("windows")]
public class PainelDaFilaEstadoTests
{
    private static ShellViewModel CriarShell(SqliteInMemoryFixture fixture) => new(
        new ConfiguracaoService(fixture.CriarContexto, new SoftcomAuthService(new HttpClient(), new SegredoProtector()), new SegredoProtector()),
        new LoginOperadorService(fixture.CriarContexto),
        new CaixaService(fixture.CriarContexto),
        new DashboardService(fixture.CriarContexto),
        new VendaService(fixture.CriarContexto),
        new CatalogoLocalService(fixture.CriarContexto),
        new VendaLocalService(fixture.CriarContexto),
        new CadastroLocalService(fixture.CriarContexto));

    private static bool PodeSincronizar(ShellViewModel shell)
    {
        var pode = false;
        shell.SincronizarAgoraCommand.CanExecute.Subscribe(v => pode = v);
        return pode;
    }

    [Fact]
    public void SemNadaPendenteEstaTudoEnviadoESemChips()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);

        Assert.Equal(EstadoDaFila.TudoEnviado, shell.EstadoDaFila);
        Assert.Equal("Tudo enviado", shell.TituloEstadoDaFila);
        Assert.Equal("🟢", shell.IconeEstadoDaFila);
        Assert.Equal("Nenhuma pendência na fila local.", shell.DetalheEstadoDaFila);
        Assert.Empty(shell.ChipsDePendencias);
        Assert.False(shell.Sincronizando);
        Assert.Equal("🔄 Disparar Sincronização Agora", shell.TextoBotaoSincronizar);
        Assert.True(PodeSincronizar(shell));
    }

    [Fact]
    public async Task ComPendenciasMostraOQueEstaEsperandoPorTipoSoOsMaioresQueZero()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Clientes.Add(new Cliente { Nome = "Ana", SyncStatus = SyncStatus.PendenteSync });
            context.Clientes.Add(new Cliente { Nome = "Bia", SyncStatus = SyncStatus.PendenteSync });
            context.Produtos.Add(new Produto { Nome = "Café", SyncStatus = SyncStatus.PendenteSync });
            await context.SaveChangesAsync();
        }
        var shell = CriarShell(fixture);

        shell.NotificarDadosSincronizados();   // recontagem da fila (é o que o ciclo de fundo dispara ao mexer no banco)
        await shell.WhenAnyValue(s => s.PendentesSync).Where(n => n == 3).FirstAsync().Timeout(TimeSpan.FromSeconds(5)).ToTask();

        Assert.Equal(EstadoDaFila.Aguardando, shell.EstadoDaFila);
        Assert.Equal("Aguardando envio", shell.TituloEstadoDaFila);
        Assert.Equal("🟡", shell.IconeEstadoDaFila);
        Assert.Equal("3 itens na fila local.", shell.DetalheEstadoDaFila);
        Assert.Equal(new[] { ("👥", "Clientes", 2), ("📦", "Produtos", 1) },
            shell.ChipsDePendencias.Select(c => (c.Icone, c.Rotulo, c.Quantidade)));           // ordem fixa; caixa e venda (zero) não aparecem
        Assert.Equal(3, shell.Pendencias.Total);
    }

    [Fact]
    public async Task UmItemSoUsaOSingular()
    {
        using var fixture = new SqliteInMemoryFixture();
        await using (var context = fixture.CriarContexto())
        {
            context.Produtos.Add(new Produto { Nome = "Café", SyncStatus = SyncStatus.PendenteSync });
            await context.SaveChangesAsync();
        }
        var shell = CriarShell(fixture);

        shell.NotificarDadosSincronizados();
        await shell.WhenAnyValue(s => s.PendentesSync).Where(n => n == 1).FirstAsync().Timeout(TimeSpan.FromSeconds(5)).ToTask();

        Assert.Equal("1 item na fila local.", shell.DetalheEstadoDaFila);
    }

    [Fact]
    public void SincronizandoMostraAEtapaDesabilitaOBotaoEVoltaAoTerminar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);

        shell.DefinirAndamento(new AndamentoDoEnvio(true, "Vendas"));

        Assert.True(shell.Sincronizando);
        Assert.Equal(EstadoDaFila.Sincronizando, shell.EstadoDaFila);
        Assert.Equal("Sincronizando…", shell.TituloEstadoDaFila);
        Assert.Equal("🔄", shell.IconeEstadoDaFila);
        Assert.Equal("Enviando: Vendas", shell.DetalheEstadoDaFila);
        Assert.Equal("🔄 Sincronizando…", shell.TextoBotaoSincronizar);
        Assert.False(PodeSincronizar(shell));                       // não dá para empilhar um segundo envio por cima

        shell.DefinirAndamento(AndamentoDoEnvio.Ocioso);

        Assert.False(shell.Sincronizando);
        Assert.Equal(EstadoDaFila.TudoEnviado, shell.EstadoDaFila);
        Assert.Equal("🔄 Disparar Sincronização Agora", shell.TextoBotaoSincronizar);
        Assert.True(PodeSincronizar(shell));
    }

    [Fact]
    public void SincronizandoSemEtapaAindaDizPreparando()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);

        shell.DefinirAndamento(new AndamentoDoEnvio(true, null));

        Assert.Equal("Preparando o envio…", shell.DetalheEstadoDaFila);
    }

    [Fact]
    public void SemConexaoDizQueOQueEstaNaFilaSaiQuandoAInternetVoltar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);

        shell.DefinirConexao(EstadoConexao.Offline, "Sem rede");

        Assert.Equal(EstadoDaFila.SemConexao, shell.EstadoDaFila);
        Assert.Equal("Sem conexão", shell.TituloEstadoDaFila);
        Assert.Equal("🔴", shell.IconeEstadoDaFila);
        Assert.Contains("assim que a internet voltar", shell.DetalheEstadoDaFila);

        shell.DefinirConexao(EstadoConexao.Online, null);
        Assert.Equal(EstadoDaFila.TudoEnviado, shell.EstadoDaFila);
    }

    [Fact]
    public void SincronizandoTemPrioridadeSobreSemConexao()
    {
        // O ciclo que está enviando prova que há conexão: o cartão não pode dizer "Sem conexão" no meio de um envio.
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);
        shell.DefinirConexao(EstadoConexao.Offline, "atrasado");

        shell.DefinirAndamento(new AndamentoDoEnvio(true, "Clientes novos"));

        Assert.Equal(EstadoDaFila.Sincronizando, shell.EstadoDaFila);
    }

    [Fact]
    public void AsPropriedadesDoCartaoAvisamATelaQuandoMudam()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = CriarShell(fixture);
        var mudadas = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)shell).PropertyChanged += (_, e) => mudadas.Add(e.PropertyName);

        shell.DefinirAndamento(new AndamentoDoEnvio(true, "Vendas"));

        Assert.Contains(nameof(ShellViewModel.EstadoDaFila), mudadas);
        Assert.Contains(nameof(ShellViewModel.TituloEstadoDaFila), mudadas);
        Assert.Contains(nameof(ShellViewModel.DetalheEstadoDaFila), mudadas);
        Assert.Contains(nameof(ShellViewModel.Sincronizando), mudadas);
        Assert.Contains(nameof(ShellViewModel.TextoBotaoSincronizar), mudadas);
    }
}

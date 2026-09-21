using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.Versioning;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Avisos temporários no canto da tela: a fila (ToastCentral) e o que o Shell publica (conexão caiu/voltou, item lançado).
[SupportedOSPlatform("windows")]
public class ToastsTests
{
    // ---- a fila ----

    [Fact]
    public void AvisoFicaVisivelPeloTempoDadoDepoisEsmaeceESai()
    {
        var relogio = new HistoricalScheduler();
        var central = new ToastCentral(relogio);

        var toast = central.Publicar("Olá", "🔔", duracao: TimeSpan.FromSeconds(3));
        Assert.Single(central.Ativos);
        Assert.False(toast.Saindo);

        relogio.AdvanceBy(TimeSpan.FromSeconds(2.9));
        Assert.False(toast.Saindo);                        // ainda visível

        relogio.AdvanceBy(TimeSpan.FromSeconds(0.2));
        Assert.True(toast.Saindo);                         // esmaecendo (a tela faz a transição)
        Assert.Single(central.Ativos);

        relogio.AdvanceBy(TimeSpan.FromSeconds(0.4));
        Assert.Empty(central.Ativos);                      // saiu da lista
    }

    [Fact]
    public void AvisoDaMesmaChaveSubstituiOAnteriorEONovoNaoSomeCedoDemais()
    {
        var relogio = new HistoricalScheduler();
        var central = new ToastCentral(relogio);

        central.Publicar("Café adicionado", "🔔", chave: "item", duracao: TimeSpan.FromSeconds(2));
        relogio.AdvanceBy(TimeSpan.FromSeconds(1.5));
        var segundo = central.Publicar("Arroz adicionado", "🔔", chave: "item", duracao: TimeSpan.FromSeconds(2));

        Assert.Single(central.Ativos);
        Assert.Equal("Arroz adicionado", central.Ativos[0].Texto);

        relogio.AdvanceBy(TimeSpan.FromSeconds(1));        // o prazo do 1º já passou; o do 2º não
        Assert.Single(central.Ativos);
        Assert.False(segundo.Saindo);
    }

    [Fact]
    public void ComMaisDeQuatroAvisosOMaisAntigoSai()
    {
        var central = new ToastCentral(new HistoricalScheduler());

        for (var i = 1; i <= 6; i++)
            central.Publicar($"Aviso {i}", "🔔");

        Assert.Equal(4, central.Ativos.Count);
        Assert.Equal("Aviso 3", central.Ativos[0].Texto);
        Assert.Equal("Aviso 6", central.Ativos[^1].Texto);
    }

    // ---- conexão ----

    [Fact]
    public async Task ConexaoCairAvisaUmaVezEOMesmoEstadoRepetidoNaoAvisaDeNovo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture, comCaixaAberto: false);

        shell.DefinirConexao(EstadoConexao.Online, "ok");
        Assert.Empty(shell.Toasts.Ativos);                 // ficar online de partida não "restaura" nada

        shell.DefinirConexao(EstadoConexao.Offline, "sem rede");
        var aviso = Assert.Single(shell.Toasts.Ativos);
        Assert.Equal(ToastTipo.Erro, aviso.Tipo);
        Assert.Contains("Internet desconectada", aviso.Texto);
        Assert.Contains("100% no banco local", aviso.Texto);

        shell.DefinirConexao(EstadoConexao.Offline, "sem rede");   // o ciclo de 30 s repete o estado
        Assert.Single(shell.Toasts.Ativos);
    }

    [Fact]
    public async Task ConexaoVoltarAvisaERecontaAFilaAvisandoQueEstaVazia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture, comCaixaAberto: false);
        shell.DefinirConexao(EstadoConexao.Offline, "sem rede");

        shell.DefinirConexao(EstadoConexao.Online, "ok");
        await shell.RecontagemAposReconexao;

        Assert.Contains(shell.Toasts.Ativos, t => t.Texto.Contains("restaurada") && t.Tipo == ToastTipo.Sucesso);
        Assert.Contains(shell.Toasts.Ativos, t => t.Texto.Contains("Nenhuma pendência na fila local"));
        Assert.DoesNotContain(shell.Toasts.Ativos, t => t.Texto.Contains("desconectada"));   // o aviso de queda foi substituído
    }

    [Fact]
    public async Task ConexaoVoltarComPendenciasNaoDizQueAFilaEstaVazia()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture, comCaixaAberto: true);   // o caixa local ainda não subiu
        shell.DefinirConexao(EstadoConexao.Offline, "sem rede");

        shell.DefinirConexao(EstadoConexao.OnlineComFalhas, "algo falhou");
        await shell.RecontagemAposReconexao;

        Assert.True(shell.PendentesSync > 0);
        Assert.Contains(shell.Toasts.Ativos, t => t.Texto.Contains("restaurada"));
        Assert.DoesNotContain(shell.Toasts.Ativos, t => t.Texto.Contains("Nenhuma pendência"));
    }

    // ---- item lançado ----

    [Fact]
    public async Task LancarItemAvisaComONomeEOProximoSubstituiOAviso()
    {
        using var fixture = new SqliteInMemoryFixture();
        var shell = await ShellBarraTopoTests.CriarShellLogadoAsync(fixture);
        await shell.IrParaPdvCommand.Execute();
        var pdv = (PdvViewModel)shell.CurrentViewModel!;

        pdv.AdicionarItem(new Produto { Nome = "Sabonete", PrecoVenda = 3.20m });
        var aviso = Assert.Single(shell.Toasts.Ativos);
        Assert.Equal("\"Sabonete\" adicionado ao cupom.", aviso.Texto);

        pdv.AdicionarItem(new Produto { Nome = "Café", PrecoVenda = 16.50m });
        aviso = Assert.Single(shell.Toasts.Ativos);        // não empilha: quem bipa 10 produtos vê o último
        Assert.Equal("\"Café\" adicionado ao cupom.", aviso.Texto);
    }
}

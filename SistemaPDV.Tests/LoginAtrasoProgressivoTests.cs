using System.Reactive.Linq;
using SistemaPDV.Models;
using SistemaPDV.Services;
using SistemaPDV.Services.Caixa;
using SistemaPDV.ViewModels;

namespace SistemaPDV.Tests;

// Erros seguidos de chave no login: as primeiras passam livres (engano de digitação), depois o app exige uma espera que
// dobra a cada erro (5 s … 5 min). Durante a espera nem a chave certa é conferida.
public class LoginAtrasoProgressivoTests
{
    // ---- a regra (LimitadorDeTentativas) ----

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]    // as 3 primeiras falhas são livres
    [InlineData(4, 5)]
    [InlineData(5, 10)]
    [InlineData(6, 20)]
    [InlineData(7, 40)]
    [InlineData(8, 80)]
    [InlineData(9, 160)]
    [InlineData(10, 300)]   // teto de 5 min
    [InlineData(50, 300)]   // e nunca estoura, por mais que insistam
    public void EsperaCresceEBateNoTeto(int falhasSeguidas, int segundosEsperados) =>
        Assert.Equal(TimeSpan.FromSeconds(segundosEsperados), LimitadorDeTentativas.EsperaPara(falhasSeguidas));

    [Theory]
    [InlineData(1, "1 s")]
    [InlineData(4.2, "5 s")]     // arredonda pra cima: "0 s" com a espera valendo confundiria
    [InlineData(59.5, "1 min")]   // 60 s vira "1 min"
    [InlineData(61, "2 min")]
    [InlineData(300, "5 min")]
    public void DescreveAEsperaSempreArredondandoParaCima(double segundos, string esperado) =>
        Assert.Equal(esperado, LimitadorDeTentativas.Descrever(TimeSpan.FromSeconds(segundos)));

    [Fact]
    public void ForaDaEsperaPodeTentarEDepoisDoTempoLibera()
    {
        var relogio = new RelogioFalso();
        var limitador = new LimitadorDeTentativas(relogio);
        for (var i = 0; i < 3; i++)
            limitador.RegistrarFalha();
        Assert.Equal(TimeSpan.Zero, limitador.EsperaRestante);

        var espera = limitador.RegistrarFalha();   // a 4ª

        Assert.Equal(TimeSpan.FromSeconds(5), espera);
        Assert.Equal(TimeSpan.FromSeconds(5), limitador.EsperaRestante);
        relogio.Avancar(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(2), limitador.EsperaRestante);
        relogio.Avancar(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.Zero, limitador.EsperaRestante);
    }

    [Fact]
    public void AcertarZeraOContador()
    {
        var limitador = new LimitadorDeTentativas(new RelogioFalso());
        for (var i = 0; i < 6; i++)
            limitador.RegistrarFalha();
        Assert.True(limitador.EsperaRestante > TimeSpan.Zero);

        limitador.Zerar();

        Assert.Equal(TimeSpan.Zero, limitador.EsperaRestante);
        for (var i = 0; i < 3; i++)
            Assert.Equal(TimeSpan.Zero, limitador.RegistrarFalha());   // recomeça com as 3 livres
    }

    // ---- o serviço de login ----

    private static async Task<LoginOperadorService> CriarServicoAsync(SqliteInMemoryFixture fixture, RelogioFalso relogio)
    {
        await using var context = fixture.CriarContexto();
        context.Funcionarios.Add(new Funcionario { Nome = "Carlos", PdvKeyHash = PdvKeyTeste.Hash("1234") });
        await context.SaveChangesAsync();
        return new LoginOperadorService(fixture.CriarContexto, relogio);
    }

    private static async Task ErrarAsync(LoginOperadorService servico, int vezes)
    {
        for (var i = 0; i < vezes; i++)
            Assert.Null(await servico.AutenticarAsync("0000"));
    }

    [Fact]
    public async Task TresErrosSeguidosNaoBloqueiamEACertaAindaEntra()
    {
        using var fixture = new SqliteInMemoryFixture();
        var servico = await CriarServicoAsync(fixture, new RelogioFalso());

        await ErrarAsync(servico, 3);

        Assert.Equal(TimeSpan.Zero, servico.EsperaRestante);
        Assert.NotNull(await servico.AutenticarAsync("1234"));
    }

    [Fact]
    public async Task AQuartaFalhaBloqueiaEATeclaCertaNaEsperaTambemEhRecusada()
    {
        using var fixture = new SqliteInMemoryFixture();
        var servico = await CriarServicoAsync(fixture, new RelogioFalso());
        await ErrarAsync(servico, 4);

        Assert.Equal(TimeSpan.FromSeconds(5), servico.EsperaRestante);
        Assert.Null(await servico.AutenticarAsync("1234"));   // nem a chave certa vale durante a espera
    }

    [Fact]
    public async Task DepoisDaEsperaACertaEntraEZeraTudo()
    {
        using var fixture = new SqliteInMemoryFixture();
        var relogio = new RelogioFalso();
        var servico = await CriarServicoAsync(fixture, relogio);
        await ErrarAsync(servico, 5);   // espera de 10 s
        relogio.Avancar(TimeSpan.FromSeconds(10));

        Assert.NotNull(await servico.AutenticarAsync("1234"));

        Assert.Equal(TimeSpan.Zero, servico.EsperaRestante);
        await ErrarAsync(servico, 3);   // recomeça com as 3 livres
        Assert.Equal(TimeSpan.Zero, servico.EsperaRestante);
    }

    [Fact]
    public async Task TentativaDuranteAEsperaNaoContaNemEstendeAEspera()
    {
        using var fixture = new SqliteInMemoryFixture();
        var relogio = new RelogioFalso();
        var servico = await CriarServicoAsync(fixture, relogio);
        await ErrarAsync(servico, 4);   // espera de 5 s

        await ErrarAsync(servico, 10);  // insistir durante a espera não piora nada

        relogio.Avancar(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.Zero, servico.EsperaRestante);
        await ErrarAsync(servico, 1);   // esta é a 5ª de verdade: 10 s (e não o teto de 10 tentativas)
        Assert.Equal(TimeSpan.FromSeconds(10), servico.EsperaRestante);
    }

    [Fact]
    public async Task EsperaCrescenteDeVerdadeEntreErrosDepoisDeCadaEspera()
    {
        using var fixture = new SqliteInMemoryFixture();
        var relogio = new RelogioFalso();
        var servico = await CriarServicoAsync(fixture, relogio);
        await ErrarAsync(servico, 4);

        var esperas = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            esperas.Add((int)servico.EsperaRestante.TotalSeconds);
            relogio.Avancar(servico.EsperaRestante);
            await ErrarAsync(servico, 1);
        }

        Assert.Equal(new[] { 5, 10, 20, 40 }, esperas);
    }

    // ---- a tela ----

    [Fact]
    public async Task TelaAvisaQuandoOErroPassaAExigirEsperaEDepoisRecusaSemTentar()
    {
        using var fixture = new SqliteInMemoryFixture();
        var servico = await CriarServicoAsync(fixture, new RelogioFalso());
        var viewModel = new LoginViewModel(servico) { PdvKeyDigitada = "0000" };

        for (var i = 0; i < 3; i++)
        {
            await viewModel.EntrarCommand.Execute();
            Assert.Equal("Chave inválida.", viewModel.MensagemErro);   // ainda nas livres: mensagem de sempre
        }

        await viewModel.EntrarCommand.Execute();   // a 4ª
        Assert.Contains("Chave inválida.", viewModel.MensagemErro);
        Assert.Contains("aguarde 5 s", viewModel.MensagemErro);

        viewModel.PdvKeyDigitada = "1234";         // mesmo a certa, durante a espera
        var funcionario = await viewModel.EntrarCommand.Execute();
        Assert.Null(funcionario);
        Assert.Contains("aguarde", viewModel.MensagemErro);
    }

    [Fact]
    public async Task TelaEntraNormalmenteDepoisDaEspera()
    {
        using var fixture = new SqliteInMemoryFixture();
        var relogio = new RelogioFalso();
        var servico = await CriarServicoAsync(fixture, relogio);
        await ErrarAsync(servico, 4);
        relogio.Avancar(TimeSpan.FromSeconds(5));
        var viewModel = new LoginViewModel(servico) { PdvKeyDigitada = "1234" };

        var funcionario = await viewModel.EntrarCommand.Execute();

        Assert.NotNull(funcionario);
        Assert.Null(viewModel.MensagemErro);
    }
}

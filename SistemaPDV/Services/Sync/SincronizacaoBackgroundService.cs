using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa;
using SistemaPDV.Services.Sales;

namespace SistemaPDV.Services.Sync;

public enum EstadoConexao
{
    // Nenhum ciclo chegou a falar com a API ainda (app recém-aberto, ou dispositivo sem
    // configuração) — diferente de Offline, que é "tentou e não conseguiu".
    Desconhecida,
    Online,

    // Autenticou, mas algum recurso do catálogo (funcionários, produtos…) falhou — o header
    // avisa e a dica diz qual. Sem isso um endpoint quebrado ficava invisível atrás do 🟢.
    OnlineComFalhas,
    Offline,
}

// Dispara sozinho os outboxes (caixa, venda, cliente novo) a cada 30 s e o catálogo
// completo a cada 5 min — o que tira o "sincroniza quando o operador lembra" do v1.
//
// Estrutura: os CICLOS (ExecutarCiclo*Async) são métodos públicos comuns, testados com
// FakeHttpMessageHandler como os outros serviços de sync; Iniciar/Parar só ligam dois
// DispatcherTimer (decisão de 2026-09-18) que chamam esses ciclos.
//
// Cada tick vira Task.Run: o DispatcherTimer dispara na thread de UI, mas o trabalho
// NÃO pode rodar nela — o provedor SQLite do EF é síncrono por baixo (os "await" dele
// não liberam a thread), então um catálogo grande gravado na thread de UI congelaria a
// tela. Por isso o estado de conexão é publicado num observable e quem escuta (App)
// leva o valor de volta pra thread de UI.
//
// Regras que valem pra qualquer ciclo:
//  - nada roda sem ConfiguracaoSincronizacao completa (URL + client id + secret);
//  - só um ciclo por vez (semáforo, sem fila): se o anterior ainda está rodando, o novo
//    é ignorado e o próximo tick tenta de novo — evita dois ciclos disputando o SQLite
//    e push/pull de clientes ao mesmo tempo;
//  - exceção nunca escapa (só cancelamento passa): vira Offline + mensagem. Falha de
//    sync é silenciosa + indicador, nunca crash (Boundary "Nunca" da SPEC-pdv-ui).
[SupportedOSPlatform("windows")]
public class SincronizacaoBackgroundService : IDisposable
{
    public static readonly TimeSpan IntervaloOutbox = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan IntervaloCatalogo = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IntervaloVerificacao = TimeSpan.FromSeconds(15);

    // SincronizarCaixaPendenteAsync trata UM caixa por chamada; o teto só evita um laço
    // longo se algo estiver errado (na prática há 0-2 caixas pendentes).
    private const int MaximoCaixasPorCiclo = 20;
    private const int TamanhoMaximoMensagem = 200;

    private readonly Func<AppDbContext> contextFactory;
    private readonly SoftcomAuthService authService;
    private readonly CatalogSyncService catalogSyncService;
    private readonly CaixaSyncService caixaSyncService;
    private readonly VendaSyncService vendaSyncService;
    private readonly TimeProvider timeProvider;
    private readonly VerificadorDeConexao? verificador;

    private readonly SemaphoreSlim cicloEmAndamento = new(1, 1);
    private readonly BehaviorSubject<EstadoConexao> estado = new(EstadoConexao.Desconhecida);
    private readonly object travaEstadoRegistrado = new();
    private EstadoConexao? ultimoEstadoRegistrado;   // só pro log: ver RegistrarMudancaDeEstado
    private readonly Subject<Unit> dadosAlterados = new();
    private readonly BehaviorSubject<AndamentoDoEnvio> andamento = new(AndamentoDoEnvio.Ocioso);
    private int enviadosNoCiclo;   // contadores do ciclo em curso, para o resumo do fim (o ciclo é serializado pelo semáforo)
    private int falhasNoCiclo;

    private bool catalogoSincronizado;
    private CancellationTokenSource? cancelamento;
    private DispatcherTimer? timerOutbox;
    private DispatcherTimer? timerCatalogo;
    private DispatcherTimer? timerVerificacao;
    private readonly object travaPublicacao = new();
    private bool? ultimaVerificacaoAlcancavel;   // null = ainda não verificou
    private int tentativasDeRecuperacao;
    private const int MaximoTentativasDeRecuperacao = 3;

    public SincronizacaoBackgroundService(
        Func<AppDbContext> contextFactory,
        SoftcomAuthService authService,
        CatalogSyncService catalogSyncService,
        CaixaSyncService caixaSyncService,
        VendaSyncService vendaSyncService,
        TimeProvider? timeProvider = null,
        VerificadorDeConexao? verificador = null,
        LogDeSincronizacao? log = null)
    {
        this.verificador = verificador;
        Log = log ?? new LogDeSincronizacao(timeProvider);
        this.contextFactory = contextFactory;
        this.authService = authService;
        this.catalogSyncService = catalogSyncService;
        this.caixaSyncService = caixaSyncService;
        this.vendaSyncService = vendaSyncService;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public EstadoConexao Estado => estado.Value;

    // O que a fila outbox fez, em linguagem de operador (a tela "Fila Outbox" mostra): conexão que caiu ou voltou, o que foi
    // enviado e o que foi recusado. O detalhe técnico (exceções) continua no arquivo de log, via Registro.
    public LogDeSincronizacao Log { get; }

    // Por que o último ciclo ficou Offline (ex: "A URL da API não usa HTTPS...") — a tela
    // mostra como dica no indicador, senão um problema de configuração pareceria só "sem
    // rede". Definida ANTES de emitir o estado, pra quem reage ao estado já ler o texto novo.
    public string? MensagemUltimoCiclo { get; private set; }

    public IObservable<EstadoConexao> EstadoConexaoAlterada => estado.DistinctUntilChanged();

    // O que o envio da fila está fazendo agora (ver AndamentoDoEnvio). Quem assina recebe o valor atual na hora; emite de qualquer thread.
    public AndamentoDoEnvio Andamento => andamento.Value;
    public IObservable<AndamentoDoEnvio> AndamentoAlterado => andamento;

    // Emite quando um ciclo mexeu no banco local (item enviado, falha registrada, dado novo
    // do catálogo) — as telas que listam esses dados se recarregam sozinhas. Ciclo que não
    // fez nada (ocioso, sem itens novos) não emite: recarregar lista à toa a cada 30 s é
    // trabalho perdido.
    public IObservable<Unit> DadosAlterados => dadosAlterados;

    // Liga os dois ritmos e já dispara uma primeira rodada — sem isso, o operador que
    // acabou de abrir o app esperaria 30 s pra ver qualquer coisa. Chamar na thread de UI.
    public void Iniciar()
    {
        if (timerOutbox is not null)
            return;

        cancelamento = new CancellationTokenSource();

        timerOutbox = new DispatcherTimer { Interval = IntervaloOutbox };
        timerOutbox.Tick += (_, _) => Disparar(ExecutarCicloRapidoAsync);
        timerOutbox.Start();

        timerCatalogo = new DispatcherTimer { Interval = IntervaloCatalogo };
        timerCatalogo.Tick += (_, _) => Disparar(ExecutarCicloCatalogoAsync);
        timerCatalogo.Start();

        // Detecção de queda/volta da internet sem esperar os ciclos (que só falam com a rede quando têm o que enviar): o
        // Windows avisa na hora quando a placa perde a rede, e uma verificação leve pega o "rede ligada, sem internet".
        if (verificador is not null)
        {
            NetworkChange.NetworkAvailabilityChanged += AoMudarDisponibilidadeDeRede;
            timerVerificacao = new DispatcherTimer { Interval = IntervaloVerificacao };
            timerVerificacao.Tick += (_, _) => Disparar(VerificarConexaoAsync);
            timerVerificacao.Start();
        }

        Disparar(ExecutarCicloRapidoAsync);
    }

    // Roda o ciclo rápido já (catálogo inicial + outbox) fora do timer — usado logo depois
    // de vincular o dispositivo, pra os funcionários chegarem antes do operador tentar logar.
    public void SolicitarAgora() => Disparar(ExecutarCicloRapidoAsync);

    public void Parar()
    {
        NetworkChange.NetworkAvailabilityChanged -= AoMudarDisponibilidadeDeRede;
        timerOutbox?.Stop();
        timerCatalogo?.Stop();
        timerVerificacao?.Stop();
        timerOutbox = null;
        timerCatalogo = null;
        timerVerificacao = null;

        cancelamento?.Cancel();
        cancelamento?.Dispose();
        cancelamento = null;
    }

    public void Dispose()
    {
        Parar();
        estado.Dispose();
        dadosAlterados.Dispose();
        andamento.Dispose();
        cicloEmAndamento.Dispose();
    }

    // Roda numa thread do sistema (não na de UI). Sem placa de rede ativa, é Offline na hora; ao voltar, confirma com a API.
    private void AoMudarDisponibilidadeDeRede(object? remetente, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            Disparar(VerificarConexaoAsync);
            return;
        }

        ultimaVerificacaoAlcancavel = false;
        if (Estado != EstadoConexao.Offline)
            Publicar(EstadoConexao.Offline, "Sem conexão de rede", faltaDeRede: true);
    }

    // A cada 15 s (e quando o Windows diz que a rede voltou): o servidor da API responde? Não usa o semáforo dos ciclos — uma
    // requisição de envio pendurada não pode atrasar o aviso de queda. Só a MUDANÇA age: caiu -> Offline; voltou (depois de ter
    // ficado inalcançável) -> roda o catálogo (que autentica e publica Online) e o envio, sem esperar os 30 s/5 min. Repetir a
    // resposta "alcançável" não dispara nada: um Offline por credencial errada não vira uma autenticação a cada 15 s.
    public async Task VerificarConexaoAsync(CancellationToken ct = default)
    {
        if (verificador is null || await LerConfiguracaoCompletaAsync(ct) is not { } configuracao)
            return;

        var alcancavel = await verificador.AlcancavelAsync(configuracao.UrlApi!, ct);
        var anterior = ultimaVerificacaoAlcancavel;
        ultimaVerificacaoAlcancavel = alcancavel;

        if (!alcancavel)
        {
            tentativasDeRecuperacao = 0;
            if (Estado != EstadoConexao.Offline)
                Publicar(EstadoConexao.Offline, "Sem acesso à API (verificação de conexão)", faltaDeRede: true);
            return;
        }

        if (anterior != false)
            return;

        await ExecutarCicloCatalogoAsync(ct);
        await ExecutarCicloOutboxAsync(ct);

        // Ainda Offline depois de tentar: ou um ciclo que já estava rodando não deixou este rodar (um envio que ficou pendurado
        // durante a queda e só agora termina — ele mesmo pode publicar Offline por cima do que acabamos de publicar), ou o
        // catálogo falhou. Tenta de novo na próxima verificação, mas só algumas vezes: com credencial errada e o servidor no ar
        // isso viraria uma autenticação a cada 15 s.
        if (Estado == EstadoConexao.Offline && ++tentativasDeRecuperacao < MaximoTentativasDeRecuperacao)
            ultimaVerificacaoAlcancavel = false;
        else
            tentativasDeRecuperacao = 0;
    }

    // Tick de 30 s: se o catálogo ainda não foi baixado nesta sessão (app recém-aberto,
    // ou dispositivo vinculado agora), baixa primeiro — sem funcionários locais ninguém
    // consegue logar, e o operador não deveria esperar os 5 min do ritmo normal. Depois,
    // o outbox.
    public async Task ExecutarCicloRapidoAsync(CancellationToken ct = default)
    {
        if (!catalogoSincronizado)
            await ExecutarCicloCatalogoAsync(ct);

        await ExecutarCicloOutboxAsync(ct);
    }

    // Tick de 5 min: catálogo completo (autentica sozinho, ver SincronizarTudoAsync).
    // "Online" = conseguiu autenticar; falha parcial de um recurso não derruba o estado
    // (o resto do catálogo já foi gravado) e é retentada no ciclo de 5 min seguinte.
    public async Task ExecutarCicloCatalogoAsync(CancellationToken ct = default)
    {
        if (!await cicloEmAndamento.WaitAsync(0, ct))
            return;

        try
        {
            if (await LerConfiguracaoCompletaAsync(ct) is null)
                return;

            var resultado = await catalogSyncService.SincronizarTudoAsync(ct);
            if (resultado.AutenticacaoSucesso)
            {
                catalogoSincronizado = true;
                var falhas = DescreverFalhas(resultado);
                Publicar(
                    falhas is null ? EstadoConexao.Online : EstadoConexao.OnlineComFalhas,
                    falhas ?? DescreverTrazido(resultado));
                RegistrarCatalogoNoLog(resultado, falhas);
                if (TrouxeAlgo(resultado))
                    dadosAlterados.OnNext(Unit.Default);
            }
            else
            {
                Publicar(EstadoConexao.Offline, resultado.MensagemAutenticacao);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Registro.Erro("Sincronização", "Exceção no ciclo do catálogo", ex);
            Publicar(EstadoConexao.Offline, ex.Message);
        }
        finally
        {
            cicloEmAndamento.Release();
        }
    }

    private string? ultimaFalhaDoCatalogoNoLog;

    // O catálogo roda a cada 5 min e quase sempre não traz nada: só vira linha do log quando trouxe algo, ou quando uma falha
    // NOVA aparece (a mesma falha se repetindo a cada ciclo não enche a tela de avisos iguais).
    private void RegistrarCatalogoNoLog(ResultadoSincronizacaoCompleta resultado, string? falhas)
    {
        if (falhas is not null)
        {
            if (falhas != ultimaFalhaDoCatalogoNoLog)
                Log.Registrar(NivelAtividade.Aviso, $"Catálogo atualizado com falhas: {falhas}");

            ultimaFalhaDoCatalogoNoLog = falhas;
            return;
        }

        ultimaFalhaDoCatalogoNoLog = null;
        if (TrouxeAlgo(resultado))
            Log.Registrar(NivelAtividade.Info, DescreverTrazido(resultado));
    }

    // Tick de 30 s (parte do outbox): só fala com a rede se há algo a enviar — sem
    // pendência nem token é pedido (uma requisição a cada 30 s sem motivo). Ordem:
    // clientes antes de vendas (a venda de um cliente novo só sai depois que ele tem id).
    public async Task ExecutarCicloOutboxAsync(CancellationToken ct = default)
    {
        if (!await cicloEmAndamento.WaitAsync(0, ct))
            return;

        try
        {
            var configuracao = await LerConfiguracaoCompletaAsync(ct);
            if (configuracao is null)
                return;

            var pendencias = await ContarPendenciasAsync(ct);
            if (pendencias == 0)
                return;

            var (autenticado, mensagem, accessToken) = await authService.ObterTokenAsync(configuracao, ct);
            if (!autenticado || accessToken is null)
            {
                Publicar(EstadoConexao.Offline, mensagem);
                return;
            }

            Publicar(EstadoConexao.Online, null);

            // O ciclo vira um BLOCO no log: cabeçalho, uma linha por item e um resumo no fim ("Ciclo concluído em 1,2 s: 3 enviados").
            // O resumo e o fim do "sincronizando" ficam num finally: mesmo que algo escape, o painel não fica preso em "Sincronizando…".
            var inicio = timeProvider.GetTimestamp();
            enviadosNoCiclo = 0;
            falhasNoCiclo = 0;
            andamento.OnNext(new AndamentoDoEnvio(true, null));
            Log.Registrar(NivelAtividade.Info, $"Disparando sincronização de {pendencias} item(ns) pendente(s)...", MarcaDeLog.InicioDeCiclo);

            try
            {
                await ExecutarEtapaAsync("Clientes novos",
                    quantidade => $"POST /clientes: {quantidade} cliente(s) novo(s) enviado(s) com sucesso!",
                    () => catalogSyncService.SincronizarClientesNovosPendentesAsync(accessToken, ct));
                // Produto novo antes das vendas: a venda que o contém precisa dos ids que a API devolve no cadastro.
                await ExecutarEtapaAsync("Produtos novos",
                    quantidade => $"POST /produtos: {quantidade} produto(s) novo(s) enviado(s) com sucesso!",
                    () => catalogSyncService.SincronizarProdutosNovosPendentesAsync(accessToken, ct));
                // Ordem: abrir caixa -> vendas -> fechar caixa. A venda referencia o caixa aberto, e o FECHAMENTO resume
                // o caixa: se chegasse à API antes das vendas, ela fecharia um caixa "sem vendas".
                await ExecutarEtapaAsync("Abertura de caixa",
                    _ => "Abertura de caixa enviada ao SoftcomShop.",
                    () => RepetirAsync(() => caixaSyncService.SincronizarAberturaAsync(accessToken, ct)));
                await ExecutarEtapaAsync("Vendas",
                    _ => null,   // as vendas contam uma linha por pedido (Detalhes), não um total
                    () => vendaSyncService.SincronizarVendasPendentesAsync(accessToken, ct));
                await ExecutarEtapaAsync("Fechamento de caixa",
                    _ => "Fechamento de caixa enviado ao SoftcomShop.",
                    () => RepetirAsync(() => caixaSyncService.SincronizarFechamentoAsync(accessToken, ct)));
            }
            finally
            {
                RegistrarFimDoCiclo(timeProvider.GetElapsedTime(inicio));
                andamento.OnNext(AndamentoDoEnvio.Ocioso);
            }

            // Chegou até aqui = havia pendência e tentou enviar: o que mudou (🟡 -> 🟢 ou 🔴)
            // precisa aparecer nas listas.
            dadosAlterados.OnNext(Unit.Default);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Registro.Erro("Sincronização", "Exceção no ciclo de envio", ex);
            Publicar(EstadoConexao.Offline, ex.Message);
        }
        finally
        {
            cicloEmAndamento.Release();
        }
    }

    // Uma etapa que falha (mesmo lançando) não impede as seguintes: um cliente rejeitado
    // não pode travar o caixa que o operador está esperando confirmar. A entidade
    // simplesmente continua pendente/falha e é tentada de novo no próximo ciclo. A exceção vai pro log (Registro).
    //
    // Cada etapa também conta o que fez no log da fila outbox: uma linha por item (Detalhes, as vendas) ou uma de total
    // (textoSucesso; null = não diz nada). Falha da etapa vira aviso; exceção vira linha genérica (o detalhe técnico vai só
    // para o arquivo de log, nunca para a tela).
    private async Task ExecutarEtapaAsync(string rotulo, Func<int, string?> textoSucesso, Func<Task<ResultadoSincronizacaoRecurso>> etapa)
    {
        andamento.OnNext(new AndamentoDoEnvio(true, rotulo));   // o painel mostra "Sincronizando… (Vendas)"

        try
        {
            var resultado = await etapa();

            foreach (var detalhe in resultado.Detalhes)
            {
                Log.Registrar(detalhe.Nivel, detalhe.Texto);
                if (detalhe.Nivel == NivelAtividade.Sucesso)
                    enviadosNoCiclo++;
                else if (detalhe.Nivel is NivelAtividade.Aviso or NivelAtividade.Erro)
                    falhasNoCiclo++;
            }

            if (!resultado.Sucesso)
            {
                Log.Registrar(NivelAtividade.Aviso, $"{rotulo}: {resultado.Mensagem}");
                falhasNoCiclo++;
            }
            else if (resultado.Quantidade > 0 && textoSucesso(resultado.Quantidade) is { } texto)
            {
                Log.Registrar(NivelAtividade.Sucesso, texto);
                enviadosNoCiclo += resultado.Quantidade;   // as vendas (sem texto de total) já foram contadas uma a uma nos detalhes
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Registro.Erro("Sincronização", "Exceção numa etapa do envio (as outras etapas seguem)", ex);
            Log.Registrar(NivelAtividade.Erro, $"{rotulo}: erro inesperado (detalhes no log do aplicativo).");
            falhasNoCiclo++;
        }
    }

    // A última linha do bloco: quanto demorou e o que aconteceu. Aviso se algo falhou, sucesso se tudo o que saiu foi aceito, e uma
    // linha neutra quando nada saiu (itens esperando uma dependência ou em espera crescente) — para o operador não achar que travou.
    private void RegistrarFimDoCiclo(TimeSpan duracao)
    {
        var tempo = FormatarDuracao(duracao);

        if (falhasNoCiclo > 0)
            Log.Registrar(NivelAtividade.Aviso, $"Ciclo concluído em {tempo}: {enviadosNoCiclo} enviado(s), {falhasNoCiclo} com falha.", MarcaDeLog.FimDeCiclo);
        else if (enviadosNoCiclo > 0)
            Log.Registrar(NivelAtividade.Sucesso, $"Ciclo concluído em {tempo}: {enviadosNoCiclo} item(ns) enviado(s).", MarcaDeLog.FimDeCiclo);
        else
            Log.Registrar(NivelAtividade.Info, $"Ciclo concluído em {tempo}: nada foi enviado ainda (itens aguardando ou em espera).", MarcaDeLog.FimDeCiclo);
    }

    // "820 ms", "1,2 s", "1 min 05 s": como uma pessoa lê (nada de "00:00:01.2400000").
    public static string FormatarDuracao(TimeSpan duracao)
    {
        if (duracao.TotalSeconds < 1)
            return $"{(int)duracao.TotalMilliseconds} ms";
        if (duracao.TotalSeconds < 60)
            return $"{duracao.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} s";
        return $"{(int)duracao.TotalMinutes} min {duracao.Seconds:00} s";
    }

    // null = todos os recursos sincronizaram. Senão, "Funcionários: <motivo>; Produtos: <motivo>" —
    // vira a dica do indicador, pra o operador (e quem dá suporte) ver o que quebrou.
    private static string? DescreverFalhas(ResultadoSincronizacaoCompleta r)
    {
        var falhas = new List<string>();
        Anotar(falhas, "Formas de pagamento", r.FormasPagamento);
        Anotar(falhas, "Clientes", r.Clientes);
        Anotar(falhas, "Produtos", r.Produtos);
        Anotar(falhas, "Funcionários", r.Funcionarios);
        Anotar(falhas, "Empresa", r.Empresa);
        Anotar(falhas, "Cartões", r.Cartoes);
        Anotar(falhas, "Grupos", r.Grupos);
        return falhas.Count == 0 ? null : string.Join("; ", falhas);
    }

    // "Último catálogo: Formas de pagamento 3, Clientes 0, …" — é o que responde "sincronizou
    // mas o banco está vazio?": se a API mandou 0 itens, aparece aqui. (Ciclos seguintes
    // podem trazer 0 legitimamente: o catálogo é incremental, só vêm os itens alterados.)
    private static string DescreverTrazido(ResultadoSincronizacaoCompleta r) =>
        "Último catálogo: " +
        $"Formas de pagamento {r.FormasPagamento?.Quantidade ?? 0}, Clientes {r.Clientes?.Quantidade ?? 0}, " +
        $"Produtos {r.Produtos?.Quantidade ?? 0}, Funcionários {r.Funcionarios?.Quantidade ?? 0}, " +
        $"Empresa {r.Empresa?.Quantidade ?? 0}, Cartões {r.Cartoes?.Quantidade ?? 0}, Grupos {r.Grupos?.Quantidade ?? 0}";

    private static void Anotar(List<string> falhas, string recurso, ResultadoSincronizacaoRecurso? resultado)
    {
        if (resultado is { Sucesso: false })
            falhas.Add($"{recurso}: {resultado.Mensagem}");
    }

    private static bool TrouxeAlgo(ResultadoSincronizacaoCompleta r) =>
        (r.FormasPagamento?.Quantidade ?? 0) + (r.Clientes?.Quantidade ?? 0) + (r.Produtos?.Quantidade ?? 0) +
        (r.Funcionarios?.Quantidade ?? 0) + (r.Empresa?.Quantidade ?? 0) + (r.Cartoes?.Quantidade ?? 0) + (r.Grupos?.Quantidade ?? 0) > 0;

    // SincronizarAberturaAsync/SincronizarFechamentoAsync tratam UM caixa por chamada: repete até não haver mais
    // nenhum (ou falhar), com teto pra não girar sem fim se algo estiver errado.
    // Devolve o total tratado; se uma chamada falha, devolve ESSA falha (o que veio antes já foi enviado e não se perde).
    private static async Task<ResultadoSincronizacaoRecurso> RepetirAsync(Func<Task<ResultadoSincronizacaoRecurso>> etapa)
    {
        var total = 0;
        for (var i = 0; i < MaximoCaixasPorCiclo; i++)
        {
            var resultado = await etapa();
            if (!resultado.Sucesso)
                return resultado;
            if (resultado.Quantidade == 0)
                break;

            total += resultado.Quantidade;
        }

        return ResultadoSincronizacaoRecurso.ComSucesso(total);
    }

    // Mesmas condições de elegibilidade que os próprios serviços usam (abertura ainda não
    // confirmada, fechamento não confirmado, venda pendente/falha, cliente criado
    // localmente sem id) — checar aqui evita autenticar à toa.
    private async Task<int> ContarPendenciasAsync(CancellationToken ct)
    {
        await using var context = contextFactory();
        var agora = timeProvider.GetUtcNow().UtcDateTime;

        // Só conta o que a política de retentativa deixa enviar agora: item em espera crescente
        // ou que já desistiu não justifica autenticar a cada 30 s (e não entra no "N item(ns)" do log).
        var caixas = await context.Caixas
            .Where(c => !c.AberturaSincronizada || (c.Status == StatusCaixa.Fechado && c.SyncStatus != SyncStatus.Sincronizado))
            .Where(PoliticaRetentativa.Elegivel<Models.Caixa>(agora))
            .CountAsync(ct);
        var vendas = await context.Vendas
            .Where(v => v.SyncStatus == SyncStatus.PendenteSync || v.SyncStatus == SyncStatus.FalhaSync)
            .Where(PoliticaRetentativa.Elegivel<Venda>(agora))
            .CountAsync(ct);
        var clientes = await context.Clientes
            .Where(c => c.IdExterno == null && c.SyncStatus != SyncStatus.Sincronizado)
            .Where(PoliticaRetentativa.Elegivel<Cliente>(agora))
            .CountAsync(ct);

        var produtos = await context.Produtos
            .Where(p => p.IdExterno == null && p.SyncStatus != SyncStatus.Sincronizado)
            .Where(PoliticaRetentativa.Elegivel<Produto>(agora))
            .CountAsync(ct);

        return caixas + vendas + clientes + produtos;
    }

    // null = dispositivo ainda não vinculado: todos os ciclos ficam pausados.
    private async Task<ConfiguracaoSincronizacao?> LerConfiguracaoCompletaAsync(CancellationToken ct)
    {
        await using var context = contextFactory();
        var configuracao = await context.ConfiguracoesSincronizacao.AsNoTracking().FirstOrDefaultAsync(ct);

        return configuracao is not null &&
               !string.IsNullOrWhiteSpace(configuracao.UrlApi) &&
               !string.IsNullOrWhiteSpace(configuracao.ApiClienteId) &&
               !string.IsNullOrWhiteSpace(configuracao.ApiClienteSecretProtegido)
            ? configuracao
            : null;
    }

    // faltaDeRede: o motivo do Offline é a rede em si (sem placa ativa / API inalcançável) — e não uma resposta ruim da API
    // (credencial recusada, URL sem HTTPS…) ou um erro inesperado. Só muda o texto do log da fila outbox.
    private void Publicar(EstadoConexao novoEstado, string? mensagem, bool faltaDeRede = false)
    {
        MensagemUltimoCiclo = mensagem is { Length: > TamanhoMaximoMensagem } longa ? longa[..TamanhoMaximoMensagem] : mensagem;
        // Trava: os ciclos, a verificação de conexão e o evento de rede publicam de threads diferentes, e um Subject não
        // aceita OnNext concorrente.
        lock (travaPublicacao)
        {
            RegistrarMudancaDeEstado(novoEstado, mensagem, faltaDeRede);
            estado.OnNext(novoEstado);
        }
    }

    // Só quando o estado MUDA: um PDV sem internet publica "Offline" a cada 30 s, e uma linha por ciclo encheria o
    // arquivo sem dizer nada de novo. Assim o log conta a história ("caiu às 14:02, voltou às 14:20").
    private void RegistrarMudancaDeEstado(EstadoConexao novoEstado, string? mensagem, bool faltaDeRede)
    {
        EstadoConexao? anterior;
        lock (travaEstadoRegistrado)
        {
            anterior = ultimoEstadoRegistrado;
            ultimoEstadoRegistrado = novoEstado;
        }

        if (anterior == novoEstado)
            return;

        var texto = $"Conexão com a API: {anterior?.ToString() ?? "início"} → {novoEstado}" + (string.IsNullOrEmpty(mensagem) ? "" : $" ({mensagem})");
        if (novoEstado is EstadoConexao.Offline or EstadoConexao.OnlineComFalhas)
            Registro.Aviso("Sincronização", texto);
        else
            Registro.Info("Sincronização", texto);

        // A mesma história, em linguagem de operador, para a tela da fila outbox. Ficar online de partida não é notícia;
        // OnlineComFalhas continua "conectado" (a API respondeu — o que falhou vai nas linhas do catálogo).
        // "Offline" também é o que o app mostra quando a API recusa a autenticação ou a URL não usa HTTPS: dizer "conexão perdida"
        // nesses casos mandaria o operador olhar o cabo em vez do problema. O motivo bruto (que pode trazer o corpo da resposta
        // da API) fica de fora da tela — está na dica da pílula de conexão e no arquivo de log.
        if (novoEstado == EstadoConexao.Offline)
        {
            Log.Registrar(NivelAtividade.Aviso, faltaDeRede
                ? "Conexão perdida. Modo contingência ativado: as vendas irão para a Outbox."
                : "Não foi possível falar com a API (autenticação ou comunicação). Modo contingência ativado: as vendas irão para a Outbox.");
        }
        else if (anterior == EstadoConexao.Offline)
        {
            Log.Registrar(NivelAtividade.Info, "Conexão restabelecida. Iniciando o worker assíncrono...");
        }
    }

    // Fora da thread de UI (ver comentário da classe) e sem deixar nada escapar: um
    // ciclo que lança dentro de um Task.Run sem observador sumiria em silêncio, e um
    // OperationCanceledException de Parar() é esperado.
    private void Disparar(Func<CancellationToken, Task> ciclo)
    {
        var token = cancelamento?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                await ciclo(token);
            }
            catch (Exception ex)
            {
                // Os ciclos já tratam suas próprias falhas; isto é só a última rede de
                // segurança contra derrubar o processo. (O cancelamento de Parar() é esperado: não é erro.)
                if (ex is not OperationCanceledException)
                    Registro.Erro("Sincronização", "Exceção que escapou de um ciclo", ex);
            }
        }, CancellationToken.None);
    }
}

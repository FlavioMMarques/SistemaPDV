using System;
using System.Collections.Generic;
using System.Linq;
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

    private readonly SemaphoreSlim cicloEmAndamento = new(1, 1);
    private readonly BehaviorSubject<EstadoConexao> estado = new(EstadoConexao.Desconhecida);
    private readonly Subject<Unit> dadosAlterados = new();

    private bool catalogoSincronizado;
    private CancellationTokenSource? cancelamento;
    private DispatcherTimer? timerOutbox;
    private DispatcherTimer? timerCatalogo;

    public SincronizacaoBackgroundService(
        Func<AppDbContext> contextFactory,
        SoftcomAuthService authService,
        CatalogSyncService catalogSyncService,
        CaixaSyncService caixaSyncService,
        VendaSyncService vendaSyncService,
        TimeProvider? timeProvider = null)
    {
        this.contextFactory = contextFactory;
        this.authService = authService;
        this.catalogSyncService = catalogSyncService;
        this.caixaSyncService = caixaSyncService;
        this.vendaSyncService = vendaSyncService;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public EstadoConexao Estado => estado.Value;

    // Por que o último ciclo ficou Offline (ex: "A URL da API não usa HTTPS...") — a tela
    // mostra como dica no indicador, senão um problema de configuração pareceria só "sem
    // rede". Definida ANTES de emitir o estado, pra quem reage ao estado já ler o texto novo.
    public string? MensagemUltimoCiclo { get; private set; }

    public IObservable<EstadoConexao> EstadoConexaoAlterada => estado.DistinctUntilChanged();

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

        Disparar(ExecutarCicloRapidoAsync);
    }

    // Roda o ciclo rápido já (catálogo inicial + outbox) fora do timer — usado logo depois
    // de vincular o dispositivo, pra os funcionários chegarem antes do operador tentar logar.
    public void SolicitarAgora() => Disparar(ExecutarCicloRapidoAsync);

    public void Parar()
    {
        timerOutbox?.Stop();
        timerCatalogo?.Stop();
        timerOutbox = null;
        timerCatalogo = null;

        cancelamento?.Cancel();
        cancelamento?.Dispose();
        cancelamento = null;
    }

    public void Dispose()
    {
        Parar();
        estado.Dispose();
        dadosAlterados.Dispose();
        cicloEmAndamento.Dispose();
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
            Publicar(EstadoConexao.Offline, ex.Message);
        }
        finally
        {
            cicloEmAndamento.Release();
        }
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
            if (configuracao is null || !await TemPendenciasAsync(ct))
                return;

            var (autenticado, mensagem, accessToken) = await authService.ObterTokenAsync(configuracao, ct);
            if (!autenticado || accessToken is null)
            {
                Publicar(EstadoConexao.Offline, mensagem);
                return;
            }

            Publicar(EstadoConexao.Online, null);

            await ExecutarEtapaAsync(() => catalogSyncService.SincronizarClientesNovosPendentesAsync(accessToken, ct));
            await ExecutarEtapaAsync(() => SincronizarCaixasPendentesAsync(accessToken, ct));
            await ExecutarEtapaAsync(() => vendaSyncService.SincronizarVendasPendentesAsync(accessToken, ct));

            // Chegou até aqui = havia pendência e tentou enviar: o que mudou (🟡 -> 🟢 ou 🔴)
            // precisa aparecer nas listas.
            dadosAlterados.OnNext(Unit.Default);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Publicar(EstadoConexao.Offline, ex.Message);
        }
        finally
        {
            cicloEmAndamento.Release();
        }
    }

    // Uma etapa que falha (mesmo lançando) não impede as seguintes: um cliente rejeitado
    // não pode travar o caixa que o operador está esperando confirmar. A entidade
    // simplesmente continua pendente/falha e é tentada de novo no próximo ciclo. (Sem
    // infraestrutura de log ainda no projeto — mesma limitação registrada nos lotes de
    // VendaSyncService/CatalogSyncService.)
    private static async Task ExecutarEtapaAsync(Func<Task> etapa)
    {
        try
        {
            await etapa();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
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
        return falhas.Count == 0 ? null : string.Join("; ", falhas);
    }

    // "Último catálogo: Formas de pagamento 3, Clientes 0, …" — é o que responde "sincronizou
    // mas o banco está vazio?": se a API mandou 0 itens, aparece aqui. (Ciclos seguintes
    // podem trazer 0 legitimamente: o catálogo é incremental, só vêm os itens alterados.)
    private static string DescreverTrazido(ResultadoSincronizacaoCompleta r) =>
        "Último catálogo: " +
        $"Formas de pagamento {r.FormasPagamento?.Quantidade ?? 0}, Clientes {r.Clientes?.Quantidade ?? 0}, " +
        $"Produtos {r.Produtos?.Quantidade ?? 0}, Funcionários {r.Funcionarios?.Quantidade ?? 0}, " +
        $"Empresa {r.Empresa?.Quantidade ?? 0}";

    private static void Anotar(List<string> falhas, string recurso, ResultadoSincronizacaoRecurso? resultado)
    {
        if (resultado is { Sucesso: false })
            falhas.Add($"{recurso}: {resultado.Mensagem}");
    }

    private static bool TrouxeAlgo(ResultadoSincronizacaoCompleta r) =>
        (r.FormasPagamento?.Quantidade ?? 0) + (r.Clientes?.Quantidade ?? 0) + (r.Produtos?.Quantidade ?? 0) +
        (r.Funcionarios?.Quantidade ?? 0) + (r.Empresa?.Quantidade ?? 0) > 0;

    private async Task SincronizarCaixasPendentesAsync(string accessToken, CancellationToken ct)
    {
        for (var i = 0; i < MaximoCaixasPorCiclo; i++)
        {
            var resultado = await caixaSyncService.SincronizarCaixaPendenteAsync(accessToken, ct);
            if (!resultado.Sucesso || resultado.Quantidade == 0)
                return;
        }
    }

    // Mesmas condições de elegibilidade que os próprios serviços usam (abertura ainda não
    // confirmada, fechamento não confirmado, venda pendente/falha, cliente criado
    // localmente sem id) — checar aqui evita autenticar à toa.
    private async Task<bool> TemPendenciasAsync(CancellationToken ct)
    {
        await using var context = contextFactory();
        var agora = timeProvider.GetUtcNow().UtcDateTime;

        // Só conta o que a política de retentativa deixa enviar agora: item em espera crescente
        // ou que já desistiu não justifica autenticar a cada 30 s.
        return await context.Caixas
                   .Where(c => !c.AberturaSincronizada || (c.Status == StatusCaixa.Fechado && c.SyncStatus != SyncStatus.Sincronizado))
                   .Where(PoliticaRetentativa.Elegivel<Models.Caixa>(agora))
                   .AnyAsync(ct)
               || await context.Vendas
                   .Where(v => v.SyncStatus == SyncStatus.PendenteSync || v.SyncStatus == SyncStatus.FalhaSync)
                   .Where(PoliticaRetentativa.Elegivel<Venda>(agora))
                   .AnyAsync(ct)
               || await context.Clientes
                   .Where(c => c.IdExterno == null && c.SyncStatus != SyncStatus.Sincronizado)
                   .Where(PoliticaRetentativa.Elegivel<Cliente>(agora))
                   .AnyAsync(ct);
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

    private void Publicar(EstadoConexao novoEstado, string? mensagem)
    {
        MensagemUltimoCiclo = mensagem is { Length: > TamanhoMaximoMensagem } longa ? longa[..TamanhoMaximoMensagem] : mensagem;
        estado.OnNext(novoEstado);
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
            catch (Exception)
            {
                // Os ciclos já tratam suas próprias falhas; isto é só a última rede de
                // segurança contra derrubar o processo.
            }
        }, CancellationToken.None);
    }
}

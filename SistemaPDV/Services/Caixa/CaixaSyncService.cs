using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Caixa.Dtos;
using SistemaPDV.Services.Sync;
using SistemaPDV.Services;

namespace SistemaPDV.Services.Caixa;

// Outbox da abertura/fechamento de caixa: CaixaService grava local e instantâneo;
// esta classe é quem, quando há rede, tenta confirmar isso com a API — nunca é
// chamada no caminho síncrono de abrir/fechar.
public class CaixaSyncService
{
    private readonly Func<AppDbContext> contextFactory;
    private readonly SoftcomApiClient apiClient;
    private readonly TimeProvider timeProvider;

    public CaixaSyncService(Func<AppDbContext> contextFactory, SoftcomApiClient apiClient, TimeProvider? timeProvider = null)
    {
        this.contextFactory = contextFactory;
        this.apiClient = apiClient;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime AgoraUtc => timeProvider.GetUtcNow().UtcDateTime;

    // Elegível: abertura ainda não confirmada com o servidor — inclui PendenteSync
    // (nunca tentou) E FalhaSync (tentou e não deu certo; revisão de código pegou que
    // isso não estava sendo retentado antes, ver docs/APRENDIZADOS.md).
    public async Task<ResultadoSincronizacaoRecurso> SincronizarAberturaAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        // Elegivel: quem está em espera crescente (ou já desistiu) fica de fora — ver PoliticaRetentativa.
        var caixa = await context.Caixas
            .Where(c => !c.AberturaSincronizada)
            .Where(PoliticaRetentativa.Elegivel<Models.Caixa>(AgoraUtc))
            .FirstOrDefaultAsync(ct);
        if (caixa is null)
            return ResultadoSincronizacaoRecurso.ComSucesso(0);

        var funcionario = await context.Funcionarios.FindAsync(new object[] { caixa.FuncionarioId }, ct);
        if (funcionario?.IdExterno is not { } operadorId)
            return ResultadoSincronizacaoRecurso.ComFalha("Funcionário do caixa ainda não sincronizou — sincronize funcionários antes.");

        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Configuração de sincronização não encontrada.");
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);

        var corpo = new
        {
            data_caixa = FormatarDataCaixa(caixa.DataCaixa),
            operador_id = operadorId,
            turno = caixa.Turno,
            usuario_abertura_id = operadorId,
            // A API espera string ("10.00"), não número — e FormatarValor usa
            // CultureInfo.InvariantCulture de propósito: sem isso, num Windows
            // configurado em pt-BR, ToString("F2") produziria "10,00" (vírgula),
            // que quebraria o parsing numérico do lado da API.
            troco_inicial = FormatarValor(caixa.TrocoInicial),
        };

        var resultado = await apiClient.EnviarAsync(
            HttpMethod.Post, $"{dominio}/api/v2/financeiro/caixa-funcoes/abrir", corpo, accessToken, ct);

        // URL não segura é problema de configuração, não do caixa: nada saiu daqui,
        // então o caixa continua exatamente como estava (sem FalhaSync).
        if (resultado.Tipo == ResultadoEnvioTipo.ConexaoInsegura)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Conteudo);

        // 409: já existe caixa aberto pra essa data/operador/turno no servidor —
        // trata como já sincronizado em vez de erro (ver Open Questions da spec).
        // AberturaSincronizada = true aqui é o que estava faltando antes: mesmo
        // sem IdExterno (a resposta 409 não devolve id nenhum), o fechamento
        // precisa saber que pode seguir em frente — ver SincronizarFechamentoAsync.
        //
        // SyncStatus só vira Sincronizado aqui se o caixa AINDA estiver aberto
        // (correção em cima da correção anterior): se ele já foi fechado offline
        // antes dessa sincronização de abertura rodar, SyncStatus=PendenteSync
        // já está guardando "o fechamento ainda não foi confirmado" —
        // sobrescrever pra Sincronizado incondicionalmente apagaria essa
        // informação e o fechamento nunca mais seria detectado como pendente.
        // Se ainda está aberto, não tem fechamento nenhum esperando, então
        // Sincronizado aqui reflete o estado real (nada pendente).
        if (resultado.Tipo == ResultadoEnvioTipo.Conflito)
        {
            caixa.AberturaSincronizada = true;
            PoliticaRetentativa.Zerar(caixa);
            caixa.UltimoErroSync = null;
            if (caixa.Status == StatusCaixa.Aberto)
                caixa.SyncStatus = SyncStatus.Sincronizado;
            await context.SaveChangesAsync(ct);
            return ResultadoSincronizacaoRecurso.ComSucesso(1);
        }

        if (resultado.Tipo is ResultadoEnvioTipo.Falha or ResultadoEnvioTipo.TokenExpirado)
            return await MarcarFalhaAsync(context, caixa, ErroApiExtractor.Extrair(resultado.Conteudo), ct);

        var respostaDto = SoftcomJson.TentarDesserializar<CaixaFuncaoRespostaDto>(resultado.Conteudo);
        if (respostaDto?.Data?.Success is not { } sucesso)
            return await MarcarFalhaAsync(context, caixa, $"A resposta não trouxe o id do caixa: {ErroApiExtractor.Extrair(resultado.Conteudo)}", ct);

        caixa.IdExterno = sucesso.Id;
        caixa.AberturaSincronizada = true;
        PoliticaRetentativa.Zerar(caixa);
        caixa.UltimoErroSync = null;
        if (caixa.Status == StatusCaixa.Aberto)
            caixa.SyncStatus = SyncStatus.Sincronizado;
        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(1);
    }

    // Elegível: fechado localmente, abertura já confirmada com o servidor (não
    // importa se tem IdExterno — o endpoint de fechar identifica o caixa pela chave
    // natural data/turno/operador, não por id), e o fechamento em si ainda não foi
    // confirmado — inclui PendenteSync e FalhaSync, mesma lógica da abertura.
    public async Task<ResultadoSincronizacaoRecurso> SincronizarFechamentoAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var caixa = await context.Caixas
            .Include(c => c.Digitacoes)
            .Include(c => c.DigitacoesBandeiras)
            .Where(c => c.Status == StatusCaixa.Fechado && c.AberturaSincronizada && c.SyncStatus != SyncStatus.Sincronizado)
            .Where(PoliticaRetentativa.Elegivel<Models.Caixa>(AgoraUtc))
            .FirstOrDefaultAsync(ct);

        if (caixa is null)
            return ResultadoSincronizacaoRecurso.ComSucesso(0);

        var funcionario = await context.Funcionarios.FindAsync(new object[] { caixa.FuncionarioId }, ct);
        if (funcionario?.IdExterno is not { } operadorId)
            return ResultadoSincronizacaoRecurso.ComFalha("Funcionário do caixa ainda não sincronizou — sincronize funcionários antes.");

        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Configuração de sincronização não encontrada.");
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);

        var corpo = new
        {
            // Mesmo valor usado na abertura (ver SincronizarAberturaAsync) — é o que
            // identifica esse caixa junto com turno/operador do lado da API.
            data_caixa = FormatarDataCaixa(caixa.DataCaixa),
            turno = caixa.Turno,
            operador_id = operadorId,
            usuario_fechamento_id = operadorId,
            troco_final = FormatarValor(caixa.TrocoFinal ?? 0m),
            digitacao = caixa.Digitacoes.Select(d => new { forma_pagamento_id = d.FormaPagamentoId, valor = d.Valor }),
            digitacao_bandeiras = caixa.DigitacoesBandeiras.Select(d => new { bandeira = d.Bandeira, valor = d.Valor }),
        };

        var resultado = await apiClient.EnviarAsync(
            HttpMethod.Post, $"{dominio}/api/v2/financeiro/caixa-funcoes/fechar", corpo, accessToken, ct);

        // Sem esta linha, ConexaoInsegura cairia no "else" abaixo e o fechamento seria
        // marcado Sincronizado sem nunca ter saído da máquina (só Falha/TokenExpirado
        // eram tratados como erro). Novo valor de enum = revisar todo switch/if que o usa.
        if (resultado.Tipo == ResultadoEnvioTipo.ConexaoInsegura)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Conteudo);

        if (resultado.Tipo is ResultadoEnvioTipo.Falha or ResultadoEnvioTipo.TokenExpirado)
            return await MarcarFalhaAsync(context, caixa, ErroApiExtractor.Extrair(resultado.Conteudo), ct);

        PoliticaRetentativa.Zerar(caixa);
        caixa.SyncStatus = SyncStatus.Sincronizado;
        caixa.UltimoErroSync = null;
        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(1);
    }

    // Ponto de entrada único: olha o estado local e decide sozinho se precisa
    // sincronizar uma abertura ou um fechamento — quem chama isso (o futuro sync
    // geral do app) não precisa conhecer essa regra.
    public async Task<ResultadoSincronizacaoRecurso> SincronizarCaixaPendenteAsync(string accessToken, CancellationToken ct = default)
    {
        await using (var context = contextFactory())
        {
            var agora = AgoraUtc;
            var temAberturaPendente = await context.Caixas
                .Where(c => !c.AberturaSincronizada)
                .Where(PoliticaRetentativa.Elegivel<Models.Caixa>(agora))
                .AnyAsync(ct);
            if (temAberturaPendente)
                return await SincronizarAberturaAsync(accessToken, ct);

            var temFechamentoPendente = await context.Caixas
                .Where(c => c.Status == StatusCaixa.Fechado && c.AberturaSincronizada && c.SyncStatus != SyncStatus.Sincronizado)
                .Where(PoliticaRetentativa.Elegivel<Models.Caixa>(agora))
                .AnyAsync(ct);
            if (temFechamentoPendente)
                return await SincronizarFechamentoAsync(accessToken, ct);
        }

        return ResultadoSincronizacaoRecurso.ComSucesso(0);
    }

    private Task<ResultadoSincronizacaoRecurso> MarcarFalhaAsync(
        AppDbContext context, Models.Caixa caixa, string mensagem, CancellationToken ct)
    {
        var agora = AgoraUtc;
        return OutboxHelper.MarcarFalhaAsync(context, caixa, mensagem, (c, m) =>
        {
            c.SyncStatus = SyncStatus.FalhaSync;
            c.UltimoErroSync = PoliticaRetentativa.RegistrarFalha(c, agora, m);
        }, ct);
    }

    private static string FormatarValor(decimal valor) => valor.ToString("F2", CultureInfo.InvariantCulture);

    // Deriva o horário sempre da MESMA forma (meia-noite) a partir da DataCaixa
    // (DateOnly) — nunca do instante real de abertura/fechamento (DataAbertura),
    // que muda a cada tentativa e quebraria a correlação com a API (bug encontrado
    // em revisão de código: ver docs/APRENDIZADOS.md).
    private static string FormatarDataCaixa(DateOnly data) =>
        data.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

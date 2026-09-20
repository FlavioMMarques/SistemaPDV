using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Services;

// Lê/grava ConfiguracaoSincronizacao (linha única, um dispositivo) e faz o
// provisionamento inicial (link de cadastro -> client_secret via SoftcomAuthService
// -> protegido com SegredoProtector). ViewModel nunca toca Func<AppDbContext> nem
// SegredoProtector direto, só esse serviço — mesma regra já aplicada aos outros
// módulos (ver AppServices).
public class ConfiguracaoService
{
    private readonly Func<AppDbContext> contextFactory;
    private readonly SoftcomAuthService authService;
    private readonly SegredoProtector segredoProtector;

    public ConfiguracaoService(Func<AppDbContext> contextFactory, SoftcomAuthService authService, SegredoProtector segredoProtector)
    {
        this.contextFactory = contextFactory;
        this.authService = authService;
        this.segredoProtector = segredoProtector;
    }

    public async Task<ConfiguracaoSincronizacao> ObterOuCriarAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct);
        if (configuracao is not null)
            return configuracao;

        configuracao = new ConfiguracaoSincronizacao();
        context.ConfiguracoesSincronizacao.Add(configuracao);
        await context.SaveChangesAsync(ct);
        return configuracao;
    }

    // Único método que lida com segredo em texto puro (o client_secret que a API
    // devolve) — a restrição de plataforma fica só aqui, não na classe inteira,
    // mesmo padrão de granularidade já usado em SoftcomAuthService/CatalogSyncService.
    [SupportedOSPlatform("windows")]
    public async Task<(bool Sucesso, string Mensagem)> VincularDispositivoAsync(
        string linkCadastro, string nomeDispositivo, CancellationToken ct = default)
    {
        // Antes de qualquer chamada à API: um vínculo a OUTRA empresa com caixas/vendas ainda no banco local misturaria os
        // dados das duas (vendas pendentes seriam enviadas à empresa nova e o catálogo se sobreporia ao antigo).
        if (await MisturariaEmpresasAsync(linkCadastro, ct))
            return (false, "Este dispositivo já tem caixas ou vendas de outra empresa — vincular a uma empresa diferente misturaria os dados. Use um dispositivo (banco) novo para a outra empresa.");

        var (sucesso, mensagem, clienteSecret) = await authService.ObterClienteSecretAsync(linkCadastro, nomeDispositivo, ct);
        if (!sucesso || clienteSecret is null)
            return (false, mensagem);

        var clienteId = ExtrairClienteId(linkCadastro);
        var secretProtegido = segredoProtector.Proteger(clienteSecret);

        await AtualizarAsync(c =>
        {
            c.UrlApi = linkCadastro;
            c.ApiClienteId = clienteId;
            c.ApiClienteSecretProtegido = secretProtegido;
            c.NomeDispositivo = nomeDispositivo;
        }, ct);

        return (true, "Dispositivo vinculado com sucesso.");
    }

    // Abrir as Configurações depois de o dispositivo estar vinculado exige a chave de um supervisor (decisão do
    // usuário, 2026-09-20): re-vincular ou mudar o código do PDV afeta empresa e numeração, não é coisa de operador.
    // Devolve se liberou; tudo fica no log (quem entrou, e as tentativas recusadas).
    public async Task<bool> AutenticarSupervisorAsync(string? chave, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var supervisor = await SupervisorAutenticador.AutenticarAsync(context, chave, ct);

        if (supervisor is null)
            Registro.Aviso("Auditoria", "Acesso às Configurações recusado: chave de supervisor inválida.");
        else
            Registro.Info("Auditoria", $"Configurações liberadas pelo supervisor {supervisor.Id}.");

        return supervisor is not null;
    }

    public async Task AtualizarAsync(Action<ConfiguracaoSincronizacao> aplicar, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Configuração não encontrada — chame ObterOuCriarAsync antes.");

        aplicar(configuracao);
        await context.SaveChangesAsync(ct);
    }

    // true = o novo link é de uma empresa (CNPJ) diferente da atual E já existem caixas/vendas locais. Só recusa quando dá
    // pra PROVAR a diferença: link sem CNPJ (vínculo antigo) ou primeira vinculação não bloqueiam. Revincular à MESMA
    // empresa (ex: novo segredo) continua livre.
    private async Task<bool> MisturariaEmpresasAsync(string novoLink, CancellationToken ct)
    {
        await using var context = contextFactory();

        var linkAtual = await context.ConfiguracoesSincronizacao.Select(c => c.UrlApi).FirstOrDefaultAsync(ct);
        var cnpjAtual = SoftcomAuthService.ExtrairEmpresaCnpj(linkAtual);
        var cnpjNovo = SoftcomAuthService.ExtrairEmpresaCnpj(novoLink);
        if (cnpjAtual is null || cnpjNovo is null || cnpjAtual == cnpjNovo)
            return false;

        return await context.Caixas.AnyAsync(ct) || await context.Vendas.AnyAsync(ct);
    }

    // client_id vem embutido na query string do link de cadastro (junto com
    // empresa_name/empresa_cnpj) — não é algo que o operador digita separado.
    private static string? ExtrairClienteId(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return null;

        foreach (var par in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = par.Split('=', 2);
            if (partes.Length == 2 && partes[0] == "client_id")
                return Uri.UnescapeDataString(partes[1]);
        }

        return null;
    }
}

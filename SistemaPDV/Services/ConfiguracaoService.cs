using System;
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

    public async Task AtualizarAsync(Action<ConfiguracaoSincronizacao> aplicar, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Configuração não encontrada — chame ObterOuCriarAsync antes.");

        aplicar(configuracao);
        await context.SaveChangesAsync(ct);
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

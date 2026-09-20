using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Services;

// Tela de Cadastros (Task 49): listagem com busca sobre o banco local e criação de
// cliente. Nunca chama a rede — criar só grava PendenteSync; quem envia é o outbox
// (CatalogSyncService.SincronizarClienteNovoAsync, disparado pelo serviço de
// sincronização em background). Por isso criar é instantâneo e funciona offline.
public class CadastroLocalService
{
    // Sem paginação na tela (cadastro simples): um catálogo real tem milhares de
    // produtos, e materializar todos numa ListBox trava a UI. A busca é o caminho
    // pra achar o resto — a tela avisa quando a lista foi cortada.
    public const int LimiteLista = 200;

    private const int TamanhoMaximoNome = 150;

    private readonly Func<AppDbContext> contextFactory;

    public CadastroLocalService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<ClienteResumo>> ListarClientesAsync(string? busca, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var consulta = context.Clientes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var porNome = PadraoContem(busca.Trim());
            var digitos = DocumentoValidator.SoDigitos(busca);
            consulta = digitos.Length > 0
                ? consulta.Where(c => EF.Functions.Like(c.Nome, porNome, "\\") || EF.Functions.Like(c.CpfCnpj, PadraoContem(digitos), "\\"))
                : consulta.Where(c => EF.Functions.Like(c.Nome, porNome, "\\"));
        }

        var clientes = await consulta.OrderBy(c => c.Nome).Take(LimiteLista).ToListAsync(ct);
        return clientes
            .Select(c => new ClienteResumo(c.Id, c.Nome, DocumentoValidator.Formatar(c.CpfCnpj), c.SyncStatus, c.UltimoErroSync))
            .ToList();
    }

    public async Task<IReadOnlyList<ProdutoResumo>> ListarProdutosAsync(string? busca, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var consulta = context.Produtos.AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = PadraoContem(busca.Trim());
            consulta = consulta.Where(p =>
                EF.Functions.Like(p.Nome, padrao, "\\") ||
                EF.Functions.Like(p.CodigoBarras, padrao, "\\") ||
                EF.Functions.Like(p.Sku, padrao, "\\"));
        }

        var produtos = await consulta.OrderBy(p => p.Nome).Take(LimiteLista).ToListAsync(ct);
        return produtos
            .Select(p => new ProdutoResumo(p.Id, p.Nome, p.CodigoBarras, p.PrecoVenda, p.EstoqueAtual, p.SyncStatus))
            .ToList();
    }

    // Valida antes de gravar (o que o outbox rejeitaria de qualquer jeito, só que sem
    // ninguém ver o motivo): nome obrigatório, documento — se informado — com dígitos
    // verificadores válidos e ainda não cadastrado. O documento é guardado só com
    // dígitos, que é como a API o devolve e o que o push envia.
    public async Task<ResultadoCriacaoCliente> CriarClienteAsync(string? nome, string? cpfCnpj, CancellationToken ct = default)
    {
        var nomeLimpo = nome?.Trim() ?? string.Empty;
        if (nomeLimpo.Length == 0)
            return ResultadoCriacaoCliente.ComFalha("Informe o nome do cliente.");
        if (nomeLimpo.Length > TamanhoMaximoNome)
            return ResultadoCriacaoCliente.ComFalha($"O nome pode ter no máximo {TamanhoMaximoNome} caracteres.");

        string? documento = null;
        if (!string.IsNullOrWhiteSpace(cpfCnpj))
        {
            // Só pontuação usual além dos dígitos: "123abc" não vira "123" em silêncio.
            var digitos = DocumentoValidator.SoDigitos(cpfCnpj);
            var soPontuacaoUsual = cpfCnpj.All(c => char.IsAsciiDigit(c) || ".-/ ".Contains(c));
            if (!soPontuacaoUsual || !(DocumentoValidator.CpfValido(digitos) || DocumentoValidator.CnpjValido(digitos)))
                return ResultadoCriacaoCliente.ComFalha("CPF/CNPJ inválido — confira os dígitos.");

            documento = digitos;
        }

        await using var context = contextFactory();

        if (documento is not null && await context.Clientes.AnyAsync(c => c.CpfCnpj == documento, ct))
            return ResultadoCriacaoCliente.ComFalha("Já existe um cliente com este CPF/CNPJ.");

        var cliente = new Cliente
        {
            Nome = nomeLimpo,
            CpfCnpj = documento,
            // Pessoa deriva do documento validado (14 dígitos = CNPJ), como o push faz.
            Pessoa = documento?.Length == 14 ? TipoPessoa.Juridica : TipoPessoa.Fisica,
            SyncStatus = SyncStatus.PendenteSync,
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync(ct);
        return ResultadoCriacaoCliente.ComSucesso(cliente.Id);
    }

    // "Reenviar falhas": devolve à fila de envio os clientes que falharam ou desistiram
    // (zera a espera crescente e o contador — ver PoliticaRetentativa). O próximo ciclo
    // de sincronização os envia; nada é enviado daqui. Devolve quantos voltaram.
    public async Task<int> ReenviarFalhasAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await context.Clientes
            .Where(c => c.IdExterno == null && c.SyncStatus == SyncStatus.FalhaSync)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.TentativasEnvio, 0)
                .SetProperty(c => c.ProximaTentativaEm, (DateTime?)null), ct);
    }

    // "%" e "_" são curingas do LIKE: sem escapar, digitar "%" na busca listaria tudo.
    private static string PadraoContem(string texto) =>
        "%" + texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}

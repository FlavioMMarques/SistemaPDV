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
            .Select(c => new ClienteResumo(
                c.Id, c.Nome, DocumentoValidator.Formatar(c.CpfCnpj), c.SyncStatus, c.UltimoErroSync,
                c.IdExterno, FormatarTelefone(c.ContatoDdd, c.ContatoTelefone), CidadeUf(c.Cidade, c.Uf)))
            .ToList();
    }

    // "(83) 99999-0000": o DDD entre parênteses e o número com hífen antes dos 4 últimos dígitos. A API guarda o DDD à parte
    // (8 ou 9 dígitos no número), mas às vezes o número já vem com o DDD dentro (10 ou 11 dígitos) — nos dois casos sai igual, sem
    // repetir o DDD. Qualquer outro tamanho aparece como veio: melhor mostrar o dado cru que inventar uma máscara errada.
    public static string? FormatarTelefone(string? ddd, string? telefone)
    {
        var numero = DocumentoValidator.SoDigitos(telefone);
        if (numero.Length == 0)
            return null;

        if (numero.Length is 10 or 11)
            return $"({numero[..2]}) {numero[2..^4]}-{numero[^4..]}";

        if (numero.Length is 8 or 9)
        {
            var local = $"{numero[..^4]}-{numero[^4..]}";
            var prefixo = DocumentoValidator.SoDigitos(ddd);
            return prefixo.Length == 2 ? $"({prefixo}) {local}" : local;
        }

        return telefone!.Trim();
    }

    // "João Pessoa - PB"; só uma das duas partes aparece sozinha; nenhuma = nulo.
    public static string? CidadeUf(string? cidade, string? uf)
    {
        var partes = new[] { cidade?.Trim(), uf?.Trim() }.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        return partes.Length == 0 ? null : string.Join(" - ", partes);
    }

    // Os números das abas (Produtos ( 12 ) · Clientes ( 4 ) · Operadores ( 3 )): totais, sem busca e sem o corte da lista.
    public async Task<ContagemCadastros> ContarAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return new ContagemCadastros(
            await context.Produtos.CountAsync(ct),
            await context.Clientes.CountAsync(ct),
            await context.Funcionarios.CountAsync(ct));
    }

    // Os operadores do terminal (funcionários sincronizados): os ativos primeiro, depois por nome. Sem CPF na projeção.
    public async Task<IReadOnlyList<OperadorResumo>> ListarOperadoresAsync(string? busca, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var consulta = context.Funcionarios.AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = PadraoContem(busca.Trim());
            consulta = consulta.Where(f => EF.Functions.Like(f.Nome, padrao, "\\"));
        }

        var funcionarios = await consulta.OrderBy(f => f.Desativado).ThenBy(f => f.Nome).Take(LimiteLista).ToListAsync(ct);
        var ids = funcionarios.Select(f => f.Id).ToList();
        var caixasAbertos = await context.Caixas
            .Where(c => c.Status == StatusCaixa.Aberto && ids.Contains(c.FuncionarioId))
            .Select(c => new { c.FuncionarioId, c.Id })
            .ToListAsync(ct);
        var caixaPorFuncionario = caixasAbertos.GroupBy(c => c.FuncionarioId).ToDictionary(g => g.Key, g => g.Max(c => c.Id));

        return funcionarios.Select(f => new OperadorResumo(
            f.Id,
            $"OP-{f.IdExterno ?? f.Id:00}",
            f.Nome,
            f.Supervisor ? "Supervisor / Gerente" : "Operador de Caixa",
            f.Supervisor,
            caixaPorFuncionario.TryGetValue(f.Id, out var caixaId) ? $"Caixa {caixaId:00}" : null,
            f.Desativado ? SituacaoOperador.Desativado
                : string.IsNullOrEmpty(f.PdvKeyHash) ? SituacaoOperador.SemChaveDoPdv : SituacaoOperador.Ativo)).ToList();
    }

    public async Task<IReadOnlyList<ProdutoResumo>> ListarProdutosAsync(string? busca, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var consulta = context.Produtos.AsQueryable();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var padrao = PadraoContem(busca.Trim());
            // Também acha pela CATEGORIA ("mercearia" lista os produtos do grupo): os ids dos grupos cujo nome bate entram na condição.
            var gruposQueBatem = context.Grupos
                .Where(g => g.IdExterno != null && EF.Functions.Like(g.Nome, padrao, "\\"))
                .Select(g => g.IdExterno);
            consulta = consulta.Where(p =>
                EF.Functions.Like(p.Nome, padrao, "\\") ||
                EF.Functions.Like(p.CodigoBarras, padrao, "\\") ||
                EF.Functions.Like(p.Sku, padrao, "\\") ||
                (p.GrupoId != null && gruposQueBatem.Contains(p.GrupoId)));
        }

        var produtos = await consulta.OrderBy(p => p.Nome).Take(LimiteLista).ToListAsync(ct);
        await CatalogoLocalService.PreencherCategoriasAsync(context, produtos, ct);
        return produtos
            .Select(p => new ProdutoResumo(
                p.Id, p.Nome, p.CodigoBarras, p.PrecoVenda, p.EstoqueAtual, p.SyncStatus,
                p.CodigoParaExibicao, p.GrupoNome, p.UnidadeMedida))
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

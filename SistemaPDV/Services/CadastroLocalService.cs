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
    // ninguém ver o motivo): nome e documento obrigatórios, documento com dígitos
    // verificadores válidos e ainda não cadastrado, telefone/e-mail/cidade opcionais mas bem formados. O documento é guardado só com
    // dígitos, que é como a API o devolve e o que o push envia.
    public async Task<ResultadoCriacaoCliente> CriarClienteAsync(NovoClienteDados dados, CancellationToken ct = default)
    {
        var nomeLimpo = dados.Nome?.Trim() ?? string.Empty;
        if (nomeLimpo.Length == 0)
            return ResultadoCriacaoCliente.ComFalha("Informe o nome do cliente.");
        if (nomeLimpo.Length > TamanhoMaximoNome)
            return ResultadoCriacaoCliente.ComFalha($"O nome pode ter no máximo {TamanhoMaximoNome} caracteres.");

        // O documento é obrigatório (como no modal): cliente avulso, sem documento, é o "Consumidor Final" da venda.
        if (string.IsNullOrWhiteSpace(dados.CpfCnpj))
            return ResultadoCriacaoCliente.ComFalha("Informe o CPF ou CNPJ do cliente.");

        // Só pontuação usual além dos dígitos: "123abc" não vira "123" em silêncio.
        var documento = DocumentoValidator.SoDigitos(dados.CpfCnpj);
        var soPontuacaoUsual = dados.CpfCnpj.All(c => char.IsAsciiDigit(c) || ".-/ ".Contains(c));
        if (!soPontuacaoUsual || !(DocumentoValidator.CpfValido(documento) || DocumentoValidator.CnpjValido(documento)))
            return ResultadoCriacaoCliente.ComFalha("CPF/CNPJ inválido — confira os dígitos.");

        if (!TentarLerTelefone(dados.Telefone, out var ddd, out var numeroTelefone))
            return ResultadoCriacaoCliente.ComFalha("Telefone inválido — informe o DDD e o número, como (83) 99999-8888.");

        var email = dados.Email?.Trim();
        if (!string.IsNullOrEmpty(email) && !EmailValido(email))
            return ResultadoCriacaoCliente.ComFalha("E-mail inválido — confira o endereço.");

        if (!TentarLerCidadeUf(dados.CidadeUf, out var cidade, out var uf))
            return ResultadoCriacaoCliente.ComFalha($"Cidade inválida — use no máximo {TamanhoMaximoCidade} caracteres, como João Pessoa - PB.");

        await using var context = contextFactory();

        if (await context.Clientes.AnyAsync(c => c.CpfCnpj == documento, ct))
            return ResultadoCriacaoCliente.ComFalha("Já existe um cliente com este CPF/CNPJ.");

        var cliente = new Cliente
        {
            Nome = nomeLimpo,
            CpfCnpj = documento,
            // Pessoa deriva do documento validado (14 dígitos = CNPJ), como o push faz.
            Pessoa = documento.Length == 14 ? TipoPessoa.Juridica : TipoPessoa.Fisica,
            ContatoNome = ddd is null ? null : nomeLimpo,   // a API só aceita "contato" com nome + DDD + telefone
            ContatoDdd = ddd,
            ContatoTelefone = numeroTelefone,
            ContatoEmail = string.IsNullOrEmpty(email) ? null : email,
            Cidade = cidade,
            Uf = uf,
            SyncStatus = SyncStatus.PendenteSync,
        };
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync(ct);
        return ResultadoCriacaoCliente.ComSucesso(cliente.Id);
    }

    // "João Pessoa - PB" da empresa deste aparelho: o valor que o modal de novo cliente já traz preenchido (a maioria dos clientes
    // de balcão é da mesma cidade). Vazio se a empresa ainda não foi sincronizada.
    public async Task<string> ObterCidadeUfPadraoAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var empresa = await context.Empresas.Select(e => new { e.Cidade, e.Uf }).FirstOrDefaultAsync(ct);
        return empresa is null ? string.Empty : CidadeUf(empresa.Cidade, empresa.Uf) ?? string.Empty;
    }

    private const int TamanhoMaximoCidade = 60;
    private const int TamanhoMaximoEmail = 100;

    // Telefone é opcional. Aceita "(83) 99999-8888", "83999998888", "+55 83 99999-8888": DDD + 8 ou 9 dígitos. A API guarda o
    // DDD à parte, então sai separado (ddd, número). Vazio = sem telefone (true, com os dois nulos); qualquer outra coisa = false.
    private static bool TentarLerTelefone(string? texto, out string? ddd, out string? numero)
    {
        ddd = null;
        numero = null;
        if (string.IsNullOrWhiteSpace(texto))
            return true;

        if (!texto.All(c => char.IsAsciiDigit(c) || "()-+. ".Contains(c)))
            return false;

        var digitos = DocumentoValidator.SoDigitos(texto);
        if (digitos.Length is 12 or 13 && digitos.StartsWith("55"))
            digitos = digitos[2..];   // código do país

        if (digitos.Length is not (10 or 11) || digitos[0] == '0')
            return false;

        ddd = digitos[..2];
        numero = digitos[2..];
        return true;
    }

    private static bool EmailValido(string email) =>
        email.Length <= TamanhoMaximoEmail && EmailRegex.IsMatch(email);

    private static readonly System.Text.RegularExpressions.Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // "João Pessoa - PB" (ou "/" ou ","): cidade e UF de 2 letras. Sem UF reconhecível, o texto inteiro é a cidade.
    // Só fica no banco local — a API pede o CÓDIGO da cidade (c_cidade), que o app não tem.
    private static bool TentarLerCidadeUf(string? texto, out string? cidade, out string? uf)
    {
        cidade = null;
        uf = null;
        var limpo = texto?.Trim();
        if (string.IsNullOrEmpty(limpo))
            return true;

        var m = CidadeUfRegex.Match(limpo);
        if (m.Success)
        {
            cidade = m.Groups[1].Value.Trim();
            uf = m.Groups[2].Value.ToUpperInvariant();
        }
        else
        {
            cidade = limpo;
        }

        return cidade.Length is > 0 and <= TamanhoMaximoCidade;
    }

    private static readonly System.Text.RegularExpressions.Regex CidadeUfRegex =
        new(@"^(.+?)\s*[-/,]\s*([A-Za-z]{2})$", System.Text.RegularExpressions.RegexOptions.Compiled);

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

using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync.Dtos;
using SistemaPDV.Services;

namespace SistemaPDV.Services.Sync;

// Orquestra a sincronização de catálogo: autentica (via SoftcomAuthService) e busca
// cada recurso (via SoftcomApiClient), fazendo upsert local por IdExterno.
//
// Cada método Sincronizar*Async abre seu PRÓPRIO contexto e faz um único SaveChanges
// no final (upserts + marcador de última sincronização juntos) — não existe um
// método genérico de upsert usando ISincronizavel<TKey> aqui de propósito: embora a
// interface exista exatamente pra esse tipo de reuso, `contexto.Set<T>().Where(e =>
// e.IdExterno == id)` com `e` tipado pela INTERFACE (não pela classe concreta) é um
// padrão arriscado no EF Core — o tradutor de LINQ-para-SQL pode não conseguir
// resolver o acesso ao membro da interface pra coluna mapeada da entidade concreta,
// e isso só quebraria em runtime, não em tempo de compilação. Cinco blocos parecidos
// e concretos (repetição pequena, sem risco) venceram uma abstração genérica que
// dependeria de um comportamento não garantido do EF Core.
public class CatalogSyncService
{
    private readonly Func<AppDbContext> contextFactory;
    private readonly SoftcomApiClient apiClient;
    private readonly SegredoProtector segredoProtector;
    private readonly SoftcomAuthService authService;

    public CatalogSyncService(
        Func<AppDbContext> contextFactory, SoftcomApiClient apiClient, SegredoProtector segredoProtector, SoftcomAuthService authService)
    {
        this.contextFactory = contextFactory;
        this.apiClient = apiClient;
        this.segredoProtector = segredoProtector;
        this.authService = authService;
    }

    public async Task<ResultadoSincronizacaoRecurso> SincronizarFormasPagamentoAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        var ultimaSincronizacao = configuracao.UltimaSincronizacaoFormasPagamento?.ToUnixTimeSeconds();

        var resultado = await apiClient.BuscarTudoAsync<FormaPagamentoApiDto>(
            dominio, "api/v2/financeiro/forma-pagamento/page/1", ultimaSincronizacao, accessToken, ct);

        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar formas de pagamento.");

        // Guarda os já processados nesta mesma leva porque a paginação da API pode
        // repetir o mesmo registro em mais de uma página — sem isso, um IdExterno
        // repetido dentro do mesmo lote criaria duas entidades novas (ainda não
        // salvas, então a consulta ao banco não as encontraria) e o SaveChanges no
        // final quebraria no índice único de IdExterno.
        //
        // Busca todos os existentes de uma vez (1 SELECT) em vez de um FirstOrDefaultAsync
        // por item do lote — revisão de código apontou N+1 aqui: um lote de 200 formas de
        // pagamento fazia até 200 idas ao banco só pra descobrir quais já existiam.
        var idsExternos = resultado.Itens.Select(i => (int?)i.Id).Distinct().ToList();
        var processados = await context.FormasPagamento
            .Where(f => idsExternos.Contains(f.IdExterno))
            .ToDictionaryAsync(f => f.IdExterno!.Value, ct);

        foreach (var dto in resultado.Itens)
        {
            if (!processados.TryGetValue(dto.Id, out var entidade))
            {
                entidade = new FormaPagamento { Nome = dto.Nome ?? string.Empty, Tipo = dto.Tipo ?? string.Empty };
                context.FormasPagamento.Add(entidade);
                processados[dto.Id] = entidade;
            }

            entidade.IdExterno = dto.Id;
            entidade.Nome = dto.Nome ?? entidade.Nome;
            entidade.Tipo = dto.Tipo ?? entidade.Tipo;
            entidade.Padrao = dto.Padrao;
            entidade.CodigoNfce = dto.CodigoNfce;
            entidade.CodigoTransacaoSitef = dto.CodigoTransacaoSitef;
            entidade.CarteiraDigital = dto.CarteiraDigital;
            entidade.Ordem = dto.Ordem;
            entidade.PdvPos = dto.PdvPos;
            entidade.PreVenda = dto.PreVenda;
            entidade.AtalhoNumero = dto.AtalhoNumero;
            entidade.PermissaoSupervisor = dto.PermissaoSupervisor;
            entidade.SyncStatus = SyncStatus.Sincronizado;
        }

        if (resultado.DateSync is { } dateSync)
            configuracao.UltimaSincronizacaoFormasPagamento = DateTimeOffset.FromUnixTimeSeconds(dateSync);

        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(resultado.Itens.Count);
    }

    public async Task<ResultadoSincronizacaoRecurso> SincronizarClientesAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        var ultimaSincronizacao = configuracao.UltimaSincronizacaoClientes?.ToUnixTimeSeconds();

        var resultado = await apiClient.BuscarTudoAsync<ClienteApiDto>(
            dominio, "softauth/api/v2/clientes/clientes", ultimaSincronizacao, accessToken, ct);

        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar clientes.");

        var idsExternos = resultado.Itens.Select(i => (int?)i.Id).Distinct().ToList();
        var processados = await context.Clientes
            .Where(c => idsExternos.Contains(c.IdExterno))
            .ToDictionaryAsync(c => c.IdExterno!.Value, ct);

        foreach (var dto in resultado.Itens)
        {
            if (!processados.TryGetValue(dto.Id, out var entidade))
            {
                entidade = new Cliente { Nome = dto.Nome ?? string.Empty };
                context.Clientes.Add(entidade);
                processados[dto.Id] = entidade;
            }

            entidade.IdExterno = dto.Id;
            entidade.Nome = dto.Nome ?? entidade.Nome;
            entidade.RazaoSocial = dto.RazaoSocial;
            entidade.Pessoa = string.Equals(dto.Pessoa, "JURIDICA", StringComparison.OrdinalIgnoreCase) ? TipoPessoa.Juridica : TipoPessoa.Fisica;
            entidade.CpfCnpj = dto.CpfCnpj;
            entidade.Rg = dto.Rg;
            entidade.InscricaoEstadual = dto.InscricaoEstadual;
            entidade.InscricaoMunicipal = dto.InscricaoMunicipal;
            entidade.ContribuinteIcms = dto.ContribuinteIcms;
            entidade.IndicadorFinalidade = dto.IndicadorFinalidade;
            entidade.Bloqueado = dto.Bloqueado == "1";
            entidade.Observacao = dto.Observacao;
            entidade.ContatoNome = dto.ContatoNome;
            entidade.ContatoDdd = dto.ContatoDdd;
            entidade.ContatoTelefone = dto.ContatoTelefone;
            entidade.ContatoEmail = dto.ContatoEmail;
            entidade.Cep = dto.Cep;
            entidade.Endereco = dto.Endereco;
            entidade.Numero = dto.Numero;
            entidade.Complemento = dto.Complemento;
            entidade.Bairro = dto.Bairro;
            entidade.PontoReferencia = dto.PontoReferencia;
            entidade.Cidade = dto.Cidade;
            entidade.CidadeId = dto.CidadeId;
            entidade.Uf = dto.Uf;
            entidade.TipoClienteId = dto.TipoClienteId;
            entidade.TipoClienteNome = dto.TipoClienteNome;
            entidade.FuncionarioId = dto.FuncionarioId;
            entidade.FuncionarioNome = dto.FuncionarioNome;

            // TabelaPreco é obrigatório ter Descricao não-nula quando existe (ver
            // Models/TabelaPreco.cs) — a API sempre manda uma na prática, mas se um
            // dia vier sem, cai num default em vez de quebrar o SaveChanges.
            entidade.TabelaPreco = dto.TabelaPreco is null
                ? null
                : new TabelaPreco { IdExterno = dto.TabelaPreco.Id, Descricao = dto.TabelaPreco.Descricao ?? "PADRAO" };

            entidade.SyncStatus = SyncStatus.Sincronizado;
        }

        if (resultado.DateSync is { } dateSync)
            configuracao.UltimaSincronizacaoClientes = DateTimeOffset.FromUnixTimeSeconds(dateSync);

        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(resultado.Itens.Count);
    }

    public async Task<ResultadoSincronizacaoRecurso> SincronizarProdutosAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        var ultimaSincronizacao = configuracao.UltimaSincronizacaoProdutos?.ToUnixTimeSeconds();

        var resultado = await apiClient.BuscarTudoAsync<ProdutoApiDto>(
            dominio, "softauth/api/v2/produtos/produtos", ultimaSincronizacao, accessToken, ct);

        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar produtos.");

        var idsExternos = resultado.Itens.Select(i => (int?)i.Id).Distinct().ToList();
        var processados = await context.Produtos
            .Include(p => p.Imagens)
            .Where(p => idsExternos.Contains(p.IdExterno))
            .ToDictionaryAsync(p => p.IdExterno!.Value, ct);

        foreach (var dto in resultado.Itens)
        {
            if (!processados.TryGetValue(dto.Id, out var entidade))
            {
                entidade = new Produto { Nome = dto.Nome ?? string.Empty };
                context.Produtos.Add(entidade);
                processados[dto.Id] = entidade;
            }

            entidade.IdExterno = dto.Id;
            entidade.Sku = dto.Sku;
            entidade.CodigoBarras = dto.CodigoBarras;
            entidade.Nome = dto.Nome ?? entidade.Nome;
            entidade.NomeOriginal = dto.NomeOriginal;
            entidade.Fabricante = dto.Fabricante;
            entidade.Referencia = dto.Referencia;
            entidade.GrupoId = dto.GrupoId;
            entidade.EstoqueAtual = dto.Estoque;
            entidade.UnidadeMedida = dto.UnidadeMedida;
            entidade.Peso = dto.Peso;
            entidade.PrecoVenda = dto.PrecoVenda;
            entidade.PrecoCompra = dto.PrecoCompra;
            entidade.MargemLucro = dto.MargemLucro;
            entidade.Ncm = dto.Ncm;
            entidade.Cest = dto.Cest;
            entidade.CodigoBeneficioFiscal = dto.CodigoBeneficioFiscal;
            entidade.StatusFiscal = dto.StatusFiscal;
            entidade.CodigoNfe = dto.CodigoNfe;
            entidade.Vender = dto.Vender is null or not 0;
            entidade.RestricaoIdade = dto.RestricaoIdade;
            entidade.Hortifruit = dto.Hortifruit;
            entidade.Observacao = dto.Observacao;
            entidade.PromocaoPreco = dto.PromocaoPreco;
            entidade.PromocaoValidade = dto.PromocaoValidade;
            entidade.PromocaoQuantidade = dto.PromocaoQuantidade;

            // Sem chave natural própria dentro da lista de imagens — mais simples e
            // seguro substituir a coleção inteira a cada sync do que tentar casar
            // imagem por imagem.
            entidade.Imagens.Clear();
            foreach (var imagemDto in dto.ProdutoImagem)
            {
                if (string.IsNullOrWhiteSpace(imagemDto.ArquivoOriginal))
                    continue;

                entidade.Imagens.Add(new ImagemProduto
                {
                    Descricao = imagemDto.Descricao,
                    ArquivoOriginal = imagemDto.ArquivoOriginal,
                    ArquivoThumbnail = imagemDto.ArquivoThumbnail,
                    Tipo = imagemDto.Tipo,
                });
            }

            entidade.SyncStatus = SyncStatus.Sincronizado;
        }

        if (resultado.DateSync is { } dateSync)
            configuracao.UltimaSincronizacaoProdutos = DateTimeOffset.FromUnixTimeSeconds(dateSync);

        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(resultado.Itens.Count);
    }

    public async Task<ResultadoSincronizacaoRecurso> SincronizarFuncionariosAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        var ultimaSincronizacao = configuracao.UltimaSincronizacaoFuncionarios?.ToUnixTimeSeconds();

        var resultado = await apiClient.BuscarTudoAsync<FuncionarioApiDto>(
            dominio, "api/v2/funcionarios", ultimaSincronizacao, accessToken, ct);

        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar funcionários.");

        var idsExternos = resultado.Itens.Select(i => (int?)i.Id).Distinct().ToList();
        var processados = await context.Funcionarios
            .Where(f => idsExternos.Contains(f.IdExterno))
            .ToDictionaryAsync(f => f.IdExterno!.Value, ct);

        foreach (var dto in resultado.Itens)
        {
            if (!processados.TryGetValue(dto.Id, out var entidade))
            {
                entidade = new Funcionario { Nome = dto.Nome ?? string.Empty };
                context.Funcionarios.Add(entidade);
                processados[dto.Id] = entidade;
            }

            entidade.IdExterno = dto.Id;
            entidade.Nome = dto.Nome ?? entidade.Nome;
            entidade.Cpf = dto.Cpf;
            entidade.Desativado = dto.Desativado;
            entidade.Supervisor = dto.Supervisor;
            // Só sobrescreve se a API mandou uma pdv_key dessa vez — um payload sem
            // `usuario` (parcial, mudança de permissão, bug transitório da API) não
            // pode apagar um hash que já funcionava; revisão de código achou que a
            // versão anterior gravava null incondicionalmente, travando o login do
            // operador sem erro nenhum visível em lugar nenhum.
            var novoPdvKeyHash = PdvKeyHasher.Hash(dto.Usuario?.PdvKey);
            if (novoPdvKeyHash is not null)
                entidade.PdvKeyHash = novoPdvKeyHash;
            entidade.SyncStatus = SyncStatus.Sincronizado;
        }

        if (resultado.DateSync is { } dateSync)
            configuracao.UltimaSincronizacaoFuncionarios = DateTimeOffset.FromUnixTimeSeconds(dateSync);

        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(resultado.Itens.Count);
    }

    // Único método que lida com dado sensível (certificado digital + senha) — a
    // restrição de plataforma (SegredoProtector é Windows-only) fica só aqui, não na
    // classe inteira, mesma lógica de granularidade aplicada em SoftcomAuthService.
    [SupportedOSPlatform("windows")]
    public async Task<ResultadoSincronizacaoRecurso> SincronizarEmpresaAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        var ultimaSincronizacao = configuracao.UltimaSincronizacaoEmpresa?.ToUnixTimeSeconds();

        // Na prática, o dispositivo só enxerga a própria empresa — mesmo o endpoint
        // sendo paginado (ver Open Questions de SPEC-catalog-sync.md).
        var resultado = await apiClient.BuscarTudoAsync<EmpresaApiDto>(
            dominio, "api/v2/empresa/empresas/1", ultimaSincronizacao, accessToken, ct);

        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar empresa.");

        foreach (var dto in resultado.Itens)
        {
            var entidade = await context.Empresas.FirstOrDefaultAsync(e => e.IdExterno == dto.EmpresaId, ct);
            if (entidade is null)
            {
                entidade = new Empresa { RazaoSocial = dto.EmpresaRazaoSocial ?? string.Empty, Cnpj = dto.EmpresaCnpj ?? string.Empty };
                context.Empresas.Add(entidade);
            }

            entidade.IdExterno = dto.EmpresaId;
            entidade.RazaoSocial = dto.EmpresaRazaoSocial ?? entidade.RazaoSocial;
            entidade.NomeFantasia = dto.EmpresaFantasia;
            entidade.Cnpj = dto.EmpresaCnpj ?? entidade.Cnpj;
            entidade.Email = dto.EmpresaEmail;
            entidade.Cep = dto.EmpresaCep;
            entidade.Endereco = dto.EmpresaEndereco;
            entidade.Numero = dto.EmpresaNumero;
            entidade.Complemento = dto.EmpresaComplemento;
            entidade.Bairro = dto.EmpresaBairro;
            entidade.Cidade = dto.EmpresaCidade;
            entidade.Uf = dto.EmpresaUf;
            entidade.ModuloFiscal = dto.EmpresaModuloFiscal;
            entidade.NfceSerie = dto.EmpresaNfceSerie;
            entidade.NfceNumeroCaixa = dto.EmpresaNfceNumeroCaixa;
            entidade.NfceAmbiente = dto.EmpresaNfceAmbiente;
            entidade.NfceModelo = dto.EmpresaNfceModelo;
            entidade.NfceProximoNumero = dto.EmpresaNfceProximoNumero;
            entidade.CertificadoProtegido = ProtegerCertificado(dto.EmpresaCertificado, dto.EmpresaCertificadoSenha);
            entidade.SyncStatus = SyncStatus.Sincronizado;
        }

        if (resultado.DateSync is { } dateSync)
            configuracao.UltimaSincronizacaoEmpresa = DateTimeOffset.FromUnixTimeSeconds(dateSync);

        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(resultado.Itens.Count);
    }

    // Certificado e senha vêm em campos separados da API, mas Empresa só tem UM
    // campo protegido — combina os dois num JSON pequeno antes de proteger com DPAPI,
    // assim os dois ficam protegidos juntos, sem precisar de uma segunda coluna.
    [SupportedOSPlatform("windows")]
    private string? ProtegerCertificado(string? certificado, string? senha)
    {
        if (string.IsNullOrEmpty(certificado) && string.IsNullOrEmpty(senha))
            return null;

        var json = JsonSerializer.Serialize(new { certificado, senha });
        return segredoProtector.Proteger(json);
    }

    // Ponto de entrada único: autentica uma vez e sincroniza os cinco recursos em
    // sequência. Se um recurso falhar no meio, os anteriores já sincronizados
    // permanecem persistidos (cada Sincronizar*Async já salva por conta própria) —
    // não é uma transação única tudo-ou-nada.
    [SupportedOSPlatform("windows")]
    public async Task<ResultadoSincronizacaoCompleta> SincronizarTudoAsync(CancellationToken ct = default)
    {
        ConfiguracaoSincronizacao configuracao;
        await using (var context = contextFactory())
        {
            configuracao = await ObterConfiguracaoAsync(context, ct);
        }

        var (autenticado, mensagemAuth, accessToken) = await authService.ObterTokenAsync(configuracao, ct);
        if (!autenticado || accessToken is null)
            return new ResultadoSincronizacaoCompleta { AutenticacaoSucesso = false, MensagemAutenticacao = mensagemAuth };

        var formasPagamento = await SincronizarFormasPagamentoAsync(accessToken, ct);
        var clientes = await SincronizarClientesAsync(accessToken, ct);
        var produtos = await SincronizarProdutosAsync(accessToken, ct);
        var funcionarios = await SincronizarFuncionariosAsync(accessToken, ct);
        var empresa = await SincronizarEmpresaAsync(accessToken, ct);

        return new ResultadoSincronizacaoCompleta
        {
            AutenticacaoSucesso = true,
            FormasPagamento = formasPagamento,
            Clientes = clientes,
            Produtos = produtos,
            Funcionarios = funcionarios,
            Empresa = empresa,
        };
    }

    private static async Task<ConfiguracaoSincronizacao> ObterConfiguracaoAsync(AppDbContext context, CancellationToken ct) =>
        await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Configuração de sincronização não encontrada — configure a URL da API antes de sincronizar.");
}

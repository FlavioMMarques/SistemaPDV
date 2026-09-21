using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
public partial class CatalogSyncService
{
    private readonly Func<AppDbContext> contextFactory;
    private readonly SoftcomApiClient apiClient;
    private readonly SegredoProtector segredoProtector;
    private readonly SoftcomAuthService authService;
    private readonly TimeProvider timeProvider;

    public CatalogSyncService(
        Func<AppDbContext> contextFactory, SoftcomApiClient apiClient, SegredoProtector segredoProtector, SoftcomAuthService authService,
        TimeProvider? timeProvider = null)
    {
        this.contextFactory = contextFactory;
        this.apiClient = apiClient;
        this.segredoProtector = segredoProtector;
        this.authService = authService;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime AgoraUtc => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<ResultadoSincronizacaoRecurso> SincronizarFormasPagamentoAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        var ultimaSincronizacao = configuracao.UltimaSincronizacaoFormasPagamento?.ToUnixTimeSeconds();

        var resultado = await apiClient.BuscarTudoAsync<FormaPagamentoApiDto>(
            dominio, SoftcomRotas.FormasPagamento, ultimaSincronizacao, accessToken, ct);

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
            dominio, SoftcomRotas.Clientes, ultimaSincronizacao, accessToken, ct);

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
            entidade.Bloqueado = dto.Bloqueado;
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
        // Produtos sincronizados antes de o app guardar o produto_id (a venda precisa dele) nunca
        // viriam de novo numa sincronização incremental — enquanto houver algum sem, busca tudo.
        var faltaProdutoId = await context.Produtos.AnyAsync(p => p.IdExterno != null && p.ProdutoIdApi == null, ct);
        var ultimaSincronizacao = faltaProdutoId ? null : configuracao.UltimaSincronizacaoProdutos?.ToUnixTimeSeconds();

        var resultado = await apiClient.BuscarTudoAsync<ProdutoApiDto>(
            dominio, SoftcomRotas.Produtos, ultimaSincronizacao, accessToken, ct);

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
            entidade.ProdutoIdApi = dto.ProdutoId;
            entidade.Sku = dto.Sku;
            entidade.CodigoBarras = dto.CodigoBarras;
            entidade.Nome = dto.Nome ?? entidade.Nome;
            entidade.NomeOriginal = dto.NomeOriginal;
            entidade.Fabricante = dto.Fabricante;
            entidade.Referencia = dto.Referencia;
            entidade.GrupoId = dto.GrupoId;
            // A API manda o estoque como texto decimal ("15.000"): o modelo local é inteiro
            // (unidades) — arredonda, senão um item vendido a peso (kg) truncaria pra baixo.
            entidade.EstoqueAtual = (int)Math.Round(dto.Estoque, MidpointRounding.AwayFromZero);
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
            entidade.Vender = dto.Vender ?? true;
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
            dominio, SoftcomRotas.Funcionarios, ultimaSincronizacao, accessToken, ct);

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
            //
            // A API manda a pdv_key JÁ como hash bcrypt (nunca a chave): grava-se como veio.
            // Qualquer outra coisa (vazio, ou um valor em claro se a API mudar um dia) é
            // ignorada — nunca se persiste uma chave em claro (ver PdvKeyHasher).
            var pdvKeyDaApi = dto.Usuario?.PdvKey;
            if (PdvKeyHasher.EhHashBcrypt(pdvKeyDaApi))
                entidade.PdvKeyHash = pdvKeyDaApi;
            entidade.SyncStatus = SyncStatus.Sincronizado;
        }

        if (resultado.DateSync is { } dateSync)
            configuracao.UltimaSincronizacaoFuncionarios = DateTimeOffset.FromUnixTimeSeconds(dateSync);

        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(resultado.Itens.Count);
    }

    // Outbox de cliente criado localmente (Task 48): mesmo desenho de CaixaSyncService/
    // VendaSyncService, só que empurrando um cadastro em vez de uma operação. Envia
    // DADO PESSOAL (nome, CPF/CNPJ) + o access token, então (revisão de segurança):
    //  - só sai por HTTPS (ou loopback, pra desenvolvimento) — ver ConexaoSegura;
    //  - o documento é validado localmente antes de sair, e o erro gravado nunca o repete;
    //  - o payload é uma allowlist (ClienteNovoRequestDto), não a entidade;
    //  - a resposta é tratada como não confiável (id validado, corpo nunca lança nem
    //    entra sem limite no banco — ver ErroApiExtractor).
    public async Task<ResultadoSincronizacaoRecurso> SincronizarClienteNovoAsync(int clienteId, string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var cliente = await context.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null)
            return ResultadoSincronizacaoRecurso.ComFalha("Cliente não encontrado.");

        // Já tem par na API (veio de lá, ou já foi enviado) — nada a empurrar.
        if (cliente.IdExterno is not null)
            return ResultadoSincronizacaoRecurso.ComSucesso(0);

        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);

        // Problema de configuração, não do cliente: devolve falha SEM marcar o cliente
        // (ele continua PendenteSync, sem UltimoErroSync) — mesma lógica de "dependência
        // ainda não sincronizou" em VendaSyncService.
        if (!ConexaoSegura.Permitida(dominio))
            return ResultadoSincronizacaoRecurso.ComFalha(ConexaoSegura.MensagemRecusa);

        var documento = DocumentoValidator.SoDigitos(cliente.CpfCnpj);
        var temDocumento = documento.Length > 0;
        if (temDocumento && !DocumentoValidator.CpfValido(documento) && !DocumentoValidator.CnpjValido(documento))
            return await MarcarFalhaClienteAsync(context, cliente, "CPF/CNPJ inválido — corrija o cadastro para sincronizar.", ct);

        // Pessoa vem do documento já validado (14 dígitos = CNPJ), não do campo local —
        // um CNPJ marcado como Física geraria 422 (razão social obrigatória).
        var juridica = temDocumento ? documento.Length == 14 : cliente.Pessoa == TipoPessoa.Juridica;
        var nome = cliente.Nome.Trim();

        var corpo = new ClienteNovoRequestDto
        {
            Pessoa = juridica ? "JURIDICA" : "FISICA",
            Nome = nome,
            CpfCnpj = temDocumento ? documento : null,
            // Sempre enviada, também para pessoa física (usa o nome): o Swagger diz "obrigatório só para JURIDICA", mas a API
            // real grava numa coluna que não aceita nulo e recusa o cliente (erro 1048, 1º envio real em 2026-09-21).
            RazaoSocial = string.IsNullOrWhiteSpace(cliente.RazaoSocial) ? nome : cliente.RazaoSocial.Trim(),
        };

        var resultado = await apiClient.EnviarAsync(
            HttpMethod.Post, SoftcomRotas.ClientesCriar(dominio), corpo, accessToken, ct);

        if (resultado.Tipo == ResultadoEnvioTipo.ConexaoInsegura)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Conteudo);

        if (resultado.Tipo != ResultadoEnvioTipo.Sucesso)
            return await MarcarFalhaClienteAsync(context, cliente, ErroApiExtractor.Extrair(resultado.Conteudo), ct);

        // Nunca confiar cegamente no id devolvido: 0/negativo/ausente não é um id de cliente.
        var resposta = SoftcomJson.TentarDesserializar<ClienteNovoRespostaDto>(resultado.Conteudo);
        if (resposta?.Data is not { Id: > 0 } dados)
            return await MarcarFalhaClienteAsync(context, cliente, $"A resposta não trouxe um id de cliente válido: {ErroApiExtractor.Extrair(resultado.Conteudo)}", ct);

        cliente.IdExterno = dados.Id;
        cliente.SyncStatus = SyncStatus.Sincronizado;
        PoliticaRetentativa.Zerar(cliente);
        cliente.UltimoErroSync = null;
        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(1);
    }

    // Percorre os clientes criados localmente e ainda não confirmados (sem IdExterno,
    // PendenteSync ou FalhaSync — FalhaSync fica elegível pra retry, como em Venda).
    // Um cliente com erro não impede os outros: não é tudo-ou-nada.
    public async Task<ResultadoSincronizacaoRecurso> SincronizarClientesNovosPendentesAsync(string accessToken, CancellationToken ct = default)
    {
        List<int> clienteIds;
        await using (var context = contextFactory())
        {
            clienteIds = await context.Clientes
                .Where(c => c.IdExterno == null && c.SyncStatus != SyncStatus.Sincronizado)
                .Where(PoliticaRetentativa.Elegivel<Cliente>(AgoraUtc))   // espera crescente/teto (PoliticaRetentativa)
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        var totalSincronizados = 0;
        foreach (var clienteId in clienteIds)
        {
            try
            {
                var resultado = await SincronizarClienteNovoAsync(clienteId, accessToken, ct);
                if (resultado.Sucesso)
                    totalSincronizados += resultado.Quantidade;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Mesmo raciocínio de VendaSyncService.SincronizarVendasPendentesAsync: um
                // cliente com falha inesperada (ex: UrlApi malformada) não trava o lote;
                // ele simplesmente segue pendente e é tentado de novo no próximo ciclo.
                Registro.Erro("Envio", $"Exceção ao enviar o cliente {clienteId}", ex);
            }
        }

        return ResultadoSincronizacaoRecurso.ComSucesso(totalSincronizados);
    }

    private Task<ResultadoSincronizacaoRecurso> MarcarFalhaClienteAsync(
        AppDbContext context, Cliente cliente, string mensagem, CancellationToken ct)
    {
        var agora = AgoraUtc;
        return OutboxHelper.MarcarFalhaAsync(context, cliente, mensagem, (c, m) =>
        {
            c.SyncStatus = SyncStatus.FalhaSync;
            c.UltimoErroSync = PoliticaRetentativa.RegistrarFalha(c, agora, m);
        }, ct);
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

        // A API devolve TODAS as empresas do cliente (4, no dispositivo real), não só a deste
        // dispositivo — quem diz qual é a dele é o link de vínculo (empresa_cnpj).
        var resultado = await apiClient.BuscarTudoAsync<EmpresaApiDto>(
            dominio, SoftcomRotas.Empresa, ultimaSincronizacao, accessToken, ct);

        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar empresa.");

        // Só a empresa do dispositivo é gravada: as outras não servem pra nada aqui e trariam junto o
        // certificado digital e a senha delas pro banco local.
        var cnpjDoDispositivo = SoftcomAuthService.ExtrairEmpresaCnpj(configuracao.UrlApi);
        var empresasLocais = await context.Empresas.ToListAsync(ct);

        // Versões anteriores gravavam TODAS as empresas (com o certificado digital e a senha das
        // outras): sobra local a remover. Não há FK apontando pra Empresa e é dado que se baixa de novo.
        if (cnpjDoDispositivo is not null)
        {
            var deOutras = empresasLocais.Where(e => DocumentoValidator.SoDigitos(e.Cnpj) != cnpjDoDispositivo).ToList();
            if (deOutras.Count > 0)
            {
                context.Empresas.RemoveRange(deOutras);
                await context.SaveChangesAsync(ct);
                empresasLocais.RemoveAll(deOutras.Contains);
            }
        }

        List<EmpresaApiDto> itens;
        if (cnpjDoDispositivo is not null)
        {
            itens = resultado.Itens.Where(i => DocumentoValidator.SoDigitos(i.EmpresaCnpj) == cnpjDoDispositivo).ToList();
        }
        else if (resultado.Itens.Count <= 1)
        {
            itens = resultado.Itens.ToList();
        }
        else
        {
            // Link sem CNPJ (vínculo antigo) e várias empresas: não dá pra adivinhar — só atualiza a que já
            // temos, se houver.
            var idLocal = empresasLocais.Select(e => e.IdExterno).FirstOrDefault();
            if (idLocal is null)
                return ResultadoSincronizacaoRecurso.ComFalha("Não foi possível identificar a empresa deste dispositivo (o link de vínculo não traz o CNPJ e a API devolveu várias empresas) — vincule o dispositivo de novo.");

            itens = resultado.Itens.Where(i => i.EmpresaId == idLocal).ToList();
        }

        if (itens.Count == 0)
        {
            // Sincronização incremental: nada mudou pra empresa deste dispositivo (a página pode vir vazia
            // ou só com outras empresas). Só é erro se ainda não temos a empresa local.
            var jaTemLocal = cnpjDoDispositivo is null
                ? empresasLocais.Count > 0
                : empresasLocais.Any(e => DocumentoValidator.SoDigitos(e.Cnpj) == cnpjDoDispositivo);

            return resultado.Itens.Count == 0 || jaTemLocal
                ? ResultadoSincronizacaoRecurso.ComSucesso(0)
                : ResultadoSincronizacaoRecurso.ComFalha("Nenhuma das empresas devolvidas pela API tem o CNPJ deste dispositivo (do link de vínculo) — confira o vínculo.");
        }

        foreach (var dto in itens)
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
            entidade.NfceSerie = dto.EmpresaNfceSerie ?? 0;
            entidade.NfceNumeroCaixa = dto.EmpresaNfceNumeroCaixa ?? 0;   // null na API real quando não configurado
            entidade.NfceAmbiente = dto.EmpresaNfceAmbiente ?? 0;
            entidade.NfceModelo = dto.EmpresaNfceModelo ?? 0;
            entidade.NfceProximoNumero = dto.EmpresaNfceProximoNumero ?? 0;
            entidade.CertificadoProtegido = ProtegerCertificado(dto.EmpresaCertificado, dto.EmpresaCertificadoSenha);
            entidade.SyncStatus = SyncStatus.Sincronizado;
        }

        if (resultado.DateSync is { } dateSync)
            configuracao.UltimaSincronizacaoEmpresa = DateTimeOffset.FromUnixTimeSeconds(dateSync);

        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(itens.Count);
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
        var cartoes = await SincronizarCartoesAsync(accessToken, ct);
        var grupos = await SincronizarGruposAsync(accessToken, ct);

        return new ResultadoSincronizacaoCompleta
        {
            AutenticacaoSucesso = true,
            FormasPagamento = formasPagamento,
            Clientes = clientes,
            Produtos = produtos,
            Funcionarios = funcionarios,
            Empresa = empresa,
            Cartoes = cartoes,
            Grupos = grupos,
        };
    }

    private static async Task<ConfiguracaoSincronizacao> ObterConfiguracaoAsync(AppDbContext context, CancellationToken ct) =>
        await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Configuração de sincronização não encontrada — configure a URL da API antes de sincronizar.");
}

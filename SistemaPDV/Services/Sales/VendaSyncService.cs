using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales.Dtos;
using SistemaPDV.Services.Sync;
using SistemaPDV.Services;

namespace SistemaPDV.Services.Sales;

// Outbox da venda: VendaService grava local e instantâneo; esta classe, quando há
// rede, tenta confirmar isso com a API. Diferente de caixa (que só depende do
// Funcionario), a venda referencia várias entidades sincronizáveis — se qualquer
// uma delas ainda não tem IdExterno, a venda fica PendenteSync esperando (não vira
// FalhaSync: falta de sincronização de uma dependência não é a mesma coisa que um
// erro real da API, mesmo raciocínio já usado em CaixaSyncService).
public class VendaSyncService
{
    private readonly Func<AppDbContext> contextFactory;
    private readonly SoftcomApiClient apiClient;
    private readonly TimeProvider timeProvider;

    public VendaSyncService(Func<AppDbContext> contextFactory, SoftcomApiClient apiClient, TimeProvider? timeProvider = null)
    {
        this.contextFactory = contextFactory;
        this.apiClient = apiClient;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime AgoraUtc => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<ResultadoSincronizacaoRecurso> SincronizarVendaAsync(Guid vendaId, string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var venda = await context.Vendas
            .Include(v => v.Itens)
            .Include(v => v.Pagamentos)
            .FirstOrDefaultAsync(v => v.Id == vendaId, ct);

        if (venda is null)
            return ResultadoSincronizacaoRecurso.ComFalha("Venda não encontrada.");

        var caixa = await context.Caixas.FindAsync(new object[] { venda.CaixaId }, ct)
            ?? throw new InvalidOperationException("Caixa da venda não encontrado.");

        var funcionario = await context.Funcionarios.FindAsync(new object[] { caixa.FuncionarioId }, ct);
        if (funcionario?.IdExterno is not { } operadorId)
            return ResultadoSincronizacaoRecurso.ComFalha("Funcionário do caixa ainda não sincronizou — sincronize funcionários antes.");

        var configuracao = await context.ConfiguracoesSincronizacao.FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Configuração de sincronização não encontrada.");

        // A empresa é a DESTE dispositivo (o CNPJ vem no link de vínculo), não "a primeira que
        // houver": o app já só grava a empresa do dispositivo, mas dados de antes disso podem ter
        // outras. Sem CNPJ no link (vínculo antigo), cai na única empresa local.
        var cnpjDoDispositivo = SoftcomAuthService.ExtrairEmpresaCnpj(configuracao.UrlApi);
        var empresas = await context.Empresas.ToListAsync(ct);
        var empresa = cnpjDoDispositivo is null
            ? empresas.FirstOrDefault()
            : empresas.FirstOrDefault(e => DocumentoValidator.SoDigitos(e.Cnpj) == cnpjDoDispositivo);
        if (empresa?.IdExterno is not { } empresaId)
            return ResultadoSincronizacaoRecurso.ComFalha("Empresa deste dispositivo ainda não sincronizou — sincronize a empresa antes.");

        int clienteIdExterno;
        if (venda.ClienteId is { } clienteIdLocal)
        {
            var cliente = await context.Clientes.FindAsync(new object[] { clienteIdLocal }, ct);
            if (cliente?.IdExterno is not { } idExterno)
                return ResultadoSincronizacaoRecurso.ComFalha("Cliente da venda ainda não sincronizou.");

            clienteIdExterno = idExterno;
        }
        else if (configuracao.ClienteConsumidorFinalIdExterno is { } consumidorFinalId)
        {
            clienteIdExterno = consumidorFinalId;
        }
        else
        {
            return ResultadoSincronizacaoRecurso.ComFalha("Id do cliente \"Consumidor Final\" não configurado — configure antes de sincronizar vendas avulsas.");
        }

        var produtoIdsLocais = venda.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await context.Produtos.Where(p => produtoIdsLocais.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var formaPagamentoIdsLocais = venda.Pagamentos.Select(p => p.FormaPagamentoId).Distinct().ToList();
        var formasPagamento = await context.FormasPagamento.Where(f => formaPagamentoIdsLocais.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        var produtosDto = new List<VendaProdutoRequestDto>();
        foreach (var item in venda.Itens)
        {
            if (!produtos.TryGetValue(item.ProdutoId, out var produto) || produto.IdExterno is not { } produtoIdExterno)
                return ResultadoSincronizacaoRecurso.ComFalha("Um dos produtos da venda ainda não sincronizou.");

            // O produto tem DOIS ids na API. Sem o produto_id guardado (produto sincronizado antes de o
            // app guardar isso), mandar o `id` no lugar registraria OUTRO produto na venda — espera a
            // próxima sincronização de produtos, que preenche (e fica PendenteSync, sem marcar falha).
            if (produto.ProdutoIdApi is not { } produtoIdApi)
                return ResultadoSincronizacaoRecurso.ComFalha("Um dos produtos da venda ainda não tem o id-base da API — aguarde a próxima sincronização de produtos.");

            produtosDto.Add(new VendaProdutoRequestDto
            {
                ProdutoId = produtoIdApi,
                ProdutoEmpresaGradeId = produtoIdExterno,
                Preco = item.PrecoUnitario,
                PrecoCompra = produto.PrecoCompra ?? 0m,
                Quantidade = item.Quantidade,
                DescontoValorItem = item.DescontoItem,
                AcrescimoValorItem = item.AcrescimoItem,
            });
        }

        var pagamentosDto = new List<VendaPagamentoRequestDto>();
        foreach (var pagamento in venda.Pagamentos)
        {
            if (!formasPagamento.TryGetValue(pagamento.FormaPagamentoId, out var forma) || forma.IdExterno is not { } formaIdExterno)
                return ResultadoSincronizacaoRecurso.ComFalha("Uma das formas de pagamento da venda ainda não sincronizou.");

            pagamentosDto.Add(new VendaPagamentoRequestDto
            {
                FormaPagamentoId = formaIdExterno,
                ApiNomePagamento = forma.Nome,
                ApiCodigoPagamento = forma.CodigoNfce ?? string.Empty,
                ValorPagamento = pagamento.Valor,
                ValorParcela = pagamento.Valor,
                ValorRecebido = pagamento.Valor,
            });
        }

        var payload = new VendaRequestDto
        {
            Guid = venda.Id.ToString(),
            DataHora = new DateTimeOffset(venda.DataHora).ToUnixTimeSeconds(),
            EmpresaId = empresaId,
            // usuario_id e funcionario_id recebem o mesmo Funcionario.IdExterno —
            // confirmado com o usuário (2026-09-18), consistente com o padrão já
            // observado em caixa-funcoes/abrir (operador_id e usuario_abertura_id
            // idênticos no exemplo real da API).
            UsuarioId = operadorId,
            FuncionarioId = operadorId,
            ClienteId = clienteIdExterno,
            NumeroDocumento = venda.NumeroPedido.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Cancelada = false,
            Bloqueada = false,
            CaixaData = caixa.DataCaixa.ToString("yyyy-MM-dd"),
            CaixaTurno = caixa.Turno,
            CaixaFuncoesId = caixa.IdExterno,
            Produtos = produtosDto,
            Pagamentos = pagamentosDto,
        };

        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);

        var resultado = await apiClient.EnviarAsync(HttpMethod.Post, SoftcomRotas.Vendas(dominio), payload, accessToken, ct);

        // Problema de configuração, não da venda: continua PendenteSync, sem contar
        // tentativa (TentativasEnvio) nem gravar erro — ver ResultadoEnvioTipo.ConexaoInsegura.
        if (resultado.Tipo == ResultadoEnvioTipo.ConexaoInsegura)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Conteudo);

        // 409: guid já existe do lado do servidor — já foi recebida antes (ex:
        // confirmação anterior se perdeu antes de chegar no PDV). Trata como
        // sucesso, não reenvia nem duplica.
        if (resultado.Tipo == ResultadoEnvioTipo.Conflito)
        {
            venda.SyncStatus = SyncStatus.Sincronizado;
            PoliticaRetentativa.Zerar(venda);
            venda.UltimoErroSync = null;
            await context.SaveChangesAsync(ct);
            return ResultadoSincronizacaoRecurso.ComSucesso(1);
        }

        if (resultado.Tipo is ResultadoEnvioTipo.Falha or ResultadoEnvioTipo.TokenExpirado)
            return await MarcarFalhaAsync(context, venda, ErroApiExtractor.Extrair(resultado.Conteudo), ct);

        var respostaDto = SoftcomJson.TentarDesserializar<VendaRespostaDto>(resultado.Conteudo);
        if (respostaDto?.Data is not { } dados)
            return await MarcarFalhaAsync(context, venda, $"A resposta não trouxe o id da venda: {ErroApiExtractor.Extrair(resultado.Conteudo)}", ct);

        venda.VendaIdExterno = dados.Id;
        venda.SyncStatus = SyncStatus.Sincronizado;
        PoliticaRetentativa.Zerar(venda);
        venda.UltimoErroSync = null;
        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(1);
    }

    // Percorre todas as vendas ainda não confirmadas — PendenteSync (nunca tentada)
    // e também FalhaSync (já tentou e não deu certo antes; fica elegível pra retry
    // automático, decisão confirmada com o usuário em 2026-09-18). Uma venda com
    // erro não impede as outras de serem tentadas — mesmo princípio de
    // CatalogSyncService: não é tudo-ou-nada.
    public async Task<ResultadoSincronizacaoRecurso> SincronizarVendasPendentesAsync(string accessToken, CancellationToken ct = default)
    {
        List<Guid> vendaIds;
        await using (var context = contextFactory())
        {
            // Elegivel: em espera crescente (ou já desistiu) fica de fora — ver PoliticaRetentativa.
            vendaIds = await context.Vendas
                .Where(v => v.SyncStatus == SyncStatus.PendenteSync || v.SyncStatus == SyncStatus.FalhaSync)
                .Where(PoliticaRetentativa.Elegivel<Venda>(AgoraUtc))
                .Select(v => v.Id)
                .ToListAsync(ct);
        }

        var totalSincronizadas = 0;
        foreach (var vendaId in vendaIds)
        {
            try
            {
                var resultado = await SincronizarVendaAsync(vendaId, accessToken, ct);
                if (resultado.Sucesso)
                    totalSincronizadas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // SincronizarVendaAsync lança (não devolve ComFalha) em situações que
                // "não deveriam acontecer" (ex: caixa da venda sumiu) — mas mesmo assim
                // uma venda não pode travar o lote inteiro; o comentário acima desse
                // método promete isso, e antes da revisão de código o try/catch não
                // existia, então uma exceção numa venda abortava todas as seguintes.
                // OperationCanceledException passa direto: cancelamento é intencional,
                // não deve ser engolido. Sem infraestrutura de log ainda no projeto —
                // essa venda simplesmente permanece PendenteSync/FalhaSync (o estado
                // que já tinha) e será tentada de novo na próxima sincronização.
            }
        }

        return ResultadoSincronizacaoRecurso.ComSucesso(totalSincronizadas);
    }

    private Task<ResultadoSincronizacaoRecurso> MarcarFalhaAsync(AppDbContext context, Venda venda, string mensagem, CancellationToken ct)
    {
        var agora = AgoraUtc;
        return OutboxHelper.MarcarFalhaAsync(context, venda, mensagem, (v, m) =>
        {
            v.SyncStatus = SyncStatus.FalhaSync;
            v.UltimoErroSync = PoliticaRetentativa.RegistrarFalha(v, agora, m);   // também conta TentativasEnvio
        }, ct);
    }
}

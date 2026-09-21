using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sales.Dtos;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Services.Sales;

// A requisição que envia uma venda à API: a URL e o corpo (POST /vendas). Existe num lugar só porque DUAS telas dependem
// dela dizer a mesma coisa: o envio de verdade (VendaSyncService) e o modal "Detalhes do pedido", que mostra o que foi (ou
// vai ser) enviado. Se o modal montasse o seu próprio JSON, um dia ele mostraria uma coisa e a API receberia outra.
public sealed class RequisicaoDeVenda
{
    public required string Url { get; init; }
    public required VendaRequestDto Corpo { get; init; }
}

// Ou a requisição está pronta, ou falta alguma dependência sincronizar (funcionário, empresa, cliente, produto, forma de
// pagamento) — e aí Espera diz qual. Esperar NÃO é falha da venda (ver VendaSyncService).
public sealed class ResultadoMontagem
{
    public RequisicaoDeVenda? Requisicao { get; private init; }
    public string? Espera { get; private init; }

    public static ResultadoMontagem Pronta(RequisicaoDeVenda requisicao) => new() { Requisicao = requisicao };

    public static ResultadoMontagem Aguardando(string motivo) => new() { Espera = motivo };
}

public static class MontadorDeRequisicaoDeVenda
{
    // A venda precisa vir com Itens e Pagamentos carregados. Lança InvalidOperationException para o que "não deveria
    // acontecer" (caixa da venda ou configuração inexistentes) — quem envia trata como erro inesperado; quem só mostra,
    // como "não dá para montar".
    public static async Task<ResultadoMontagem> MontarAsync(AppDbContext context, Venda venda, CancellationToken ct = default)
    {
        var caixa = await context.Caixas.FindAsync(new object[] { venda.CaixaId }, ct)
            ?? throw new InvalidOperationException("Caixa da venda não encontrado.");

        var funcionario = await context.Funcionarios.FindAsync(new object[] { caixa.FuncionarioId }, ct);
        if (funcionario?.IdExterno is not { } operadorId)
            return ResultadoMontagem.Aguardando("Funcionário do caixa ainda não sincronizou — sincronize funcionários antes.");

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
            return ResultadoMontagem.Aguardando("Empresa deste dispositivo ainda não sincronizou — sincronize a empresa antes.");

        int clienteIdExterno;
        if (venda.ClienteId is { } clienteIdLocal)
        {
            var cliente = await context.Clientes.FindAsync(new object[] { clienteIdLocal }, ct);
            if (cliente?.IdExterno is not { } idExterno)
                return ResultadoMontagem.Aguardando("Cliente da venda ainda não sincronizou.");

            clienteIdExterno = idExterno;
        }
        else if (configuracao.ClienteConsumidorFinalIdExterno is { } consumidorFinalId)
        {
            clienteIdExterno = consumidorFinalId;   // configurado à mão: tem prioridade
        }
        else
        {
            // Sem configuração manual: o Consumidor Final é o cliente que a API marca com
            // indicador_finalidade = 1 (na API real: id 1, "CONSUMIDOR"). Exigir configuração à mão
            // deixava toda venda avulsa 🟡 pra sempre.
            var consumidorFinal = await context.Clientes
                .Where(c => c.IdExterno != null && c.IndicadorFinalidade == 1)
                .OrderBy(c => c.IdExterno)
                .Select(c => c.IdExterno)
                .FirstOrDefaultAsync(ct);
            if (consumidorFinal is not { } consumidorFinalDetectado)
                return ResultadoMontagem.Aguardando("Cliente \"Consumidor Final\" ainda não foi encontrado — aguarde a sincronização de clientes ou informe o id em Configurações.");

            clienteIdExterno = consumidorFinalDetectado;
        }

        var produtoIdsLocais = venda.Itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await context.Produtos.Where(p => produtoIdsLocais.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var formaPagamentoIdsLocais = venda.Pagamentos.Select(p => p.FormaPagamentoId).Distinct().ToList();
        var formasPagamento = await context.FormasPagamento.Where(f => formaPagamentoIdsLocais.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        var produtosDto = new List<VendaProdutoRequestDto>();
        foreach (var item in venda.Itens)
        {
            if (!produtos.TryGetValue(item.ProdutoId, out var produto) || produto.IdExterno is not { } produtoIdExterno)
                return ResultadoMontagem.Aguardando("Um dos produtos da venda ainda não sincronizou.");

            // O produto tem DOIS ids na API. Sem o produto_id guardado (produto sincronizado antes de o
            // app guardar isso), mandar o `id` no lugar registraria OUTRO produto na venda — espera a
            // próxima sincronização de produtos, que preenche (e fica PendenteSync, sem marcar falha).
            if (produto.ProdutoIdApi is not { } produtoIdApi)
                return ResultadoMontagem.Aguardando("Um dos produtos da venda ainda não tem o id-base da API — aguarde a próxima sincronização de produtos.");

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
                return ResultadoMontagem.Aguardando("Uma das formas de pagamento da venda ainda não sincronizou.");

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

        var corpo = new VendaRequestDto
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
            NumeroDocumento = NumeroDocumento.Formatar(configuracao.CodigoPdv, venda.NumeroPedido),
            Cancelada = false,
            Bloqueada = false,
            CaixaData = caixa.DataCaixa.ToString("yyyy-MM-dd"),
            CaixaTurno = caixa.Turno,
            CaixaFuncoesId = caixa.IdExterno,
            Produtos = produtosDto,
            Pagamentos = pagamentosDto,
        };

        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);
        return ResultadoMontagem.Pronta(new RequisicaoDeVenda { Url = SoftcomRotas.Vendas(dominio), Corpo = corpo });
    }
}

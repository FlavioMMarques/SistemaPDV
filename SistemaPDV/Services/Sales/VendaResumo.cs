using System;
using System.Collections.Generic;
using System.Globalization;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Sales;

public record VendaResumo(
    Guid Id,
    int? Numero,
    DateTime DataHora,
    string ClienteNome,
    string OperadorNome,
    decimal Total,
    string FormasPagamento,
    SyncStatus SyncStatus,
    string? UltimoErroSync = null,
    string ResumoItens = "",
    string ItensDetalhe = "");

// QuantidadeTexto: "4 un", "0,333 kg" (fração sem zeros à direita, vírgula decimal, unidade em minúscula).
public record ItemDetalhe(string Codigo, string Descricao, decimal Quantidade, string? Unidade, decimal PrecoUnitario, decimal Total)
{
    public string QuantidadeTexto => $"{Quantidade.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"))} {(string.IsNullOrWhiteSpace(Unidade) ? "un" : Unidade.Trim().ToLowerInvariant())}";
}

public record PagamentoDetalhe(string Forma, decimal Valor, string? Bandeira);

// O "Detalhes do pedido": o resumo da linha, mais os itens, os pagamentos e a requisição (ver VendaLocalService.ObterDetalheAsync).
// Requisicao é o texto completo (método, URL, cabeçalhos com token mascarado, corpo JSON); sem ela, MotivoSemRequisicao diz por quê.
public record DetalheVenda(
    VendaResumo Resumo,
    IReadOnlyList<ItemDetalhe> Itens,
    IReadOnlyList<PagamentoDetalhe> Pagamentos,
    decimal Desconto,
    int? VendaIdExterno,
    string? Requisicao,
    string? MotivoSemRequisicao);

// O que as vendas do caixa somam em cada forma de pagamento — o "esperado" do fechamento de caixa.
public record TotalFormaPagamento(int FormaPagamentoId, string Nome, decimal Total);

// O que as vendas do caixa somam em cada BANDEIRA de cartão — o "esperado" da apuração por bandeira no fechamento.
public record TotalBandeira(string Bandeira, decimal Total);

public class ResultadoDescarte
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }

    public static ResultadoDescarte Ok() => new() { Sucesso = true };
    public static ResultadoDescarte Falha(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

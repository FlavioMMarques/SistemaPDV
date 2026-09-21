using System;

namespace SistemaPDV.Services;

public record ResumoDashboard(
    decimal FaturamentoHoje,
    int EstoqueTotal,
    int PendentesOutbox,
    DateTimeOffset? UltimaSincronizacao,
    int VendasEmitidas = 0,
    int ProdutosCadastrados = 0);

// O que está esperando para ir à API, por tipo (o painel da fila mostra "🧾 Vendas 2 · 👥 Clientes 1"). Total = o número da pílula "Sync".
public record PendenciasPorTipo(int Caixas, int Vendas, int Clientes, int Produtos)
{
    public int Total => Caixas + Vendas + Clientes + Produtos;
}

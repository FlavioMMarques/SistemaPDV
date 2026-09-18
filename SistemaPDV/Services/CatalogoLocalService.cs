using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Services;

// Leitura pura do catálogo já sincronizado — usado pelo PdvViewModel pra listar o
// que dá pra vender. Produto/FormaPagamento só aparecem com IdExterno != null (não
// existe cadastro local pra eles); Cliente aparece sempre, mesmo sem IdExterno
// ainda — decisão do usuário (2026-09-18): venda pra cliente recém-criado localmente
// fica pendente até sincronizar, mas o cliente já é selecionável na hora.
public class CatalogoLocalService
{
    private readonly Func<AppDbContext> contextFactory;

    public CatalogoLocalService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<Produto>> ListarProdutosDisponiveisAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await context.Produtos.Where(p => p.IdExterno != null && p.Vender).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Cliente>> ListarClientesAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await context.Clientes.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FormaPagamento>> ListarFormasPagamentoDisponiveisAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        return await context.FormasPagamento.Where(f => f.IdExterno != null).ToListAsync(ct);
    }
}

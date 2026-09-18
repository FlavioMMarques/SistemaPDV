using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;

namespace SistemaPDV.Data;

public class AppDbContext : DbContext
{
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<FormaPagamento> FormasPagamento => Set<FormaPagamento>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<Caixa> Caixas => Set<Caixa>();
    public DbSet<ItemVenda> ItensVenda => Set<ItemVenda>();
    public DbSet<PagamentoVenda> PagamentosVenda => Set<PagamentoVenda>();
    public DbSet<Venda> Vendas => Set<Venda>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

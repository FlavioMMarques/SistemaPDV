using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Nome).IsRequired().HasMaxLength(150);
        builder.Property(c => c.CpfCnpj).HasMaxLength(14);
        builder.Property(c => c.UltimoErroSync).HasMaxLength(500);
        builder.Property(c => c.Pessoa).HasConversion<string>();
        builder.Property(c => c.SyncStatus).HasConversion<string>();
        builder.HasIndex(c => c.IdExterno).IsUnique();

        // Owned type: TabelaPreco não ganha tabela própria — suas colunas (prefixadas
        // "TabelaPreco_") vivem dentro da própria tabela Clientes.
        builder.OwnsOne(c => c.TabelaPreco);
    }
}

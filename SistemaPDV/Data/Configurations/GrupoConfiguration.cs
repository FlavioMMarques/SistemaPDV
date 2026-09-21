using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class GrupoConfiguration : IEntityTypeConfiguration<Grupo>
{
    public void Configure(EntityTypeBuilder<Grupo> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Nome).HasMaxLength(100);
        builder.Property(g => g.SyncStatus).HasConversion<string>();
        builder.HasIndex(g => g.IdExterno).IsUnique();
    }
}

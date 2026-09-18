using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class EmpresaConfiguration : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RazaoSocial).IsRequired().HasMaxLength(150);
        builder.Property(e => e.Cnpj).IsRequired().HasMaxLength(14);
        builder.Property(e => e.SyncStatus).HasConversion<string>();
        builder.HasIndex(e => e.IdExterno).IsUnique();
    }
}

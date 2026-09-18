using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class FuncionarioConfiguration : IEntityTypeConfiguration<Funcionario>
{
    public void Configure(EntityTypeBuilder<Funcionario> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Nome).IsRequired().HasMaxLength(150);
        builder.Property(f => f.Cpf).HasMaxLength(11);
        builder.Property(f => f.PdvKeyHash).HasMaxLength(64); // SHA-256 em hex = 64 caracteres
        builder.Property(f => f.SyncStatus).HasConversion<string>();
        builder.HasIndex(f => f.IdExterno).IsUnique();
    }
}

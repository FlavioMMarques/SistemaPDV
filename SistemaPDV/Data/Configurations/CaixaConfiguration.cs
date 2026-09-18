using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class CaixaConfiguration : IEntityTypeConfiguration<Caixa>
{
    public void Configure(EntityTypeBuilder<Caixa> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.SyncStatus).HasConversion<string>();
        builder.Property(c => c.Status).HasConversion<string>();
        builder.HasIndex(c => c.IdExterno).IsUnique();

        // Mesma chave natural que a API usa pra recusar abertura duplicada
        // (409 "já existe caixa aberto para esta data/operador/turno").
        builder.HasIndex(c => new { c.DataCaixa, c.Turno, c.FuncionarioId }).IsUnique();

        builder.HasOne<Funcionario>()
            .WithMany()
            .HasForeignKey(c => c.FuncionarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

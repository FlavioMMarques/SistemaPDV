using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class FormaPagamentoConfiguration : IEntityTypeConfiguration<FormaPagamento>
{
    public void Configure(EntityTypeBuilder<FormaPagamento> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Nome).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Tipo).IsRequired().HasMaxLength(50);
        builder.Property(f => f.CodigoNfce).HasMaxLength(10);
        builder.Property(f => f.CodigoTransacaoSitef).HasMaxLength(20);
        builder.Property(f => f.AtalhoNumero).HasMaxLength(5);
        builder.Property(f => f.SyncStatus).HasConversion<string>();
        builder.HasIndex(f => f.IdExterno).IsUnique();
    }
}

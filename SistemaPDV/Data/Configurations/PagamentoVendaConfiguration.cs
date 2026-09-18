using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class PagamentoVendaConfiguration : IEntityTypeConfiguration<PagamentoVenda>
{
    public void Configure(EntityTypeBuilder<PagamentoVenda> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Valor).HasPrecision(18, 2);

        builder.HasOne<FormaPagamento>()
            .WithMany()
            .HasForeignKey(p => p.FormaPagamentoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

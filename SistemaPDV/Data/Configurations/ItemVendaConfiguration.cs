using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class ItemVendaConfiguration : IEntityTypeConfiguration<ItemVenda>
{
    public void Configure(EntityTypeBuilder<ItemVenda> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Quantidade).HasPrecision(18, 3);
        builder.Property(i => i.PrecoUnitario).HasPrecision(18, 2);
        builder.Property(i => i.DescontoItem).HasPrecision(18, 2);
        builder.Property(i => i.AcrescimoItem).HasPrecision(18, 2);

        // Restrict (não cascade): apagar um Produto não pode apagar item de venda
        // histórico — a venda já aconteceu, o registro fica mesmo que o produto saia
        // do catálogo depois.
        builder.HasOne<Produto>()
            .WithMany()
            .HasForeignKey(i => i.ProdutoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

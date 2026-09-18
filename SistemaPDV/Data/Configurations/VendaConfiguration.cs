using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class VendaConfiguration : IEntityTypeConfiguration<Venda>
{
    public void Configure(EntityTypeBuilder<Venda> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Desconto).HasPrecision(18, 2);
        builder.Property(v => v.SyncStatus).HasConversion<string>();

        builder.HasOne<Caixa>()
            .WithMany()
            .HasForeignKey(v => v.CaixaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(v => v.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade aqui (diferente das FKs de Produto/FormaPagamento nos itens/
        // pagamentos): Itens e Pagamentos só existem em função da Venda — apagar a
        // venda apaga os dois junto, não deixa órfão.
        builder.HasMany(v => v.Itens)
            .WithOne()
            .HasForeignKey(i => i.VendaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.Pagamentos)
            .WithOne()
            .HasForeignKey(p => p.VendaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

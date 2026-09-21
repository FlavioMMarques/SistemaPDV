using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class CartaoConfiguration : IEntityTypeConfiguration<Cartao>
{
    public void Configure(EntityTypeBuilder<Cartao> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Credenciadora).HasMaxLength(100);
        builder.Property(c => c.Nome).HasMaxLength(100);
        builder.Property(c => c.BandeiraId).HasMaxLength(10);
        // Mesmo limite de DigitacaoBandeiraCaixa.Bandeira e PagamentoVenda.Bandeira: é o mesmo texto que viaja do cartão
        // para o pagamento e para a apuração do fechamento.
        builder.Property(c => c.BandeiraNome).HasMaxLength(30);
        builder.Property(c => c.Tipo).HasMaxLength(30);
        builder.Property(c => c.AliasCartao).HasMaxLength(100);
        builder.Property(c => c.TaxaAdministrativa).HasPrecision(9, 4);
        builder.Property(c => c.SyncStatus).HasConversion<string>();
        builder.HasIndex(c => c.IdExterno).IsUnique();
    }
}

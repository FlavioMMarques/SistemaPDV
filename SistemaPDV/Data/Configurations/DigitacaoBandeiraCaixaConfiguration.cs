using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class DigitacaoBandeiraCaixaConfiguration : IEntityTypeConfiguration<DigitacaoBandeiraCaixa>
{
    public void Configure(EntityTypeBuilder<DigitacaoBandeiraCaixa> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Bandeira).IsRequired().HasMaxLength(30);
        builder.Property(d => d.Valor).HasPrecision(18, 2);
    }
}

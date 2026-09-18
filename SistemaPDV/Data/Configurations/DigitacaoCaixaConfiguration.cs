using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class DigitacaoCaixaConfiguration : IEntityTypeConfiguration<DigitacaoCaixa>
{
    public void Configure(EntityTypeBuilder<DigitacaoCaixa> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Valor).HasPrecision(18, 2);

        // Restrict (não cascade): apagar uma FormaPagamento não pode apagar a
        // digitação histórica do fechamento — mesmo raciocínio de ItemVenda/Produto.
        builder.HasOne<FormaPagamento>()
            .WithMany()
            .HasForeignKey(d => d.FormaPagamentoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

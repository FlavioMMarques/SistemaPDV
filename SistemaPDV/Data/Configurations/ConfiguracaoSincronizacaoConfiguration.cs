using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class ConfiguracaoSincronizacaoConfiguration : IEntityTypeConfiguration<ConfiguracaoSincronizacao>
{
    public void Configure(EntityTypeBuilder<ConfiguracaoSincronizacao> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.UrlApi).HasMaxLength(500);
        builder.Property(c => c.ApiClienteId).HasMaxLength(100);
        builder.Property(c => c.NomeDispositivo).HasMaxLength(100);

        // HasDefaultValue explícito (não basta o "= true" no C#): sem isso, o EF
        // gera a coluna com default false no banco, e qualquer linha existente
        // preenchida por fora do app (ou numa migration futura) ficaria com o
        // valor errado — o inverso do comportamento mais seguro que decidimos.
        builder.Property(c => c.ExigirAberturaCaixa).HasDefaultValue(true);
    }
}

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
    }
}

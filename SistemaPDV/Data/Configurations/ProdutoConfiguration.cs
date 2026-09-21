using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SistemaPDV.Models;

namespace SistemaPDV.Data.Configurations;

public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Ignore(p => p.GrupoNome);   // preenchido na leitura (CatalogoLocalService), não é coluna
        builder.Property(p => p.Nome).IsRequired().HasMaxLength(150);
        builder.Property(p => p.Sku).HasMaxLength(50);
        builder.Property(p => p.CodigoBarras).HasMaxLength(50);
        builder.Property(p => p.SyncStatus).HasConversion<string>();
        builder.HasIndex(p => p.IdExterno).IsUnique();

        // Diferente de OwnsOne (TabelaPreco em Cliente), uma coleção owned precisa de
        // uma chave própria pra diferenciar os itens da lista — sem tipo natural pra
        // isso, cria-se uma chave "sombra" (shadow property, sem propriedade C#
        // correspondente na classe) e ela precisa ser explicitamente auto-incremento.
        builder.OwnsMany(p => p.Imagens, imagem =>
        {
            imagem.Property<int>("Id").ValueGeneratedOnAdd();
            imagem.HasKey("Id");
        });
    }
}

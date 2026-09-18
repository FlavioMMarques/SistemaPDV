# Spec: data-layer

## Objective

Camada de persistência local do SistemaPDV: um `AppDbContext` (EF Core 8 + provider Sqlite) com as entidades que sustentam operação offline-first, migrations, e um padrão comum de rastreamento de sincronização (`SyncStatus`) reutilizado por toda entidade que precisa subir/descer da API SoftcomShop.

Sucesso = `dotnet ef database update` cria um SQLite funcional do zero; todo módulo seguinte (`catalog-sync`, `caixa`, `sales`) consegue ler/escrever através deste DbContext sem precisar tocar em SQL cru.

## Tech Stack

- .NET 8, C# 12, Nullable habilitado
- `Microsoft.EntityFrameworkCore.Sqlite` 8.x
- `Microsoft.EntityFrameworkCore.Design` (para migrations via CLI)

## Commands

```
dotnet build
dotnet ef migrations add <Nome> --project SistemaPDV
dotnet ef database update --project SistemaPDV
dotnet test
```

## Project Structure

```
SistemaPDV/
  Data/
    AppDbContext.cs
    AppDbContextFactory.cs        # IDesignTimeDbContextFactory, pro `dotnet ef` funcionar sem rodar o app
    Configurations/               # IEntityTypeConfiguration<T> por entidade (Fluent API)
    Migrations/
  Models/
    Produto.cs
    Cliente.cs
    FormaPagamento.cs
    Empresa.cs
    Funcionario.cs
    Caixa.cs
    Venda.cs
    ItemVenda.cs
    PagamentoVenda.cs
    SyncStatus.cs                 # enum compartilhado
    ISincronizavel.cs             # interface genérica compartilhada (Id, IdExterno, SyncStatus)
```

## Code Style

Entidades como classes simples (POCOs), configuração via `IEntityTypeConfiguration<T>` (Fluent API em classe própria, não Data Annotations) — mesmo padrão do projeto de referência `SistemaAvalonia`:

```csharp
public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nome).IsRequired().HasMaxLength(150);
        builder.Property(p => p.SyncStatus).HasConversion<string>();
        builder.HasIndex(p => p.IdExterno).IsUnique();
    }
}
```

### `ISincronizavel<TKey>` — interface genérica

A maioria das entidades sincronizáveis usa `int` como chave local (autoincremento do SQLite), mas `Venda` usa `Guid` (é o mesmo valor enviado como idempotência pra API — ver `SPEC-sales.md`). Em vez de ter duas interfaces parecidas (uma pra `int`, outra pra `Guid`) ou abrir mão da interface pra `Venda`, `ISincronizavel` é genérica no tipo da chave:

```csharp
public interface ISincronizavel<TKey>
{
    TKey Id { get; }
    int? IdExterno { get; set; }
    SyncStatus SyncStatus { get; set; }
}
```

```csharp
public class Produto : ISincronizavel<int> { public int Id { get; set; } /* ... */ }
public class Venda   : ISincronizavel<Guid> { public Guid Id { get; set; } /* ... */ }
```

**Por que genérico em vez de duas interfaces separadas (ou nenhuma pra Venda):** o *contrato* de "essa entidade sincroniza com a API" é o mesmo não importa o tipo da chave — sempre precisa de `IdExterno` e `SyncStatus`. Sem generics, ou duplicaríamos a interface (`ISincronizavelInt`/`ISincronizavelGuid` — repetição sem necessidade) ou deixaríamos `Venda` de fora do contrato comum, quebrando o polimorfismo (código que hoje itera sobre `IEnumerable<ISincronizavel<int>>` pra, por exemplo, contar quantas entidades estão `PendenteSync` no dashboard, não conseguiria incluir `Venda` sem um caso especial). Com `ISincronizavel<TKey>`, o C# infere o tipo concreto em cada classe e o compilador garante type-safety — `Produto.Id` continua `int`, `Venda.Id` continua `Guid`, mas ambas passam no mesmo `where T : ISincronizavel<TKey>` de um método genérico compartilhado (por exemplo, um upsert genérico em `catalog-sync`). É o mesmo princípio por trás de `IEnumerable<T>`/`IRepository<T>` no .NET: uma interface, várias formas concretas.

`SyncStatus` é um enum (não string solta como no mockup do curso), persistido como string via `HasConversion<string>()` — legível direto no SQLite, sem perder type-safety no C#:

```csharp
public enum SyncStatus
{
    PendenteSync,
    Sincronizado,
    FalhaSync,
}
```

## Testing Strategy

- Projeto novo `SistemaPDV.Tests` (xUnit), referenciando `SistemaPDV`.
- EF Core com provider Sqlite in-memory (`DataSource=:memory:`, conexão mantida aberta durante o teste) para testar configurações de entidade e queries sem tocar disco.
- Cobertura mínima: cada `IEntityTypeConfiguration` tem um teste que insere e lê de volta uma entidade válida; constraints (chave única de `IdExterno`, campos obrigatórios) têm teste de violação.

## Boundaries

- **Sempre:** toda entidade sincronizável implementa `ISincronizavel<TKey>` (`Id` no tipo próprio da entidade, `IdExterno` nullable int, `SyncStatus`); toda migration é gerada via `dotnet ef`, nunca escrita à mão.
- **Perguntar antes:** mudar o nome/tipo de uma coluna já migrada em dev (pode exigir migration destrutiva); adicionar um pacote NuGet novo.
- **Nunca:** guardar `EmpresaCertificado`/`EmpresaCertificadoSenha` em texto puro — ver Open Questions.

## Success Criteria

- [x] `AppDbContext` compila e `dotnet ef database update` cria `pdv.db` com todas as tabelas
- [x] Todas as entidades sincronizáveis implementam `ISincronizavel<TKey>` (`int` pra maioria, `Guid` pra `Venda`)
- [x] `SyncStatus` persiste como string legível no SQLite
- [x] Testes de configuração de entidade passam (`dotnet test`) — 17 testes, 0 falhas

## Open Questions

```
ASSUMPTIONS I'M MAKING:
1. Entidades locais (Produto, Cliente, FormaPagamento, Funcionario, Empresa) guardam
   Id local (int, PK autoincremento) + IdExterno (int? = id vindo da API) — igual ao
   padrão Uid/IdExterno do projeto de referência, mas usando IdExterno como chave de
   casamento (não Uid, que lá era só pro fluxo SQL Server).
2. Venda usa Guid como chave primária local (não int autoincremento) — é o mesmo valor
   enviado como `guid` pro POST /api/v2/vendas, então idempotência e PK local são a
   mesma coisa. RESOLVIDO (2026-09-18): `ISincronizavel<TKey>` é genérica no tipo da
   chave, então Venda continua implementando a mesma interface que as demais
   entidades (`ISincronizavel<Guid>`), sem precisar de um caso especial.
3. Caixa vira uma tabela local própria (não só um campo solto), guardando o
   `caixa_funcoes_id` retornado pela API na abertura — ver SPEC-caixa.
4. EmpresaCertificado/EmpresaCertificadoSenha: por enquanto o campo existe no modelo
   Empresa mas a spec de catalog-sync não persiste o valor cru — fica como
   `string? CertificadoProtegido` (mesmo esquema DPAPI do client_secret no projeto
   de referência) e só é preenchido se/quando o módulo de emissão fiscal (fora de
   escopo) for implementado. Confirmar se isso é aceitável ou se o campo nem deveria
   existir ainda.
→ Corrija agora ou sigo com essas assunções.
```

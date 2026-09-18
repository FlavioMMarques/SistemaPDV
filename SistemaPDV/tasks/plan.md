# Implementation Plan: SistemaPDV

Plano único do projeto, com uma fase por módulo do capability map (`specs/CAPABILITY-MAP.md`), na ordem de build: `data-layer` → `catalog-sync` → `caixa` → `sales` → `pdv-ui`. Cada fase é detalhada quando o módulo anterior está pronto — as fases abaixo além da 1 são placeholders até serem planejadas.

## Overview

PDV offline-first (.NET 8 + Avalonia + EF Core 8/Sqlite) sincronizando com a API SoftcomShop. Fase 1 (`data-layer`) constrói a base de persistência local que todo o resto depende.

## Architecture Decisions

- EF Core 8 + Sqlite, configuração via `IEntityTypeConfiguration<T>` (Fluent API em classe própria, não Data Annotations) — ver `specs/SPEC-data-layer.md`.
- `ISincronizavel<TKey>` (genérica no tipo da chave — `int` pra maioria, `Guid` pra Venda) + enum `SyncStatus` (`PendenteSync`/`Sincronizado`/`FalhaSync`, persistido como string) é o padrão comum de toda entidade que sincroniza com a API. Ser genérica evita duplicar a interface e mantém `Venda` no mesmo contrato polimórfico das demais entidades, mesmo usando um tipo de chave diferente — ver a explicação completa em `specs/SPEC-data-layer.md`.
- `Venda` usa `Guid` como chave primária local (`ISincronizavel<Guid>`) — é o mesmo valor enviado como `guid` (idempotência) pro `POST /api/v2/vendas`.
- `Caixa` é uma entidade local própria (não um campo solto), referenciada pela venda via chave natural (`DataCaixa`+`Turno`+`FuncionarioId`), não pelo id remoto — isso é o que permite caixa e venda serem offline-first (ver `specs/SPEC-caixa.md`, `specs/SPEC-sales.md`).
- `Funcionario.PdvKeyHash` guarda hash (SHA-256) do `pdv_key` sincronizado, nunca o valor em texto puro.
- Testes: projeto `SistemaPDV.Tests` (xUnit) novo, usando Sqlite in-memory (`DataSource=:memory:`, conexão mantida aberta) — sem tocar disco, sem mocks de EF Core.

## Task List

### Fase 1: data-layer (detalhada — ver `tasks/todo.md`) — ✅ completa (2026-09-18)

**Foundation**
- [x] Task 1: SyncStatus enum + ISincronizavel<TKey> interface
- [x] Task 2: Projeto SistemaPDV.Tests + harness Sqlite in-memory

### Checkpoint: Foundation
- [x] `dotnet build` sem erros
- [x] `dotnet test` roda (harness smoke test passa)

**Entidades de catálogo**
- [x] Task 3: AppDbContext + AppDbContextFactory + Produto (entidade/config/testes)
- [x] Task 4: Cliente (entidade/config/testes)
- [x] Task 5: FormaPagamento (entidade/config/testes)

### Checkpoint: Catálogo
- [x] Produto/Cliente/FormaPagamento fazem round-trip no Sqlite in-memory
- [x] `dotnet test` verde

**Empresa, Funcionário, Caixa**
- [x] Task 6: Empresa (entidade/config/testes)
- [x] Task 7: Funcionario (entidade/config/testes, incl. PdvKeyHash)
- [x] Task 8: Caixa (entidade/config/testes)

### Checkpoint: Referência completa
- [x] Todas as entidades de referência persistem e leem de volta corretamente
- [x] `dotnet test` verde

**Agregado de venda**
- [x] Task 9: ItemVenda + PagamentoVenda (entidades/configs/testes)
- [x] Task 10: Venda (entidade/config/testes) — agregado raiz com Guid PK, FKs pra Caixa/Cliente

### Checkpoint: Agregado de venda
- [x] Venda com Itens e Pagamentos faz round-trip completo no Sqlite in-memory
- [x] `dotnet test` verde

**Migration**
- [x] Task 11: Migration InitialCreate + `dotnet ef database update` cria `pdv.db` funcional

### Checkpoint: data-layer completo
- [x] Todos os Success Criteria de `specs/SPEC-data-layer.md` atendidos
- [ ] Revisão com o usuário antes de planejar `catalog-sync`

### Fase 2: catalog-sync — a planejar após Fase 1
### Fase 3: caixa — a planejar após Fase 2
### Fase 4: sales — a planejar após Fase 3
### Fase 5: pdv-ui — a planejar após Fase 4

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Modelar demais/de menos nas entidades antes de `catalog-sync` existir, exigindo migration retrabalhada | Médio | Entidades seguem exatamente os campos já confirmados no contrato da API (ver `softcomshop-api-contract` na memória do agente); campos claramente fora de escopo (NFC-e, mesa/comanda) ficam de fora do modelo por ora, não só nulos |
| `Guid` como PK do SQLite tem overhead de índice maior que `int` autoincremento | Baixo | Volume de vendas de um PDV local é pequeno; a simplicidade de usar o mesmo Guid como idempotência da API compensa |
| Sqlite in-memory por teste pode mascarar diferenças de comportamento do arquivo real (ex: `PRAGMA foreign_keys`) | Baixo | Task 11 (migration real + `database update`) serve de verificação final contra o SQLite de arquivo de verdade, não só o in-memory |

## Open Questions

- Nenhuma bloqueante para a Fase 1 — as pendências reais (id de "Consumidor Final", required fields exatos de `/vendas`) só afetam `sales`, fases futuras.

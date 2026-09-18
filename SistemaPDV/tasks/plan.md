# Implementation Plan: SistemaPDV

Plano único do projeto, com uma fase por módulo do capability map (`specs/CAPABILITY-MAP.md`), na ordem de build: `data-layer` → `catalog-sync` → `caixa` → `sales` → `pdv-ui`. Cada fase é detalhada quando o módulo anterior está pronto — as fases abaixo da 2 são placeholders até serem planejadas.

## Overview

PDV offline-first (.NET 8 + Avalonia + EF Core 8/Sqlite) sincronizando com a API SoftcomShop. Fase 1 (`data-layer`, completa) construiu a base de persistência local. Fase 2 (`catalog-sync`) conecta essa base à API real: autenticação, busca paginada, e upsert dos cinco recursos de referência (produtos, clientes, formas de pagamento, empresa, funcionários).

## Architecture Decisions

- EF Core 8 + Sqlite, configuração via `IEntityTypeConfiguration<T>` (Fluent API em classe própria, não Data Annotations) — ver `specs/SPEC-data-layer.md`.
- `ISincronizavel<TKey>` (genérica no tipo da chave — `int` pra maioria, `Guid` pra Venda) + enum `SyncStatus` (`PendenteSync`/`Sincronizado`/`FalhaSync`, persistido como string) é o padrão comum de toda entidade que sincroniza com a API. Ser genérica evita duplicar a interface e mantém `Venda` no mesmo contrato polimórfico das demais entidades, mesmo usando um tipo de chave diferente — ver a explicação completa em `specs/SPEC-data-layer.md`.
- `Venda` usa `Guid` como chave primária local (`ISincronizavel<Guid>`) — é o mesmo valor enviado como `guid` (idempotência) pro `POST /api/v2/vendas`.
- `Caixa` é uma entidade local própria (não um campo solto), referenciada pela venda via chave natural (`DataCaixa`+`Turno`+`FuncionarioId`), não pelo id remoto — isso é o que permite caixa e venda serem offline-first (ver `specs/SPEC-caixa.md`, `specs/SPEC-sales.md`).
- `Funcionario.PdvKeyHash` guarda hash (SHA-256) do `pdv_key` sincronizado, nunca o valor em texto puro.
- Testes: projeto `SistemaPDV.Tests` (xUnit) novo, usando Sqlite in-memory (`DataSource=:memory:`, conexão mantida aberta) — sem tocar disco, sem mocks de EF Core.
- `ConfiguracaoSincronizacao` (Task 12) é uma extensão pontual do `data-layer` feita já na Fase 2 — não fazia sentido antecipar essa entidade antes de existir um consumidor real (`catalog-sync`) pra ela.
- `SegredoProtector` (Task 13, DPAPI) protege `client_secret` (e, mais adiante, o certificado da empresa) em repouso — mesmo padrão que o projeto de referência usa. Amarra essa funcionalidade especificamente ao Windows (ver Risks).
- `SoftcomApiClient` (Task 14) é o único ponto que fala HTTP com a API — todo DTO/serviço de sincronização depende dele, nunca chama `HttpClient` direto.
- `Caixa` ganha `UltimoErroSync` e duas entidades filhas (`DigitacaoCaixa`/`DigitacaoBandeiraCaixa`) na Fase 3 — não fazia sentido antecipar isso no `data-layer` original: só ficou claro que o fechamento precisa guardar a digitação localmente depois da decisão de tornar caixa 100% offline-first (tomada durante a revisão da Fase 1).
- `PdvKeyHasher` é extraído do `CatalogSyncService` assim que aparece um segundo consumidor (`LoginOperadorService`) — duplicar lógica de hash de segurança é mais arriscado que duplicar lógica comum (se um dia o algoritmo mudar num lugar só, login e sync ficam dessincronizados silenciosamente).
- `ErroApiExtractor` (Fase 4) extrai o parsing de `{"errors": {...}}` que já existia duplicado em `CaixaSyncService` — dessa vez é pura extração de string (sem risco de tradução de LINQ como no caso do upsert genérico do `catalog-sync`), então três usos justificam compartilhar.
- `ClienteConsumidorFinalIdExterno` fica configurável em `ConfiguracaoSincronizacao`, não hardcoded — a evidência de que é `id=1` é forte mas não 100% confirmada (ver Open Questions de `SPEC-sales.md`); se estiver errada, é um valor de configuração pra corrigir, não uma busca pelo código.

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

### Fase 2: catalog-sync (detalhada — ver `tasks/todo.md`)

**Foundation**
- [x] Task 12: ConfiguracaoSincronizacao (entidade — extensão pontual do data-layer)
- [x] Task 13: SegredoProtector (DPAPI)
- [x] Task 14: PaginaApiDto + SoftcomApiClient (cliente HTTP paginado genérico)

### Checkpoint: Foundation (catalog-sync)
- [x] `dotnet build` sem erros
- [x] `dotnet test` verde

**Autenticação**
- [x] Task 15: SoftcomAuthService

### Checkpoint: Autenticação pronta
- [x] `dotnet test` verde

**Sincronização por recurso (vertical, um de cada vez)**
- [x] Task 16: FormaPagamento
- [x] Task 17: Cliente (introduz mapeamento de owned type)
- [x] Task 18: Produto (introduz mapeamento de owned collection)
- [x] Task 19: Funcionario (introduz hash do pdv_key)

### Checkpoint: Recursos de catálogo sincronizando individualmente
- [x] `dotnet test` verde — 47 testes

**Empresa e orquestração final**
- [x] Task 20: Empresa (dado sensível — certificado protegido)
- [x] Task 21: CatalogSyncService — orquestração completa (autentica + 5 recursos + resumo)

### Checkpoint: catalog-sync completo — ✅ completa (2026-09-18)
- [x] Todos os Success Criteria de `specs/SPEC-catalog-sync.md` atendidos
- [x] Revisão com o usuário antes de planejar `caixa`

### Fase 3: caixa (detalhada — ver `tasks/todo.md`)

**Foundation (extensões pontuais do data-layer)**
- [x] Task 22: `Caixa.UltimoErroSync` (campo novo, mesmo padrão de `Venda`)
- [x] Task 23: `DigitacaoCaixa` + `DigitacaoBandeiraCaixa` (entidades novas — conferência de fechamento)
- [x] Task 24: `PdvKeyHasher` (extrai a lógica de hash de `CatalogSyncService`, reaproveitada pelo login)
- [x] Task 25: `LoginOperadorService`

### Checkpoint: Foundation + Login (caixa)
- [x] `dotnet build` sem erros
- [x] `dotnet test` verde — 62 testes

**Abertura offline-first**
- [x] Task 26: `CaixaService.AbrirCaixaLocalAsync` (grava local, sem rede)
- [x] Task 27: `CaixaSyncService` — sincroniza abertura pendente (outbox)

### Checkpoint: Abertura offline-first completa
- [x] `dotnet test` verde — 70 testes

**Fechamento offline-first**
- [x] Task 28: `CaixaService.FecharCaixaLocalAsync` (grava digitação local, sem rede)
- [x] Task 29: `CaixaSyncService` — sincroniza fechamento pendente (outbox)

### Checkpoint: Fechamento offline-first completo
- [x] `dotnet test` verde — 77 testes

**Orquestração**
- [x] Task 30: `SincronizarCaixaPendenteAsync` — decide abrir vs. fechar automaticamente conforme o estado local

### Checkpoint: caixa completo — ✅ completa (2026-09-18)
- [x] Todos os Success Criteria de `specs/SPEC-caixa.md` atendidos
- [x] Revisão com o usuário antes de planejar `sales`
### Fase 4: sales (detalhada — ver `tasks/todo.md`)

**Foundation**
- [x] Task 31: `ErroApiExtractor` (extrai a lógica de parsing de erro de `CaixaSyncService` — terceiro uso, DRY)
- [x] Task 32: `ConfiguracaoSincronizacao.ClienteConsumidorFinalIdExterno` (configurável, não número mágico)
- [x] Task 33: `VendaApiDto` (request essencial + response)

### Checkpoint: Foundation (sales)
- [x] `dotnet build` sem erros
- [x] `dotnet test` verde — 85 testes

**Registro local e outbox**
- [x] Task 34: `VendaService.RegistrarVendaLocalAsync` (local, offline-safe)
- [x] Task 35: `VendaSyncService.SincronizarVendaAsync` (uma venda: 200/409/422)
- [x] Task 36: `VendaSyncService.SincronizarVendasPendentesAsync` (lote) + teste de integração fim-a-fim

### Checkpoint: sales completo — ✅ completa (2026-09-18)
- [x] Todos os Success Criteria de `specs/SPEC-sales.md` atendidos
- [ ] Revisão com o usuário antes de planejar a Fase 5 (`pdv-ui`)
### Fase 5: pdv-ui — a planejar após Fase 4

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Modelar demais/de menos nas entidades antes de `catalog-sync` existir, exigindo migration retrabalhada | Médio | Entidades seguem exatamente os campos já confirmados no contrato da API (ver `softcomshop-api-contract` na memória do agente); campos claramente fora de escopo (NFC-e, mesa/comanda) ficam de fora do modelo por ora, não só nulos |
| `Guid` como PK do SQLite tem overhead de índice maior que `int` autoincremento | Baixo | Volume de vendas de um PDV local é pequeno; a simplicidade de usar o mesmo Guid como idempotência da API compensa |
| Sqlite in-memory por teste pode mascarar diferenças de comportamento do arquivo real (ex: `PRAGMA foreign_keys`) | Baixo | Task 11 (migration real + `database update`) serve de verificação final contra o SQLite de arquivo de verdade, não só o in-memory |
| `SegredoProtector` (Task 13) usa DPAPI, específico do Windows | Baixo | Aceitável pro escopo do curso (app roda em Windows); documentado como limitação conhecida caso surja necessidade multiplataforma depois |
| `next_page_url` da API aponta pra domínios diferentes conforme o cliente (visto nos exemplos: `localhost:73`, `qualidade.softcomshop.com.br`) — validação de domínio rígida demais pode rejeitar paginação legítima | Médio | `SoftcomApiClient` (Task 14) valida contra o domínio da própria `UrlApi` configurada (não um domínio fixo hardcoded), então funciona em qualquer ambiente (dev/homologação/produção) igual |
| Nenhum teste real contra a API SoftcomShop de verdade (tudo com `HttpMessageHandler` fake) — DTOs podem estar sutilmente errados em relação à API real | Médio | Formato dos DTOs segue exatamente os schemas confirmados via Swagger real (não suposição); ajuste fica pra quando houver acesso a um ambiente de teste real da API |

## Open Questions

- Nenhuma bloqueante para a Fase 2 — a pendência real (id fixo de "Consumidor Final" pra venda avulsa) só afeta `sales`, fase futura; já há forte evidência de que é `id: 1` (ver `softcomshop-api-contract` na memória).

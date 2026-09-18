# Task List: SistemaPDV

Fase 1 (`data-layer`) detalhada abaixo. Ver `tasks/plan.md` para o plano geral e as fases seguintes (ainda não planejadas em detalhe).

## Fase 1: data-layer

## Task 1: SyncStatus enum + ISincronizavel<TKey> interface

**Description:** Cria os dois tipos compartilhados que toda entidade sincronizável vai usar: o enum `SyncStatus` e a interface genérica `ISincronizavel<TKey>`. É genérica no tipo da chave (`TKey`) porque a maioria das entidades usa `int` (PK autoincremento do SQLite), mas `Venda` usa `Guid` (Task 10) — genérico deixa as duas famílias de entidade no mesmo contrato polimórfico, sem duplicar a interface nem deixar `Venda` de fora (ver explicação completa em `specs/SPEC-data-layer.md`). Não depende de nenhuma entidade concreta — é a base sobre a qual as próximas tasks constroem.

**Acceptance criteria:**
- [x] `SyncStatus` tem os 3 valores: `PendenteSync`, `Sincronizado`, `FalhaSync`
- [x] `ISincronizavel<TKey>` expõe `TKey Id`, `int? IdExterno`, `SyncStatus SyncStatus`

**Verification:**
- [x] Build: `dotnet build`
- [x] Manual check: os dois arquivos compilam sem referenciar nada além de `System`

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Models/SyncStatus.cs`
- `SistemaPDV/Models/ISincronizavel.cs`

**Estimated scope:** XS (2 files)

---

## Task 2: Projeto SistemaPDV.Tests + harness Sqlite in-memory

**Description:** Cria o projeto de testes xUnit e um helper reutilizável que abre uma conexão Sqlite in-memory (`DataSource=:memory:`, mantida aberta durante o teste). Fica só no nível de `SqliteConnection` por enquanto — não referencia `AppDbContext` (que só existe a partir da Task 3), pra não criar uma dependência circular entre as tasks. Task 3 estende esse helper com um método que devolve um `AppDbContext` configurado sobre essa conexão.

**Acceptance criteria:**
- [x] `SistemaPDV.Tests.csproj` referencia `SistemaPDV` e os pacotes xUnit + `Microsoft.EntityFrameworkCore.Sqlite` padrão
- [x] Helper `SqliteInMemoryFixture` (ou nome equivalente) abre e mantém viva uma `SqliteConnection` in-memory, com `Dispose` fechando a conexão
- [x] Um smoke test comprova que a conexão abre, aceita um `SELECT 1`, e fecha sem erro

**Verification:**
- [x] Tests pass: `dotnet test`
- [x] Build: `dotnet build`

**Dependencies:** None (pode rodar em paralelo com Task 1)

**Files likely touched:**
- `SistemaPDV.Tests/SistemaPDV.Tests.csproj`
- `SistemaPDV.Tests/SqliteInMemoryFixture.cs`
- `SistemaPDV.Tests/SmokeTests.cs`

**Estimated scope:** S (3 files)

---

### Checkpoint: Foundation
- [x] `dotnet build` sem erros
- [x] `dotnet test` roda e passa (mesmo que só o smoke test)

---

## Task 3: AppDbContext + AppDbContextFactory + Produto

**Description:** Cria o `AppDbContext` com o primeiro `DbSet` (Produto), sua configuração Fluent API, e a `IDesignTimeDbContextFactory` que o `dotnet ef` usa pra rodar migrations sem precisar iniciar o app. A partir daqui o harness da Task 2 passa a testar contra um contexto real.

**Acceptance criteria:**
- [x] `Produto` implementa `ISincronizavel<int>`; campos: `Sku`, `CodigoBarras`, `Nome`, `PrecoVenda`, `EstoqueAtual`
- [x] `ProdutoConfiguration` define chave, campos obrigatórios/tamanho máximo, índice único em `IdExterno`, e `SyncStatus` convertido pra string
- [x] `AppDbContext` expõe `DbSet<Produto> Produtos` e aplica as configurations via `ApplyConfigurationsFromAssembly`
- [x] `AppDbContextFactory` implementa `IDesignTimeDbContextFactory<AppDbContext>`
- [x] `SqliteInMemoryFixture` (Task 2) ganha um método `CriarContexto()` que devolve um `AppDbContext` configurado sobre a conexão in-memory já aberta

**Verification:**
- [x] Tests pass: `dotnet test --filter Produto`
- [x] Build: `dotnet build`
- [x] Manual check: inserir e ler um Produto de volta no harness in-memory funciona

**Dependencies:** Task 1, Task 2

**Files likely touched:**
- `SistemaPDV/Models/Produto.cs`
- `SistemaPDV/Data/Configurations/ProdutoConfiguration.cs`
- `SistemaPDV/Data/AppDbContext.cs`
- `SistemaPDV/Data/AppDbContextFactory.cs`
- `SistemaPDV.Tests/ProdutoConfigurationTests.cs`

**Estimated scope:** M (5 files)

---

## Task 4: Cliente

**Description:** Segunda entidade de catálogo, mesmo padrão da Task 3 mas sem precisar recriar o DbContext/Factory — só adiciona o DbSet e a configuration.

**Acceptance criteria:**
- [x] `Cliente` implementa `ISincronizavel<int>`; campos: `Nome`, `Documento` (CPF/CNPJ), `Telefone`, `Email`
- [x] `ClienteConfiguration` define chave, campos obrigatórios, índice único em `IdExterno`, `SyncStatus` como string
- [x] `AppDbContext` expõe `DbSet<Cliente> Clientes`

**Verification:**
- [x] Tests pass: `dotnet test --filter Cliente`
- [x] Build: `dotnet build`

**Dependencies:** Task 3

**Files likely touched:**
- `SistemaPDV/Models/Cliente.cs`
- `SistemaPDV/Data/Configurations/ClienteConfiguration.cs`
- `SistemaPDV.Tests/ClienteConfigurationTests.cs`

**Estimated scope:** S (3 files)

---

## Task 5: FormaPagamento

**Description:** Terceira entidade de catálogo, campos batendo com o contrato confirmado da API (`softcomshop-api-contract`).

**Acceptance criteria:**
- [x] `FormaPagamento` implementa `ISincronizavel<int>`; campos: `Nome`, `Tipo`, `Padrao`, `CodigoNfce`, `CodigoTransacaoSitef`, `CarteiraDigital`, `Ordem`, `PdvPos`, `PreVenda`, `AtalhoNumero`, `PermissaoSupervisor`
- [x] `FormaPagamentoConfiguration` define chave, campos obrigatórios, índice único em `IdExterno`
- [x] `AppDbContext` expõe `DbSet<FormaPagamento> FormasPagamento`

**Verification:**
- [x] Tests pass: `dotnet test --filter FormaPagamento`
- [x] Build: `dotnet build`

**Dependencies:** Task 3

**Files likely touched:**
- `SistemaPDV/Models/FormaPagamento.cs`
- `SistemaPDV/Data/Configurations/FormaPagamentoConfiguration.cs`
- `SistemaPDV.Tests/FormaPagamentoConfigurationTests.cs`

**Estimated scope:** S (3 files)

---

### Checkpoint: Catálogo
- [x] Produto/Cliente/FormaPagamento fazem round-trip no Sqlite in-memory (insere, lê de volta, valores batem)
- [x] `dotnet test` verde
- [x] `dotnet build` sem erros

---

## Task 6: Empresa

**Description:** Entidade de configuração da empresa/dispositivo. Inclui os campos fiscais e, com atenção especial, o certificado digital — que NUNCA é gravado em texto puro (ver Boundaries de `specs/SPEC-data-layer.md`).

**Acceptance criteria:**
- [x] `Empresa` implementa `ISincronizavel<int>`; campos essenciais (razão social, CNPJ, endereço, série/numeração NFC-e) mais `CertificadoProtegido` (string? — já pensado para armazenamento protegido, mesmo que a proteção em si seja implementada só no módulo `catalog-sync`)
- [x] `EmpresaConfiguration` define chave e campos obrigatórios
- [x] `AppDbContext` expõe `DbSet<Empresa> Empresas`
- [x] Teste comprova que `CertificadoProtegido` nunca aparece em `ToString()`/log padrão da entidade (guarda contra vazamento acidental)

**Verification:**
- [x] Tests pass: `dotnet test --filter Empresa`
- [x] Build: `dotnet build`

**Dependencies:** Task 3

**Files likely touched:**
- `SistemaPDV/Models/Empresa.cs`
- `SistemaPDV/Data/Configurations/EmpresaConfiguration.cs`
- `SistemaPDV.Tests/EmpresaConfigurationTests.cs`

**Estimated scope:** S (3 files)

---

## Task 7: Funcionario

**Description:** Entidade de funcionário/operador, incluindo o campo de login local. O `pdv_key` sincronizado da API nunca é gravado como veio — só o hash (SHA-256), conforme decidido em `specs/SPEC-caixa.md`.

**Acceptance criteria:**
- [x] `Funcionario` implementa `ISincronizavel<int>`; campos: `Nome`, `Cpf`, `Supervisor` (bool), `Desativado` (bool), `PdvKeyHash` (string — nunca `PdvKey` em claro)
- [x] `FuncionarioConfiguration` define chave, campos obrigatórios, índice único em `IdExterno`
- [x] `AppDbContext` expõe `DbSet<Funcionario> Funcionarios`
- [x] Teste comprova que não existe nenhuma propriedade/campo que guarde o pdv_key em texto puro na entidade

**Verification:**
- [x] Tests pass: `dotnet test --filter Funcionario`
- [x] Build: `dotnet build`

**Dependencies:** Task 3

**Files likely touched:**
- `SistemaPDV/Models/Funcionario.cs`
- `SistemaPDV/Data/Configurations/FuncionarioConfiguration.cs`
- `SistemaPDV.Tests/FuncionarioConfigurationTests.cs`

**Estimated scope:** S (3 files)

---

## Task 8: Caixa

**Description:** Entidade de sessão de caixa. Guarda a chave natural (`DataCaixa`+`Turno`+`FuncionarioId`) que a venda vai referenciar, além do `IdExterno` opcional preenchido quando a abertura sincronizar (ver `specs/SPEC-caixa.md`).

**Acceptance criteria:**
- [x] `Caixa` implementa `ISincronizavel<int>`; campos: `FuncionarioId` (FK), `DataCaixa`, `Turno`, `DataAbertura`, `DataFechamento` (nullable), `TrocoInicial`, `TrocoFinal` (nullable), `Status` (enum: `Aberto`/`Fechado`)
- [x] `CaixaConfiguration` define chave, FK pra `Funcionario`, índice composto em `(DataCaixa, Turno, FuncionarioId)` — reflete a chave natural que a API usa pra detectar conflito
- [x] `AppDbContext` expõe `DbSet<Caixa> Caixas`

**Verification:**
- [x] Tests pass: `dotnet test --filter Caixa`
- [x] Build: `dotnet build`

**Dependencies:** Task 7 (FK pra Funcionario)

**Files likely touched:**
- `SistemaPDV/Models/Caixa.cs`
- `SistemaPDV/Models/StatusCaixa.cs`
- `SistemaPDV/Data/Configurations/CaixaConfiguration.cs`
- `SistemaPDV.Tests/CaixaConfigurationTests.cs`

**Estimated scope:** M (4 files)

---

### Checkpoint: Referência completa
- [x] Empresa/Funcionario/Caixa fazem round-trip no Sqlite in-memory
- [x] `dotnet test` verde
- [x] `dotnet build` sem erros

---

## Task 9: ItemVenda + PagamentoVenda

**Description:** Entidades filhas do agregado de venda — ainda sem a própria `Venda`, que vem na próxima task. Preparar essas primeiro deixa a Task 10 focada só no agregado raiz e suas relações.

**Acceptance criteria:**
- [x] `ItemVenda`: `VendaId` (FK), `ProdutoId` (FK), `Quantidade`, `PrecoUnitario`, `DescontoItem`, `AcrescimoItem`
- [x] `PagamentoVenda`: `VendaId` (FK), `FormaPagamentoId` (FK), `Valor`
- [x] Configurations definem as FKs e campos obrigatórios
- [x] `AppDbContext` expõe `DbSet<ItemVenda>` e `DbSet<PagamentoVenda>`

**Verification:**
- [x] Tests pass: `dotnet test --filter "ItemVenda|PagamentoVenda"`
- [x] Build: `dotnet build`

**Dependencies:** Task 3 (Produto), Task 5 (FormaPagamento)

**Files likely touched:**
- `SistemaPDV/Models/ItemVenda.cs`
- `SistemaPDV/Models/PagamentoVenda.cs`
- `SistemaPDV/Data/Configurations/ItemVendaConfiguration.cs`
- `SistemaPDV/Data/Configurations/PagamentoVendaConfiguration.cs`
- `SistemaPDV.Tests/ItemVendaEPagamentoVendaConfigurationTests.cs`

**Estimated scope:** M (5 files)

---

## Task 10: Venda

**Description:** Agregado raiz da venda: `Guid` como chave primária (mesmo valor enviado como idempotência pra API), referências a `Caixa` e `Cliente` (nullable), coleções de `ItemVenda`/`PagamentoVenda`, e `SyncStatus` pro outbox.

**Acceptance criteria:**
- [x] `Venda` implementa `ISincronizavel<Guid>` — `Id` é `Guid` (mesmo contrato polimórfico das demais entidades, só que com `TKey = Guid`); `IdExterno` da interface fica sem uso direto (a API não devolve um id "casável" nesse padrão — ela devolve um id de venda próprio), então a entidade adiciona `VendaIdExterno` (int?) como propriedade própria pro id numérico retornado pelo servidor, sem reaproveitar `IdExterno` pra não confundir os dois conceitos
- [x] Campos: `DataHora`, `CaixaId` (FK), `ClienteId` (FK nullable), `Desconto`, `SyncStatus`, `UltimoErroSync` (string?), `TentativasEnvio` (int), `VendaIdExterno` (int?)
- [x] Navegações: `ICollection<ItemVenda> Itens`, `ICollection<PagamentoVenda> Pagamentos`
- [x] `VendaConfiguration` define a PK como Guid, as FKs, e delete behavior de cascata pra Itens/Pagamentos (apagar venda apaga itens/pagamentos junto)
- [x] `AppDbContext` expõe `DbSet<Venda> Vendas`

**Verification:**
- [x] Tests pass: `dotnet test --filter Venda`
- [x] Build: `dotnet build`
- [x] Manual check: criar uma Venda com 2 Itens e 1 Pagamento, salvar, reler do zero (novo DbContext) e conferir que as coleções vêm populadas

**Dependencies:** Task 8 (Caixa), Task 4 (Cliente), Task 9 (ItemVenda/PagamentoVenda)

**Files likely touched:**
- `SistemaPDV/Models/Venda.cs`
- `SistemaPDV/Data/Configurations/VendaConfiguration.cs`
- `SistemaPDV.Tests/VendaConfigurationTests.cs`

**Estimated scope:** M (3 files, mas o mais denso em relacionamentos)

---

### Checkpoint: Agregado de venda
- [x] Venda com Itens e Pagamentos faz round-trip completo no Sqlite in-memory
- [x] `dotnet test` verde
- [x] `dotnet build` sem erros

---

## Task 11: Migration InitialCreate

**Description:** Gera a migration real do EF Core e confirma que ela cria um banco SQLite de arquivo funcional (não só o in-memory dos testes) — é a prova final de que o modelo inteiro é consistente.

**Acceptance criteria:**
- [x] `dotnet ef migrations add InitialCreate` gera migration sem warnings de dados perdidos/pendências
- [x] `dotnet ef database update` cria `pdv.db` com todas as tabelas (Produtos, Clientes, FormasPagamento, Empresas, Funcionarios, Caixas, Vendas, ItensVenda, PagamentosVenda)
- [x] Confirmado via `dotnet ef migrations script` (sqlite3 CLI não estava disponível no ambiente) que todas as 9 tabelas e os índices únicos de `IdExterno` (+ o índice composto de `Caixas`) existem

**Verification:**
- [x] Build: `dotnet build`
- [x] Manual check: `dotnet ef database update` rodou limpo a partir de um banco inexistente; `pdv.db` removido depois (era só verificação, não faz parte do repo)

**Dependencies:** Task 6, Task 10 (precisa de todas as entidades já configuradas)

**Files likely touched:**
- `SistemaPDV/Data/Migrations/*_InitialCreate.cs`
- `SistemaPDV/Data/Migrations/*_InitialCreate.Designer.cs`
- `SistemaPDV/Data/Migrations/AppDbContextModelSnapshot.cs`

**Estimated scope:** S (arquivos gerados automaticamente, revisão manual)

---

### Checkpoint: data-layer completo
- [x] Todos os Success Criteria de `specs/SPEC-data-layer.md` atendidos
- [x] `dotnet build` e `dotnet test` verdes
- [ ] Revisão com o usuário antes de planejar a Fase 2 (`catalog-sync`)

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
- [x] Revisão com o usuário antes de planejar a Fase 2 (`catalog-sync`) — revisão pegou Produto/Cliente incompletos, corrigido; commit inicial no GitHub

---

## Fase 2: catalog-sync

## Task 12: ConfiguracaoSincronizacao (entidade)

**Description:** Nova entidade local pra guardar a configuração de sincronização: URL/link da API, `client_id`, `client_secret` protegido, nome do dispositivo, e o marcador de última sincronização de cada recurso (produtos/clientes/formas-pagamento/empresa/funcionários têm cada um seu próprio `date_sync`/`ultima_sincronizacao`, não um único marcador global). É uma extensão pontual do `data-layer` — não fazia sentido existir antes de `catalog-sync` precisar dela.

**Acceptance criteria:**
- [x] `ConfiguracaoSincronizacao`: `Id`, `UrlApi`, `ApiClienteId`, `ApiClienteSecretProtegido`, `NomeDispositivo`, `UltimaSincronizacaoProdutos`/`Clientes`/`FormasPagamento`/`Empresa`/`Funcionarios` (todos `DateTimeOffset?`)
- [x] `ConfiguracaoSincronizacaoConfiguration` define a chave; nenhum campo obrigatório (linha pode existir vazia antes da primeira configuração)
- [x] `AppDbContext` expõe `DbSet<ConfiguracaoSincronizacao>`
- [x] Nova migration `AddConfiguracaoSincronizacao`

**Verification:**
- [x] Tests pass: `dotnet test --filter ConfiguracaoSincronizacao`
- [x] Build: `dotnet build`
- [x] Manual check: `dotnet ef migrations add AddConfiguracaoSincronizacao` gerou sem warnings, aplicado com sucesso contra SQLite de arquivo real

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Models/ConfiguracaoSincronizacao.cs`
- `SistemaPDV/Data/Configurations/ConfiguracaoSincronizacaoConfiguration.cs`
- `SistemaPDV/Data/AppDbContext.cs`
- `SistemaPDV/Data/Migrations/*_AddConfiguracaoSincronizacao.*`
- `SistemaPDV.Tests/ConfiguracaoSincronizacaoConfigurationTests.cs`

**Estimated scope:** M (5 arquivos)

---

## Task 13: SegredoProtector (DPAPI)

**Description:** Serviço utilitário que protege/desprotege strings sensíveis em repouso usando Windows DPAPI (`ProtectedData`, `DataProtectionScope.CurrentUser`) — mesmo esquema do `client_secret` no projeto de referência `SistemaAvalonia`. Usado pelo `SoftcomAuthService` (Task 15) pro `client_secret`, e futuramente pelo certificado da empresa (Task 20).

**Acceptance criteria:**
- [x] `Proteger(string valor)` devolve string protegida (base64); `Desproteger(string valorProtegido)` devolve o original
- [x] `null`/vazio passa direto (não tenta proteger nada)
- [x] `Desproteger` aceita um valor não-protegido como fallback (devolve o valor original), pro caso de dado gravado antes dessa proteção existir — mesma tolerância do projeto de referência

**Verification:**
- [x] Tests pass: `dotnet test --filter Segredo`
- [x] Build: `dotnet build` (0 avisos, após marcar `[SupportedOSPlatform("windows")]` na classe e nos testes)

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Services/Sync/SegredoProtector.cs`
- `SistemaPDV.Tests/SegredoProtectorTests.cs`

**Estimated scope:** S (2 arquivos)

**Risco:** `ProtectedData`/DPAPI é específico do Windows (pacote `System.Security.Cryptography.ProtectedData`). Como o Avalonia em si é multiplataforma, isso amarra esse recurso especificamente ao Windows — aceitável pro escopo do curso/protótipo, mas vale registrar como limitação conhecida caso o app precise rodar em Linux/Mac no futuro.

---

## Task 14: PaginaApiDto + SoftcomApiClient

**Description:** O DTO genérico do envelope de paginação da API, e um cliente HTTP genérico que segue `next_page_url` até acabar — reaproveitado pelos cinco recursos sincronizáveis. Inclui a validação de domínio que impede seguir uma `next_page_url` de um host diferente do configurado (proteção contra vazar o Bearer token pra outro lugar).

**Acceptance criteria:**
- [x] `PaginaApiDto<T>`: `CurrentPage`, `Data` (`List<T>`), `NextPageUrl`, `Total`, `DateSync` (long?), mapeados via `JsonPropertyName` (snake_case da API)
- [x] `SoftcomApiClient.BuscarTudoAsync<T>(caminho, ultimaSincronizacao, accessToken, ct)` segue a paginação inteira e devolve todos os itens
- [x] Toda requisição inclui `Authorization: Bearer {token}` e o header `Api-Version`
- [x] `next_page_url` fora do domínio configurado é rejeitado sem seguir o link

**Verification:**
- [x] Tests pass: `dotnet test --filter SoftcomApiClient` (usando `HttpMessageHandler` fake, sem rede real) — 6 testes, incluindo um bug do próprio teste (substring `"page=2"` batendo em `"per_page=200"`) pego e corrigido
- [x] Build: `dotnet build`

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Services/Sync/Dtos/PaginaApiDto.cs`
- `SistemaPDV/Services/Sync/SoftcomApiClient.cs`
- `SistemaPDV.Tests/SoftcomApiClientTests.cs`

**Estimated scope:** M (3 arquivos)

---

### Checkpoint: Foundation (catalog-sync)
- [x] `dotnet build` sem erros
- [x] `dotnet test` verde (config, protector, cliente paginado genérico) — 28 testes

---

## Task 15: SoftcomAuthService

**Description:** Troca o link de cadastro do dispositivo pelo `client_secret` (uma vez, na configuração inicial), e troca `client_id`/`client_secret` por um `access_token` a cada sincronização. Usa o `SegredoProtector` (Task 13) pra ler/gravar o `client_secret` protegido em `ConfiguracaoSincronizacao` (Task 12).

**Acceptance criteria:**
- [x] `ObterClienteSecretAsync(link, nomeDispositivo)`: `GET {link}&device_id={nomeDispositivo}` → extrai `data.client_secret`
- [x] `ObterTokenAsync(configuracao)`: `POST {dominio}/softauth/authentication/token` form-urlencoded (`grant_type=client_credentials`) → extrai `data.token` ou `data.access_token`
- [x] Falhas de rede/HTTP não-2xx devolvem um resultado `(Sucesso, Mensagem)` em vez de lançar exceção não tratada — mesmo padrão do projeto de referência

**Verification:**
- [x] Tests pass: `dotnet test --filter SoftcomAuthService` (`HttpMessageHandler` fake)
- [x] Build: `dotnet build`

**Dependencies:** Task 12, Task 13

**Files likely touched:**
- `SistemaPDV/Services/Sync/SoftcomAuthService.cs`
- `SistemaPDV.Tests/SoftcomAuthServiceTests.cs`

**Estimated scope:** M (2 arquivos, lógica um pouco densa)

---

### Checkpoint: Autenticação pronta
- [x] `dotnet test` verde — 35 testes
- [x] `dotnet build` sem erros

---

## Task 16: Sincronização de FormaPagamento

**Description:** Primeiro recurso sincronizado de ponta a ponta — o mais simples dos cinco (sem owned type/coleção), serve de modelo pros próximos. DTO fiel ao schema confirmado + lógica de upsert por `IdExterno` dentro do `CatalogSyncService` (que nasce aqui e cresce nas próximas tasks).

**Acceptance criteria:**
- [x] `FormaPagamentoApiDto` com todos os campos confirmados (`id`, `nome`, `tipo`, `padrao`, `codigo_nfce`, `codigo_transacao_sitef`, `carteira_digital`, `ordem`, `pdv_pos`, `pre_venda`, `atalho_numero`, `permissao_supervisor`), `JsonNumberHandling.AllowReadingFromString` onde a API manda número como string
- [x] `CatalogSyncService.SincronizarFormasPagamentoAsync()`: busca via `SoftcomApiClient`, upsert por `IdExterno` (cria se não existe, atualiza se existe), marca `SyncStatus.Sincronizado`
- [x] Atualiza `ConfiguracaoSincronizacao.UltimaSincronizacaoFormasPagamento` com o `date_sync` da resposta

**Verification:**
- [x] Tests pass: `dotnet test --filter FormaPagamento` (cobre: primeira sync cria N registros; segunda sync com mesmo `IdExterno` atualiza em vez de duplicar; falha HTTP não lança exceção)
- [x] Build: `dotnet build` (0 avisos — inclui correção de granularidade do `[SupportedOSPlatform]`)

**Dependencies:** Task 14

**Files likely touched:**
- `SistemaPDV/Services/Sync/Dtos/FormaPagamentoApiDto.cs`
- `SistemaPDV/Services/Sync/CatalogSyncService.cs`
- `SistemaPDV.Tests/CatalogSyncServiceFormaPagamentoTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 17: Sincronização de Cliente

**Description:** Segundo recurso — introduz o mapeamento do owned type (`TabelaPreco`) e das conversões de tipo (`pessoa` "FISICA"/"JURIDICA" → `TipoPessoa`, `bloqueado` "0"/"1" → `bool`).

**Acceptance criteria:**
- [x] `ClienteApiDto` com o schema completo confirmado
- [x] `CatalogSyncService.SincronizarClientesAsync()`: upsert por `IdExterno`, mapeia `tabela_preco` pro owned type, converte `pessoa`/`bloqueado`
- [x] Atualiza `ConfiguracaoSincronizacao.UltimaSincronizacaoClientes`

**Verification:**
- [x] Tests pass: `dotnet test --filter Cliente`
- [x] Build: `dotnet build`

**Dependencies:** Task 14, Task 16

**Files likely touched:**
- `SistemaPDV/Services/Sync/Dtos/ClienteApiDto.cs`
- `SistemaPDV/Services/Sync/CatalogSyncService.cs`
- `SistemaPDV.Tests/CatalogSyncServiceClienteTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 18: Sincronização de Produto

**Description:** Terceiro recurso — introduz o mapeamento da coleção owned (`ImagemProduto[]`), e só os campos do escopo já decidido (retail genérico + fiscal essencial + imagens; sem combustível/restaurante).

**Acceptance criteria:**
- [x] `ProdutoApiDto` com os campos do escopo aprovado (ver `softcomshop-api-contract` na memória) + `produto_imagem[]`
- [x] `CatalogSyncService.SincronizarProdutosAsync()`: upsert por `IdExterno`, popula `Imagens`
- [x] Atualiza `ConfiguracaoSincronizacao.UltimaSincronizacaoProdutos`

**Verification:**
- [x] Tests pass: `dotnet test --filter Produto`
- [x] Build: `dotnet build`

**Dependencies:** Task 14, Task 17

**Files likely touched:**
- `SistemaPDV/Services/Sync/Dtos/ProdutoApiDto.cs`
- `SistemaPDV/Services/Sync/CatalogSyncService.cs`
- `SistemaPDV.Tests/CatalogSyncServiceProdutoTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 19: Sincronização de Funcionario

**Description:** Quarto recurso — introduz o hash do `pdv_key` (SHA-256) antes de persistir, nunca o valor cru, e o mapeamento dos objetos aninhados (`endereco`, `setor`, `funcao`, `usuario`).

**Acceptance criteria:**
- [x] `FuncionarioApiDto` com os campos confirmados, incluindo `usuario.pdv_key`
- [x] `CatalogSyncService.SincronizarFuncionariosAsync()`: upsert por `IdExterno`, grava `PdvKeyHash = Sha256(dto.Usuario.PdvKey)` — nunca `dto.Usuario.PdvKey` cru
- [x] Atualiza `ConfiguracaoSincronizacao.UltimaSincronizacaoFuncionarios`

**Verification:**
- [x] Tests pass: `dotnet test --filter Funcionario`
- [x] Build: `dotnet build`
- [x] Manual check: nenhum teste ou log imprime o `pdv_key` recebido do DTO em texto puro

**Ajuste feito durante a implementação:** `Funcionario.PdvKeyHash` (data-layer) virou opcional (`string?`, era `required string`) — nem todo funcionário sincronizado necessariamente tem `pdv_key` do lado da API. Nova migration `TornarPdvKeyHashOpcional` gerada e verificada contra SQLite real.

**Dependencies:** Task 14, Task 18

**Files likely touched:**
- `SistemaPDV/Services/Sync/Dtos/FuncionarioApiDto.cs`
- `SistemaPDV/Services/Sync/CatalogSyncService.cs`
- `SistemaPDV.Tests/CatalogSyncServiceFuncionarioTests.cs`

**Estimated scope:** M (3 arquivos)

---

### Checkpoint: Recursos de catálogo sincronizando individualmente
- [x] FormaPagamento, Cliente, Produto e Funcionario sincronizam cada um isoladamente contra um `HttpMessageHandler` fake
- [x] `dotnet test` verde — 47 testes

---

## Task 20: Sincronização de Empresa

**Description:** Quinto e último recurso — um único registro (não lista, na prática), e o único que lida com dado realmente sensível: `empresa_certificado`/`empresa_certificado_senha` nunca são persistidos crus, sempre via `SegredoProtector` (Task 13).

**Acceptance criteria:**
- [x] `EmpresaApiDto` com os campos do escopo já modelado em `data-layer` (`Empresa.cs`)
- [x] `CatalogSyncService.SincronizarEmpresaAsync()`: upsert (na prática sempre 1 registro), protege `empresa_certificado`/`empresa_certificado_senha` antes de gravar em `Empresa.CertificadoProtegido`
- [x] Atualiza `ConfiguracaoSincronizacao.UltimaSincronizacaoEmpresa`

**Verification:**
- [x] Tests pass: `dotnet test --filter Empresa`
- [x] Build: `dotnet build` (0 avisos)
- [x] Manual check: nenhum teste ou log imprime o certificado/senha em texto puro — certificado e senha são combinados num JSON e protegidos juntos via DPAPI (`ProtegerCertificado`)

**Dependencies:** Task 13, Task 14, Task 19

**Files likely touched:**
- `SistemaPDV/Services/Sync/Dtos/EmpresaApiDto.cs`
- `SistemaPDV/Services/Sync/CatalogSyncService.cs`
- `SistemaPDV.Tests/CatalogSyncServiceEmpresaTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 21: CatalogSyncService — orquestração completa

**Description:** Amarra tudo: autentica uma vez (Task 15), sincroniza os cinco recursos em sequência (Tasks 16-20), e devolve um resumo com as contagens de cada um. É o ponto de entrada único que o `pdv-ui` vai chamar mais pra frente.

**Acceptance criteria:**
- [x] `SincronizarTudoAsync()` devolve um `ResultadoSincronizacaoCompleta { FormasPagamento, Clientes, Produtos, Funcionarios, Empresa, TudoComSucesso }`
- [x] Se a autenticação falhar, aborta sem tentar nenhum recurso, resultado indica o erro claramente
- [x] Se um recurso falhar no meio da sequência, os recursos já sincronizados antes dele permanecem persistidos (não é uma transação única tudo-ou-nada) — garantido porque cada `Sincronizar*Async` já salva por conta própria

**Verification:**
- [x] Tests pass: `dotnet test --filter CatalogSync`
- [x] Build: `dotnet build`
- [x] Manual check: teste de integração fim-a-fim (`SqliteInMemoryFixture` + `HttpMessageHandler` fake simulando os 5 endpoints) sincroniza tudo numa chamada só — 51 testes no total do módulo

**Dependencies:** Task 15, Task 16, Task 17, Task 18, Task 19, Task 20

**Files likely touched:**
- `SistemaPDV/Services/Sync/CatalogSyncService.cs` (finalizado)
- `SistemaPDV.Tests/CatalogSyncServiceIntegracaoTests.cs`

**Estimated scope:** M (2 arquivos, mas o teste de integração é mais longo)

---

### Checkpoint: catalog-sync completo
- [x] Todos os Success Criteria de `specs/SPEC-catalog-sync.md` atendidos
- [x] `dotnet build` e `dotnet test` verdes — 51 testes
- [x] Revisão com o usuário antes de planejar a Fase 3 (`caixa`) — decisão de não generalizar o upsert via ISincronizavel<TKey> confirmada

---

## Fase 3: caixa

## Task 22: Caixa.UltimoErroSync

**Description:** Adiciona o campo `UltimoErroSync` (string?) na entidade `Caixa` — mesmo padrão já usado em `Venda`, pra guardar a mensagem de erro de uma sincronização de abertura/fechamento que falhou, sem travar o operador nem perder o motivo do erro.

**Acceptance criteria:**
- [x] `Caixa.UltimoErroSync` (string?) adicionado
- [x] Migration gerada e aplicada contra SQLite real

**Verification:**
- [x] Tests pass: `dotnet test --filter Caixa`
- [x] Build: `dotnet build`

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Models/Caixa.cs`
- `SistemaPDV/Data/Migrations/*_AddCaixaUltimoErroSync.*`

**Estimated scope:** XS (2 arquivos)

---

## Task 23: DigitacaoCaixa + DigitacaoBandeiraCaixa

**Description:** Entidades novas pra guardar a conferência de fechamento (quanto o operador apurou por forma de pagamento, e por bandeira de cartão) — precisam existir localmente porque o fechamento é offline-first: os valores digitados ficam guardados até a sincronização conseguir enviar pro `POST .../caixa-funcoes/fechar`.

**Acceptance criteria:**
- [x] `DigitacaoCaixa`: `CaixaId` (FK), `FormaPagamentoId` (FK), `Valor`
- [x] `DigitacaoBandeiraCaixa`: `CaixaId` (FK), `Bandeira` (string), `Valor`
- [x] `Caixa` ganha `ICollection<DigitacaoCaixa> Digitacoes` e `ICollection<DigitacaoBandeiraCaixa> DigitacoesBandeiras`, cascade delete (mesmo padrão de `Venda.Itens`/`Pagamentos`)
- [x] `AppDbContext` expõe os dois `DbSet`
- [x] Migration gerada e aplicada contra SQLite real

**Verification:**
- [x] Tests pass: `dotnet test --filter Digitacao`
- [x] Build: `dotnet build`

**Dependencies:** Task 22

**Files likely touched:**
- `SistemaPDV/Models/DigitacaoCaixa.cs`
- `SistemaPDV/Models/DigitacaoBandeiraCaixa.cs`
- `SistemaPDV/Models/Caixa.cs`
- `SistemaPDV/Data/Configurations/DigitacaoCaixaConfiguration.cs`
- `SistemaPDV/Data/Configurations/DigitacaoBandeiraCaixaConfiguration.cs`
- `SistemaPDV/Data/Configurations/CaixaConfiguration.cs`
- `SistemaPDV/Data/Migrations/*_AddDigitacaoCaixa.*`
- `SistemaPDV.Tests/DigitacaoCaixaConfigurationTests.cs`

**Estimated scope:** M (7 arquivos, mas mecânico — mesmo padrão de Venda/ItemVenda)

---

## Task 24: PdvKeyHasher

**Description:** Extrai a lógica de hash SHA-256 do `pdv_key`, hoje privada dentro de `CatalogSyncService`, pra uma classe compartilhada — `LoginOperadorService` (Task 25) precisa exatamente da mesma lógica, e duplicar código de hash de segurança é arriscado (se um dia mudar num lugar só, login e sincronização ficam dessincronizados silenciosamente, sem erro nenhum na hora — só logins passando a falhar).

**Acceptance criteria:**
- [x] `PdvKeyHasher.Hash(string? pdvKey)` (static) — mesma lógica que já existia em `CatalogSyncService.HashPdvKey`
- [x] `CatalogSyncService` passa a usar `PdvKeyHasher.Hash` em vez do método privado (removido)
- [x] Testes de `CatalogSyncService` (Task 19) continuam passando sem alteração — comprova que o comportamento não mudou, só o lugar do código

**Verification:**
- [x] Tests pass: `dotnet test --filter "PdvKeyHasher|Funcionario"`
- [x] Build: `dotnet build`

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Services/PdvKeyHasher.cs`
- `SistemaPDV/Services/Sync/CatalogSyncService.cs`
- `SistemaPDV.Tests/PdvKeyHasherTests.cs`

**Estimated scope:** S (3 arquivos)

---

## Task 25: LoginOperadorService

**Description:** Login local do operador no terminal — sem rede, comparando o hash da chave digitada contra o `PdvKeyHash` já sincronizado.

**Acceptance criteria:**
- [x] `AutenticarAsync(string pdvKeyDigitado)` devolve o `Funcionario` se o hash bater e ele não estiver `Desativado`; `null` caso contrário
- [x] Não faz nenhuma chamada de rede

**Verification:**
- [x] Tests pass: `dotnet test --filter LoginOperador`
- [x] Build: `dotnet build`

**Dependencies:** Task 24

**Files likely touched:**
- `SistemaPDV/Services/Caixa/LoginOperadorService.cs`
- `SistemaPDV.Tests/LoginOperadorServiceTests.cs`

**Estimated scope:** S (2 arquivos)

---

### Checkpoint: Foundation + Login (caixa)
- [x] `dotnet build` sem erros
- [x] `dotnet test` verde — 62 testes

---

## Task 26: CaixaService.AbrirCaixaLocalAsync

**Description:** Abre um caixa **localmente**, sem rede — grava `DataCaixa`/`Turno`/`FuncionarioId`/`TrocoInicial` imediatamente, com `Status = Aberto` e `SyncStatus = PendenteSync`. É esse registro local que a venda vai referenciar depois (pela chave natural), não o id remoto.

**Acceptance criteria:**
- [x] `AbrirCaixaLocalAsync(funcionarioId, dataCaixa, turno, trocoInicial)` cria e salva um `Caixa` local, devolve a entidade criada
- [x] Impede abrir um segundo caixa **local** com a mesma chave natural (`DataCaixa`+`Turno`+`FuncionarioId`) enquanto o anterior ainda não sincronizou — reflete o Boundary da spec ("Perguntar antes: permitir dois caixas locais abertos ao mesmo tempo")
- [x] Não faz nenhuma chamada de rede

**Verification:**
- [x] Tests pass: `dotnet test --filter AbrirCaixaLocal`
- [x] Build: `dotnet build`

**Dependencies:** Task 23

**Files likely touched:**
- `SistemaPDV/Services/Caixa/CaixaService.cs`
- `SistemaPDV.Tests/CaixaServiceAbrirTests.cs`

**Estimated scope:** S (2 arquivos)

---

## Task 27: CaixaSyncService — sincroniza abertura pendente

**Description:** Outbox da abertura: pega o caixa local com `SyncStatus.PendenteSync` e `IdExterno` nulo, chama `POST /api/v2/financeiro/caixa-funcoes/abrir`. Sucesso grava o `id` remoto; `409` (já existe caixa aberto pra essa data/operador/turno) é tratado como "já sincronizado" em vez de erro (ver Open Questions da spec); `422`/erro de rede marca `SyncStatus.FalhaSync` com a mensagem em `UltimoErroSync`, sem travar o operador.

**Acceptance criteria:**
- [x] `SincronizarAberturaAsync(accessToken)`: `200/201` → grava `IdExterno`, `SyncStatus = Sincronizado`
- [x] `409` → `SyncStatus = Sincronizado` (tratado como já existente), sem quebrar
- [x] `422`/falha de rede → `SyncStatus = FalhaSync`, `UltimoErroSync` preenchido, não lança exceção

**Verification:**
- [x] Tests pass: `dotnet test --filter SincronizarAbertura`
- [x] Build: `dotnet build`

**Dependencies:** Task 26, Task 14 (SoftcomApiClient/padrão de request)

**Files likely touched:**
- `SistemaPDV/Services/Caixa/CaixaSyncService.cs`
- `SistemaPDV/Services/Caixa/Dtos/CaixaFuncaoApiDto.cs`
- `SistemaPDV.Tests/CaixaSyncServiceAberturaTests.cs`

**Estimated scope:** M (3 arquivos)

---

### Checkpoint: Abertura offline-first completa
- [x] Abrir caixa funciona sem rede; sincronizar depois (com rede) preenche o `IdExterno`
- [x] `dotnet test` verde — 70 testes

---

## Task 28: CaixaService.FecharCaixaLocalAsync

**Description:** Fecha o caixa **localmente** — grava `DataFechamento`/`TrocoFinal`, a digitação por forma de pagamento e por bandeira (`DigitacaoCaixa`/`DigitacaoBandeiraCaixa`), muda `Status` pra `Fechado` e `SyncStatus` volta pra `PendenteSync` (o registro mudou, precisa sincronizar de novo — dessa vez o fechamento).

**Acceptance criteria:**
- [x] `FecharCaixaLocalAsync(caixaId, trocoFinal, digitacoes, digitacoesBandeiras)` grava tudo local, `Status = Fechado`, `SyncStatus = PendenteSync`
- [x] Não permite fechar um caixa que já está `Fechado`
- [x] Não faz nenhuma chamada de rede

**Verification:**
- [x] Tests pass: `dotnet test --filter FecharCaixaLocal`
- [x] Build: `dotnet build`

**Dependencies:** Task 26

**Files likely touched:**
- `SistemaPDV/Services/Caixa/CaixaService.cs`
- `SistemaPDV.Tests/CaixaServiceFecharTests.cs`

**Estimated scope:** S (2 arquivos)

---

## Task 29: CaixaSyncService — sincroniza fechamento pendente

**Description:** Outbox do fechamento: pega o caixa local `Status = Fechado` com `SyncStatus.PendenteSync` (e `IdExterno` já preenchido — a abertura precisa ter sincronizado antes), monta a digitação salva localmente e chama `POST /api/v2/financeiro/caixa-funcoes/fechar`.

**Acceptance criteria:**
- [x] `SincronizarFechamentoAsync(accessToken)`: `200` → `SyncStatus = Sincronizado`
- [x] `422` (já fechado, ou campo faltando) → `SyncStatus = FalhaSync`, `UltimoErroSync` preenchido, sem travar
- [x] Se o caixa ainda não tem `IdExterno` (abertura não sincronizou), não tenta fechar ainda — devolve um resultado indicando que precisa sincronizar a abertura primeiro

**Verification:**
- [x] Tests pass: `dotnet test --filter SincronizarFechamento`
- [x] Build: `dotnet build`

**Dependencies:** Task 27, Task 28

**Files likely touched:**
- `SistemaPDV/Services/Caixa/CaixaSyncService.cs`
- `SistemaPDV.Tests/CaixaSyncServiceFechamentoTests.cs`

**Estimated scope:** M (2 arquivos)

---

### Checkpoint: Fechamento offline-first completo
- [x] Fechar caixa funciona sem rede; sincronizar depois envia a digitação corretamente
- [x] `dotnet test` verde — 77 testes

---

## Task 30: SincronizarCaixaPendenteAsync — orquestração

**Description:** Ponto de entrada único do módulo: olha o estado do caixa local pendente e decide sozinho se chama sincronizar-abertura ou sincronizar-fechamento — quem for usar isso depois (o futuro sync geral do app, ou uma tela) não precisa saber a regra de decisão.

**Acceptance criteria:**
- [x] `SincronizarCaixaPendenteAsync(accessToken)`: se existe caixa `PendenteSync` sem `IdExterno` → sincroniza abertura; se existe caixa `Fechado` + `PendenteSync` com `IdExterno` → sincroniza fechamento; se não há nada pendente → não faz nada, devolve resultado neutro

**Verification:**
- [x] Tests pass: `dotnet test --filter SincronizarCaixaPendente`
- [x] Build: `dotnet build`
- [x] Manual check: teste de integração cobrindo o ciclo completo (abrir local → sincronizar → fechar local → sincronizar) contra `HttpMessageHandler` fake — 78 testes no total do projeto

**Dependencies:** Task 27, Task 29

**Files likely touched:**
- `SistemaPDV/Services/Caixa/CaixaSyncService.cs`
- `SistemaPDV.Tests/CaixaSyncServiceIntegracaoTests.cs`

**Estimated scope:** M (2 arquivos)

---

### Checkpoint: caixa completo
- [x] Todos os Success Criteria de `specs/SPEC-caixa.md` atendidos
- [x] `dotnet build` e `dotnet test` verdes — 78 testes
- [x] Revisão com o usuário antes de planejar a Fase 4 (`sales`) — reaproveitar ResultadoSincronizacaoRecurso confirmado

---

## Fase 4: sales

## Task 31: ErroApiExtractor

**Description:** Extrai a lógica de parsing de `{"errors": {...}}` que hoje existe duplicada dentro de `CaixaSyncService` (`ExtrairMensagemErro`), pra um lugar compartilhado — `VendaSyncService` (Task 35) vai precisar exatamente da mesma lógica, e esse é o terceiro uso do mesmo padrão de erro genérico da API (o primeiro foi implícito nos outros serviços). Diferente do upsert genérico que decidimos NÃO compartilhar no `catalog-sync`, aqui não tem risco de tradução de LINQ — é parsing de string puro, seguro de extrair.

**Acceptance criteria:**
- [x] `ErroApiExtractor.Extrair(string conteudo)` (static) — mesma lógica que já existia em `CaixaSyncService.ExtrairMensagemErro`
- [x] `CaixaSyncService` passa a usar `ErroApiExtractor.Extrair`
- [x] Testes de `CaixaSyncService` continuam passando sem alteração

**Verification:**
- [x] Tests pass: `dotnet test --filter "ErroApiExtractor|CaixaSync"`
- [x] Build: `dotnet build`

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Services/ErroApiExtractor.cs`
- `SistemaPDV/Services/Caixa/CaixaSyncService.cs`
- `SistemaPDV.Tests/ErroApiExtractorTests.cs`

**Estimated scope:** S (3 arquivos)

---

## Task 32: ConfiguracaoSincronizacao.ClienteConsumidorFinalIdExterno

**Description:** Campo novo pra configurar o id externo do cliente "Consumidor Final" usado em vendas avulsas (sem cliente selecionado) — configurável em vez de um número `1` cravado no código, já que a evidência de que é esse o id é forte mas não 100% confirmada.

**Acceptance criteria:**
- [x] `ConfiguracaoSincronizacao.ClienteConsumidorFinalIdExterno` (int?) adicionado
- [x] Migration gerada e aplicada contra SQLite real

**Verification:**
- [x] Tests pass: `dotnet test --filter ConfiguracaoSincronizacao`
- [x] Build: `dotnet build`

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Models/ConfiguracaoSincronizacao.cs`
- `SistemaPDV/Data/Migrations/*_AddClienteConsumidorFinal.*`

**Estimated scope:** XS (2 arquivos)

---

## Task 33: VendaApiDto

**Description:** DTOs de request (só o subconjunto essencial confirmado — ver `SPEC-sales.md`) e response de `POST /api/v2/vendas`.

**Acceptance criteria:**
- [x] Request: `guid`, `data_hora` (unix int), `empresa_id`, `usuario_id`, `funcionario_id`, `cliente_id`, `caixa_data`, `caixa_turno`, `caixa_funcoes_id` (nullable), `produtos[]` (`produto_id`, `preco`, `quantidade`, descontos/acréscimos), `pagamentos[]` (`forma_pagamento_id`, `valor_pagamento`)
- [x] Response de sucesso: `{ "data": { "id": ... } }`

**Verification:**
- [x] Tests pass: `dotnet test --filter VendaApiDto`
- [x] Build: `dotnet build`

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Services/Sales/Dtos/VendaApiDto.cs`
- `SistemaPDV.Tests/VendaApiDtoTests.cs`

**Estimated scope:** S (2 arquivos)

---

### Checkpoint: Foundation (sales)
- [x] `dotnet build` sem erros
- [x] `dotnet test` verde — 85 testes

---

## Task 34: VendaService.RegistrarVendaLocalAsync

**Description:** Registra a venda **localmente**, sem rede — gera o `Guid` (chave de idempotência), monta `Itens`/`Pagamentos`, resolve `ClienteId` (o informado, ou nenhum = venda avulsa, sem tentar resolver o id do Consumidor Final aqui — isso é papel do `VendaSyncService` na hora de montar o payload, ver Task 35), marca `SyncStatus.PendenteSync`.

**Acceptance criteria:**
- [x] `RegistrarVendaLocalAsync(caixaId, clienteId?, itens, pagamentos)` cria e salva a `Venda` com `Guid` novo, devolve a entidade
- [x] Não faz nenhuma chamada de rede — salva e retorna imediatamente
- [x] Suporta pagamento misto (mais de uma forma de pagamento na mesma venda) desde o início

**Verification:**
- [x] Tests pass: `dotnet test --filter RegistrarVendaLocal`
- [x] Build: `dotnet build`

**Dependencies:** Task 33

**Files likely touched:**
- `SistemaPDV/Services/Sales/VendaService.cs`
- `SistemaPDV.Tests/VendaServiceTests.cs`

**Estimated scope:** S (2 arquivos)

---

## Task 35: VendaSyncService.SincronizarVendaAsync

**Description:** Outbox de uma venda: monta o payload (resolvendo `ClienteConsumidorFinalIdExterno` quando `ClienteId` é nulo, `caixa_data`/`caixa_turno`/`caixa_funcoes_id` a partir do caixa local), envia, e trata os três casos de resposta.

**Acceptance criteria:**
- [x] `200` → grava `VendaIdExterno`, `SyncStatus = Sincronizado`
- [x] `409` (guid já existe) → `SyncStatus = Sincronizado`, sem duplicar, sem erro visível
- [x] `422`/erro de rede → `SyncStatus = FalhaSync`, `UltimoErroSync` preenchido (via `ErroApiExtractor`), venda continua elegível pra nova tentativa
- [x] `data_hora` enviado como inteiro Unix (não string)

**Verification:**
- [x] Tests pass: `dotnet test --filter SincronizarVenda`
- [x] Build: `dotnet build`

**Dependencies:** Task 31, Task 32, Task 34

**Files likely touched:**
- `SistemaPDV/Services/Sales/VendaSyncService.cs`
- `SistemaPDV.Tests/VendaSyncServiceTests.cs`

**Estimated scope:** M (2 arquivos, lógica mais densa)

---

## Task 36: VendaSyncService.SincronizarVendasPendentesAsync

**Description:** Percorre todas as vendas `PendenteSync` (inclusive as que já tentaram e falharam antes — `FalhaSync` também é elegível pra nova tentativa) e chama `SincronizarVendaAsync` pra cada uma. Ponto de entrada que o futuro sync geral do app vai chamar.

**Acceptance criteria:**
- [x] Reenvia só as vendas ainda não confirmadas (`Sincronizado`), inclusive as que falharam antes (`FalhaSync` é elegível pra retry automático — confirmado com o usuário 2026-09-18, evita venda presa numa falha antiga já resolvida)
- [x] Uma venda com erro não impede as outras de serem tentadas (mesmo princípio do `catalog-sync`: não é tudo-ou-nada)

**Verification:**
- [x] Tests pass: `dotnet test --filter SincronizarVendasPendentes`
- [x] Build: `dotnet build`
- [x] Manual check: teste de integração cobrindo o ciclo completo (registrar venda local → sincronizar → confirmar `VendaIdExterno`) contra `HttpMessageHandler` fake — 96 testes no total do projeto

**Dependencies:** Task 35

**Files likely touched:**
- `SistemaPDV/Services/Sales/VendaSyncService.cs`
- `SistemaPDV.Tests/VendaSyncServiceIntegracaoTests.cs`

**Estimated scope:** S (2 arquivos)

---

### Checkpoint: sales completo
- [x] Todos os Success Criteria de `specs/SPEC-sales.md` atendidos
- [x] `dotnet build` e `dotnet test` verdes — 96 testes
- [x] Revisão com o usuário antes de planejar a Fase 5 (`pdv-ui`)

---

## Revisão de código pós-sales (2026-09-18)

Skill `code-review-and-quality` (nível `high`, 8 agentes) rodada sobre o diff acumulado de `catalog-sync` + `caixa` + `sales` desde o commit inicial. 13 achados reportados; os 7 de correção real foram corrigidos na hora, com testes de regressão novos pra cada um (ver `docs/APRENDIZADOS.md` #17-19 pros detalhes e o "porquê"):

- [x] `CaixaSyncService`: fechamento de caixa perdido pra sempre se aberto+fechado 100% offline antes de sincronizar — corrigido com o campo `Caixa.AberturaSincronizada`, dedicado a rastrear a abertura separado do fechamento
- [x] `CaixaSyncService`: `data_caixa` enviado como `DataAbertura` (timestamp) em vez de `DataCaixa` (data) — corrigido com `FormatarDataCaixa`
- [x] `CaixaSyncService`: `409` na abertura não gravava `AberturaSincronizada`/travava o fechamento — corrigido junto com o item acima
- [x] `CaixaSyncService`: caixa com `FalhaSync` nunca era retentado — corrigido (query de abertura e fechamento agora aceitam `FalhaSync`)
- [x] `VendaSyncService`: lote de vendas podia abortar por completo se uma venda lançasse exceção — corrigido com `try/catch` no laço de `SincronizarVendasPendentesAsync`
- [x] `CatalogSyncService`: resync de funcionário podia apagar `PdvKeyHash` que já funcionava — corrigido pra só sobrescrever quando a API manda um valor novo
- [x] `CaixaService`: `FecharCaixaLocalAsync` sem validação de `formaPagamentoId`/`Bandeira`, e `AbrirCaixaLocalAsync` com corrida (TOCTOU) — corrigidos com validação prévia e captura de `DbUpdateException`

Os 6 achados de limpeza/eficiência (duplicação de `JsonSerializerOptions`, montagem de request HTTP repetida, N+1 no catalog-sync, falta de tratamento de 401) ficaram registrados mas **não foram corrigidos nessa passada** — são cleanup, não bugs de correção. Retomar quando fizer sentido (ex: antes de `pdv-ui` usar esses serviços de verdade, ou numa passada de simplificação dedicada).

Migration nova: `AddAberturaSincronizada`. Testes: 96 → 105 (9 testes novos cobrindo especificamente os bugs corrigidos).

### Polimento (2026-09-18)

Os 5 achados de limpeza/eficiência deixados de fora da passada acima, resolvidos a pedido do usuário ("vamos fazer logo os polimentos") — ver `docs/APRENDIZADOS.md` #21-22 pro "porquê" de cada decisão:

- [x] `JsonSerializerOptions` duplicado em 3 lugares — unificado em `Services/SoftcomJson.cs` (`SoftcomJson.Opcoes`)
- [x] Montagem de request HTTP (headers, envio, classificação da resposta) duplicada em `CaixaSyncService` e `VendaSyncService` — unificada em `SoftcomApiClient.EnviarAsync`, que devolve um `ResultadoEnvio` (`Services/Sync/ResultadoEnvio.cs`)
- [x] `MarcarFalhaAsync` privado quase idêntico nos dois serviços de sync — unificado em `Services/OutboxHelper.cs` (`OutboxHelper.MarcarFalhaAsync<T>`, genérico)
- [x] N+1 nos 4 upserts em lote de `CatalogSyncService` (uma consulta ao banco por item do lote) — corrigido com uma única consulta em lote por página, usando o mesmo dicionário `processados` que já existia pra deduplicar entre páginas
- [x] 401 (token expirado) tratado igual a qualquer outra falha — agora distinguido como `ResultadoEnvioTipo.TokenExpirado` em `SoftcomApiClient`, mas os chamadores ainda reagem a ele do mesmo jeito que a `Falha` (retry automático com renovação de token fica pra quando fizer sentido — é uma mudança de arquitetura maior, não um polimento pequeno)

`CaixaSyncService` e `VendaSyncService` passaram a receber `SoftcomApiClient` no construtor em vez de `HttpClient` puro (pra poder usar `EnviarAsync`) — os 7 arquivos de teste que os construíam diretamente foram ajustados pra passar `new SoftcomApiClient(httpClient)`. Build limpo, 0 avisos.

Skill `code-review-and-quality` (nível `high`) rodada sobre esse polimento antes do commit — achou 1 lacuna de cobertura: `ResultadoEnvioTipo.TokenExpirado` (código novo dessa passada) não tinha nenhum teste de regressão garantindo que um 401 realmente cai no tratamento de falha em `CaixaSyncService`/`VendaSyncService`. Corrigido na hora com 2 testes novos (`TokenExpiradoEhTratadoComoFalhaEMarcaFalhaSync` em `CaixaSyncServiceAberturaTests`, `TokenExpiradoEhTratadoComoFalhaEIncrementaTentativas` em `VendaSyncServiceTests`). Testes: 105 → 107.

## Fase 5: pdv-ui

Spec aprovada em `specs/SPEC-pdv-ui.md` (2026-09-18). Depende de `catalog-sync`, `caixa`, `sales` — todos completos, revisados e publicados (commits `d6a389f`..`28db0af`). 4 novidades descobertas na revisão da spec, sem existir nos módulos anteriores: `AppServices` (composition root), `ConfiguracaoSincronizacao.ExigirAberturaCaixa`, `CatalogSyncService.SincronizarClienteNovoAsync` (push de cliente) e `SincronizacaoBackgroundService` (timer automático).

## Task 37: ConfiguracaoSincronizacao.ExigirAberturaCaixa

**Description:** Campo novo (`bool`, default `true`) em `ConfiguracaoSincronizacao` — decide se o fluxo pós-login exige caixa aberto antes de liberar Dashboard/PDV, ou se abrir caixa vira uma ação opcional pelo menu. Mesma filosofia já aplicada a `ClienteConsumidorFinalIdExterno`: regra de negócio configurável, não fixa no código.

**Acceptance criteria:**
- [x] `ConfiguracaoSincronizacao.ExigirAberturaCaixa` existe, default `true`
- [x] Migration `AddExigirAberturaCaixa` aplicada, `AppDbContextModelSnapshot.cs` atualizado

**Verification:**
- [x] Tests pass: `dotnet test --filter ConfiguracaoSincronizacao` — 108 testes no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Models/ConfiguracaoSincronizacao.cs`
- `SistemaPDV/Data/Configurations/ConfiguracaoSincronizacaoConfiguration.cs`
- `SistemaPDV/Data/Migrations/*_AddExigirAberturaCaixa.cs` (+ Designer + snapshot)

**Estimated scope:** S (3-4 arquivos, mecânico)

---

## Task 38: AppServices — composition root

**Description:** Classe simples com propriedades públicas pros serviços já prontos de `catalog-sync`/`caixa`/`sales` (`CatalogSyncService`, `LoginOperadorService`, `CaixaService`, `CaixaSyncService`, `VendaService`, `VendaSyncService`). Montada uma vez em `App.axaml.cs`. Sem container de DI — wiring manual, decisão do usuário (2026-09-18).

**Acceptance criteria:**
- [x] `AppServices` expõe só os serviços prontos (nunca `HttpClient`/`SegredoProtector`/`Func<AppDbContext>` soltos) — ViewModel nunca fala com infraestrutura direto, decisão confirmada com o usuário (2026-09-18)
- [x] `App.axaml.cs` cria uma única instância e a torna acessível pros ViewModels (via construtor, não singleton estático global) — guardada como `App.Services`, pronta pra Task 42 passar pro `ShellViewModel`
- [x] `HttpClient` é uma única instância compartilhada internamente (não um novo por chamada) — mas fica privado dentro de `AppServices`, nunca exposto

**Verification:**
- [x] Build: `dotnet build` — 0 avisos, 0 erros
- [x] Manual check: `dotnet run` abre e fica rodando sem exceção; `pdv.db` criado com a migration completa aplicada (confirmado via WAL de ~346KB)

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/AppServices.cs`
- `SistemaPDV/App.axaml.cs`

**Estimated scope:** S (2 arquivos)

---

## Task 39: Tema visual (design tokens do protótipo)

**Description:** `ResourceDictionary` (`Themes/PdvTheme.axaml`, incluído em `App.axaml`) com os tokens de cor/tipografia extraídos direto do CSS real do protótipo mockado do curso (ver tabela em `specs/SPEC-pdv-ui.md`, seção "Convenções de UI") — tema escuro, fonte Inter (texto) + Fira Code (números/valores). Toda View da Fase 5 usa esses `StaticResource`, nunca hex cravado ou o tema Fluent padrão do Avalonia sem ajuste.

**Acceptance criteria:**
- [x] `SolidColorBrush` nomeados pra cada token da tabela (`BgMainBrush`, `BgCardBrush`, `AccentGreenBrush`, `AccentRedBrush`, `BrandYellowBrush`, etc.) com os valores hex exatos do protótipo
- [x] `FontFamily` resources pra `Inter` (texto, já embutida via `Avalonia.Fonts.Inter`) — `Fira Code` substituída por `monospace` genérico: não existe pacote confiável do Fira Code pro Avalonia, e `monospace` é literalmente o fallback que o próprio CSS do protótipo já declara pro token `font-mono` (decisão registrada no comentário do arquivo e em `docs/APRENDIZADOS.md`)
- [x] `App.axaml` aplica o `ResourceDictionary` globalmente (`Application.Resources` → `MergedDictionaries`) — qualquer View novo já nasce com acesso aos tokens sem import extra

**Verification:**
- [x] Build: `dotnet build` — 0 avisos, 0 erros
- [x] Manual check: `dotnet run` abre e roda sem exceção de binding (`StaticResource` resolveram) — **confirmação visual da cor exata fica pendente pro usuário conferir**, o ambiente onde rodo não tira screenshot da janela

**Dependencies:** None

**Files likely touched:**
- `SistemaPDV/Themes/PdvTheme.axaml`
- `SistemaPDV/App.axaml`
- `SistemaPDV/SistemaPDV.csproj` (fonte Fira Code, se não vier embutida)

**Estimated scope:** S (2-3 arquivos, sem lógica)

---

### Checkpoint: Foundation (pdv-ui)
- [ ] `dotnet build` sem erros
- [ ] `dotnet test` verde

## Task 40: LoginViewModel / LoginView

**Description:** Tela de login: um campo (`pdv_key` digitada), chama `LoginOperadorService.AutenticarAsync`. Sucesso guarda o `Funcionario` autenticado e navega adiante (config se necessário, senão caixa/dashboard); falha mostra mensagem sem detalhar o motivo (não revela se a chave existe ou não, por segurança).

**Acceptance criteria:**
- [x] Comando de login só habilitado com o campo preenchido
- [x] Sucesso e falha refletidos no ViewModel sem travar a UI (chamada assíncrona)
- [x] Funcionário desativado (`Desativado = true`) não loga — mesma checagem que `LoginOperadorService` já faz (coberto indiretamente: ViewModel só repassa o `null` que o serviço já devolve pra esse caso)

**Verification:**
- [x] Tests pass: `dotnet test --filter LoginViewModel` — 5 testes novos, 113 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros

**Dependencies:** Task 38

**Files likely touched:**
- `SistemaPDV/ViewModels/LoginViewModel.cs`
- `SistemaPDV/Views/LoginView.axaml` (+ `.cs`)
- `SistemaPDV.Tests/LoginViewModelTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 41: ConfiguracoesViewModel / ConfiguracoesView

**Description:** Provisionamento do dispositivo (link de cadastro → `SoftcomAuthService.ObterClienteSecretAsync` → grava `ApiClienteSecretProtegido`) e edição dos campos de `ConfiguracaoSincronizacao` (`UrlApi`, `NomeDispositivo`, `ClienteConsumidorFinalIdExterno`, `ExigirAberturaCaixa`). Acessível quando a configuração ainda não existe (fluxo inicial) e depois pelo menu.

**Acceptance criteria:**
- [x] Formulário de vínculo grava `client_secret` protegido (nunca em texto puro na tela nem no banco) — confirmado em teste comparando o valor salvo com o original via `Desproteger`
- [x] Toggle `ExigirAberturaCaixa` persiste e é lido de volta corretamente
- [ ] Sem `ConfiguracaoSincronizacao` preenchida, o app direciona pra essa tela antes de qualquer outra — **adiado pra Task 42**: essa é uma decisão de navegação do `ShellViewModel`, que ainda não existe; `ConfiguracoesViewModel`/`View` já estão prontos pra serem exibidos quando o Shell decidir

**Verification:**
- [x] Tests pass: `dotnet test --filter Configuracoes` — 9 testes novos (5 `ConfiguracaoServiceTests` + 4 `ConfiguracoesViewModelTests`), 122 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros

**Dependencies:** Task 37, Task 38

**Files likely touched:**
- `SistemaPDV/ViewModels/ConfiguracoesViewModel.cs`
- `SistemaPDV/Views/ConfiguracoesView.axaml` (+ `.cs`)
- `SistemaPDV.Tests/ConfiguracoesViewModelTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 42: ShellViewModel / ShellView

**Description:** Casca de navegação: troca entre as telas (Login/Configurações/AbrirCaixa/Dashboard/Pdv/ListaPedidos/Cadastros), mostra operador+caixa logado, indicador online/offline e contador de pendências (outbox). É quem decide, com base em `ExigirAberturaCaixa` e no estado local, se navega pra `AbrirCaixaView` ou direto pro Dashboard depois do login.

**Acceptance criteria:**
- [x] Navegação entre as telas funciona sem recriar estado perdido — a barra Painel/Nova Venda/Pedidos (Task 47) fica **bloqueada enquanto há venda em andamento** (carrinho ou pagamento já digitado), decisão do usuário (2026-09-20): cada `IrPara*` cria um ViewModel novo, então navegar descartaria o carrinho; agora o operador precisa finalizar ou cancelar (Esc) antes de sair. `PdvViewModel.TemVendaEmAndamento` → `ShellViewModel.VendaEmAndamento` → `CanExecute` dos 3 comandos
- [x] Sem `ConfiguracaoSincronizacao.UrlApi` preenchida (dispositivo nunca vinculado), o Shell abre direto em `ConfiguracoesView`, antes até da `LoginView` — critério que ficou pendente da Task 41
- [x] Header mostra operador + caixa (quando aberto) — igual ao protótipo mockado
- [ ] Indicador de pendências e de conexão existem (ligados de verdade só na Task 50)

**Verification:**
- [x] Tests pass: `dotnet test --filter Shell` — 9 testes novos (4 `CaixaServiceObterCaixaAbertoTests` + 5 `ShellViewModelTests`), 131 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros
- [x] Manual check: `dotnet run` abre e fica rodando sem exceção, direcionando pra `ConfiguracoesView` (banco novo, sem config ainda)

**Dependencies:** Task 40, Task 41

**Files likely touched:**
- `SistemaPDV/ViewModels/ShellViewModel.cs`
- `SistemaPDV/Views/ShellView.axaml` (+ `.cs`)
- `SistemaPDV/App.axaml.cs` (troca `MainWindow.DataContext` pro `ShellViewModel`)
- `SistemaPDV.Tests/ShellViewModelTests.cs`

**Estimated scope:** M (4 arquivos)

---

## Task 43: AbrirCaixaViewModel / AbrirCaixaView

**Description:** Tela de abertura de caixa: troco inicial, chama `CaixaService.AbrirCaixaLocalAsync`. Navegável direto (menu) quando `ExigirAberturaCaixa = false`, ou obrigatória pós-login quando `true` e não há caixa aberto hoje.

**Acceptance criteria:**
- [x] `ExigirAberturaCaixa = true` sem caixa aberto → Shell força essa tela antes de liberar Dashboard/PDV
- [x] `ExigirAberturaCaixa = false` → Shell libera Dashboard direto — a parte de "vira opcional no menu" fica **pendente**: a barra de navegação (Task 47) só cobre Painel/Nova Venda/Pedidos e só fica habilitada COM caixa aberto; ainda não existe um jeito de abrir caixa depois, a partir do menu, quando `ExigirAberturaCaixa = false` e o operador entrou sem caixa
- [x] Sucesso navega pro Dashboard (placeholder até Task 46); falha (ex: já existe caixa aberto pra hoje) mostra a mensagem de `ResultadoOperacaoCaixa`

**Verification:**
- [x] Tests pass: `dotnet test --filter AbrirCaixa` — 3 testes de `AbrirCaixaViewModel` + 1 teste de ponta a ponta em `ShellViewModelTests`, 135 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros
- [x] Manual check: `dotnet run` roda sem exceção

**Dependencies:** Task 37, Task 38, Task 42

**Files likely touched:**
- `SistemaPDV/ViewModels/AbrirCaixaViewModel.cs`
- `SistemaPDV/Views/AbrirCaixaView.axaml` (+ `.cs`)
- `SistemaPDV.Tests/AbrirCaixaViewModelTests.cs`

**Estimated scope:** M (3 arquivos)

---

### Checkpoint: Login → configuração → abrir caixa navegável
- [x] `dotnet test` verde — 135 testes
- [x] Manual check: `dotnet run`, fluxo login→config→caixa navegável quando `ExigirAberturaCaixa = true` (testado via `ShellViewModelTests`); o estado `false` (pula direto pro Dashboard) também coberto em teste, mas o Dashboard em si ainda é placeholder até a Task 46

## Task 44: PdvViewModel — venda

**Description:** O núcleo do módulo: carrinho local (itens + quantidade + preço), seleção de cliente (default Consumidor Final), seleção de forma(s) de pagamento (suporta pagamento misto, já suportado por `VendaService`), `PodeFinalizarVenda` (só habilitado com caixa aberto e ao menos um item), atalhos de teclado (F2 novo, F4 buscar produto/cliente, F10 pagar, Esc cancelar). Finalizar chama `VendaService.RegistrarVendaLocalAsync` — nunca `VendaSyncService` diretamente (isso é papel do background service, Task 50).

**Acceptance criteria:**
- [x] `PodeFinalizarVenda` reflete caixa aberto (garantido por construção — quem cria o ViewModel já tem um `caixaId` de um caixa aberto) + carrinho não vazio, reativo (ReactiveUI, via `RaisePropertyChanged` manual no `CollectionChanged` de `Itens`)
- [x] Cliente não selecionado não trava a venda — fica implícito Consumidor Final (resolvido só na hora de sincronizar, não aqui)
- [x] Cliente pode ser selecionado mesmo sem `IdExterno` ainda (recém-criado na Task 49) — testado (`ClienteSemIdExternoPodeSerSelecionadoESalvoNaVenda`)
- [x] Produto e forma de pagamento continuam exigindo `IdExterno != null` pra aparecer selecionável — filtrado em `CatalogoLocalService` (novo), não no ViewModel
- [x] Finalizar venda não faz nenhuma chamada de rede — grava local e devolve na hora (`VendaService`, sem dependência de rede)

**Verification:**
- [x] Tests pass: `dotnet test --filter PdvViewModel` — 6 testes de `PdvViewModel` + 3 de `CatalogoLocalService`, 144 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros

**Dependencies:** Task 38, Task 42

**Files likely touched:**
- `SistemaPDV/Services/CatalogoLocalService.cs` (novo — faltava leitura de produtos/clientes/formas de pagamento já sincronizados)
- `SistemaPDV/ViewModels/PdvViewModel.cs`, `ItemCarrinho.cs`, `PagamentoAlocado.cs`
- `SistemaPDV.Tests/PdvViewModelTests.cs`, `CatalogoLocalServiceTests.cs`

**Estimated scope:** M (2 arquivos, lógica densa) — na prática L (7 arquivos): `CatalogoLocalService` não estava previsto

---

## Task 45: PdvView.axaml

**Description:** Layout da tela de venda e ligação dos atalhos de teclado do protótipo (F2/F4/F10/Esc) aos comandos do `PdvViewModel`.

**Acceptance criteria:**
- [x] F2 novo, F4 buscar, F10 pagar, Esc cancelar ligados de verdade (`KeyBinding` pra F2/F10/Esc; F4 via exceção pontual em code-behind — foco de UI puro, sem `Command` declarativo equivalente no Avalonia)
- [x] Indicador de `SyncStatus` visível pro cliente selecionado (🟢/🟡/🔴) — `SyncStatusIndicator` (novo, `UserControl` reaproveitável, já prometido em `specs/SPEC-pdv-ui.md`); indicador nos itens do carrinho não fez sentido ainda (produto só entra no carrinho já sincronizado, por `CatalogoLocalService`)

**Verification:**
- [x] Build: `dotnet build` — 0 avisos, 0 erros
- [x] Manual check: `dotnet run` roda sem exceção (XAML carrega) — `PdvView` **agora é alcançável de verdade** (login → abrir caixa → Dashboard → "Nova Venda (F2)"), resolvido na Task 46; teste manual dos 4 atalhos em uso real ainda fica a cargo do usuário rodar (não tenho como capturar tecla numa sessão headless)

**Dependencies:** Task 44

**Files likely touched:**
- `SistemaPDV/Views/PdvView.axaml` (+ `.cs`)
- `SistemaPDV/Views/Controls/SyncStatusIndicator.axaml` (+ `.cs`), `SistemaPDV/Converters/SyncStatusConverters.cs` — não previstos, mas prometidos na spec

**Estimated scope:** S (1-2 arquivos, sem lógica nova) — na prática M (5 arquivos): `SyncStatusIndicator` reaproveitável não estava contado aqui

---

### Checkpoint: Vender offline funciona ponta a ponta
- [x] Fluxo completo agora existe no código: login → abrir caixa → Dashboard → Nova Venda → Pedidos (venda aparece com 🟡 Pendente) — coberto por testes de ponta a ponta (`PdvViewModelTests`, `ShellViewModelTests`, `ListaPedidosViewModelTests`)
- [ ] Manual check em uso real (vender de verdade, com a rede desligada, e ver a venda na lista) — fica a cargo do usuário rodar: não tenho como digitar/clicar numa sessão headless

## Task 46: DashboardViewModel / DashboardView

**Description:** Faturamento do dia (soma de vendas do caixa aberto), contagem de estoque local, tamanho da fila outbox (caixa+venda+cliente pendentes), última sincronização — todas consultas de leitura simples sobre o banco local, sem chamada de rede própria.

**Acceptance criteria:**
- [x] Números batem com o estado real do banco local — testado (`DashboardServiceTests`, incl. filtro por caixa e caso sem caixa aberto)
- [x] Não dispara sincronização nenhuma sozinho (isso é só `SincronizacaoBackgroundService`, Task 50) — `DashboardService` só lê, nunca chama `SoftcomApiClient`/serviço de sync nenhum

**Verification:**
- [x] Tests pass: `dotnet test --filter Dashboard` — 3 testes de `DashboardService` + 4 de `DashboardViewModel`, 153 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros
- [x] Manual check: `dotnet run` roda sem exceção

**Dependencies:** Task 38, Task 42

**Files likely touched:**
- `SistemaPDV/Services/DashboardService.cs`, `ResumoDashboard.cs` (novos — não previstos, mas necessários pra leitura sem tocar `Func<AppDbContext>` direto)
- `SistemaPDV/ViewModels/DashboardViewModel.cs`
- `SistemaPDV/Views/DashboardView.axaml` (+ `.cs`)
- `SistemaPDV/ViewModels/ShellViewModel.cs` (navegação real pro Dashboard + Dashboard→Pdv via `NovaVendaCommand`; corrigido também um bug de ordem de atribuição — `TelaAtual` mudava antes de `CurrentViewModel`, criando uma corrida)
- `SistemaPDV.Tests/DashboardServiceTests.cs`, `DashboardViewModelTests.cs`

**Estimated scope:** S (3 arquivos, sem lógica densa) — na prática M (8 arquivos)

---

### Revisão de código pós-Task 46 (2026-09-18)

Skill `code-review-and-quality` (nível `high`) rodada sobre o fluxo completo "venda ponta a ponta" (Tasks 37-46) — 153 testes verdes na hora, mas a revisão achou 3 problemas reais que nenhum teste cobria (ver `docs/APRENDIZADOS.md` #37 pro "porquê" de cada um), todos corrigidos na hora:

- [x] `PdvViewModel.AdicionarPagamento` criava pagamento de R$ 0,00 silencioso com valor inválido/vazio — agora rejeita a ação e mostra mensagem
- [x] `PdvViewModel.PodeFinalizarVenda` não validava pagamento contra o total — agora exige soma dos pagamentos ≥ total (pagar a mais, com troco, continua permitido)
- [x] `ShellViewModel`: chamadas fire-and-forget (`_ = viewModel.IniciarAsync()`, `_ = AposLoginAsync(...)`) sem tratamento de exceção podiam travar a tela em silêncio — agora envolvidas em `ExecutarComTratamentoDeErroAsync`, que captura e expõe via `Mensagem` (mostrada na `ShellView` como um banner vermelho)

Testes: 153 → 159. Build limpo, 0 avisos.

---

## Task 47: ListaPedidosViewModel / ListaPedidosView

**Description:** Lista as vendas do caixa atual (ou período, a definir na implementação) com número, data/hora, cliente, operador, total, forma de pagamento e `SyncStatus`.

**Acceptance criteria:**
- [x] Reflete o `SyncStatus` real de cada venda com indicador visual — via `SyncStatusIndicator` (reaproveitado da Task 45)
- [ ] Atualiza sozinha quando uma venda muda de status (ex: depois que o background service sincroniza) — via binding reativo, sem precisar de F5 manual — **adiado pra Task 50**, decisão confirmada com o usuário (2026-09-18): depende de algo rodando em background que ainda não existe; por ora `AtualizarCommand` (botão "🔄 Atualizar") recarrega manualmente

**Verification:**
- [x] Tests pass: `dotnet test --filter ListaPedidos` — 2 testes de `ListaPedidosViewModel` + 4 de `VendaLocalService` + 2 novos de navegação em `ShellViewModelTests`, 167 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros
- [x] Manual check: `dotnet run` roda sem exceção

**Dependencies:** Task 38, Task 42

**Files likely touched:**
- `SistemaPDV/Services/Sales/VendaLocalService.cs`, `VendaResumo.cs` (novos — leitura com cliente/operador/formas resolvidos em lote, sem N+1)
- `SistemaPDV/ViewModels/ListaPedidosViewModel.cs`, `Tela.cs`
- `SistemaPDV/Views/ListaPedidosView.axaml` (+ `.cs`)
- `SistemaPDV/ViewModels/ShellViewModel.cs`, `Views/ShellView.axaml` — **barra de navegação persistente** (Painel / Nova Venda / Pedidos), habilitada só com caixa aberto: resolve a pendência antiga das Tasks 42/43/45 ("não tem como voltar depois de sair do Dashboard"); Cadastros entra na Task 49
- `SistemaPDV.Tests/ListaPedidosViewModelTests.cs`, `VendaLocalServiceTests.cs`, `ShellViewModelTests.cs`, `DashboardViewModelTests.cs` (correção de teste flaky pré-existente)

**Estimated scope:** M (3 arquivos) — na prática L (12 arquivos)

---

## Task 48: CatalogSyncService.SincronizarClienteNovoAsync

**Description:** Nova ponta de outbox, simétrica à de `Caixa`/`Venda`: busca clientes locais com `IdExterno == null && SyncStatus == PendenteSync`, envia via `POST {dominio}/softauth/api/v2/clientes/clientes` (`apiClient.EnviarAsync`), grava o `IdExterno` retornado em sucesso. Reaproveita `OutboxHelper.MarcarFalhaAsync`, `SoftcomJson`, `ErroApiExtractor`.

**Acceptance criteria:**
- [x] `200` → grava `IdExterno`, `SyncStatus = Sincronizado` (id validado: `> 0`, resposta não-JSON/sem id vira falha, nunca exceção)
- [x] `409`/`422`/erro de rede → `SyncStatus = FalhaSync` + `UltimoErroSync` (via `ErroApiExtractor`), cliente continua elegível pra nova tentativa — **`409` deliberadamente NÃO vira `Sincronizado`** (diferente de venda/caixa): a resposta não traz o id do cliente que já existe, então marcar sincronizado deixaria `IdExterno` nulo e travaria toda venda pra esse cliente
- [x] Payload mapeia `Pessoa`→`pessoa`, `CpfCnpj`→`cpf_cnpj` (só dígitos), defaults da spec (`contribuinte_icms=9`, `indicador_finalidade=0`) — `pessoa` é derivada do documento validado (14 dígitos = JURIDICA), não do campo local; `razao_social` cai no `Nome` quando JURIDICA sem razão social (obrigatória na API)
- [x] Um cliente com erro não impede outros (`SincronizarClientesNovosPendentesAsync`, lote com try/catch por cliente)
- [x] **Segurança (skill `security-and-hardening`)**: só sai por HTTPS ou loopback; documento validado localmente (dígitos verificadores) antes de sair e nunca repetido no erro gravado; payload é uma allowlist (`ClienteNovoRequestDto`); corpo de erro da API truncado (`ErroApiExtractor.TamanhoMaximo = 300`) — endurecimento que vale também pra caixa/venda

**Verification:**
- [x] Tests pass: `dotnet test --filter SincronizarClienteNovo` — 18 testes novos (`CatalogSyncServiceClienteNovoTests`) + 4 de `ErroApiExtractor` + 14 de `DocumentoValidator`; 205 no total
- [x] Build: `dotnet build` — 0 avisos, 0 erros

**Dependencies:** None (extensão do `CatalogSyncService` já existente)

**Files likely touched:**
- `SistemaPDV/Services/Sync/CatalogSyncService.cs` — `SincronizarClienteNovoAsync(clienteId, token)` (um) + `SincronizarClientesNovosPendentesAsync(token)` (lote, é o que a Task 50 chama)
- `SistemaPDV/Services/Sync/Dtos/ClienteNovoApiDto.cs` (novo — request/response de escrita)
- `SistemaPDV/Services/DocumentoValidator.cs` (novo — CPF/CNPJ), `ErroApiExtractor.cs` (endurecido)
- `SistemaPDV/Models/Cliente.cs` + `ClienteConfiguration.cs` + migration `AddClienteUltimoErroSync` (novo campo `UltimoErroSync`)
- `SistemaPDV.Tests/CatalogSyncServiceClienteNovoTests.cs`, `DocumentoValidatorTests.cs`, `ErroApiExtractorTests.cs`

**Estimated scope:** M (3 arquivos) — na prática L (12 arquivos), por causa do endurecimento de segurança

### Revisão de segurança da Task 48 (skill `security-and-hardening`, 2026-09-20)

Corrigido nesta task: (1) `ErroApiExtractor` lançava exceção se `errors` não fosse array de arrays e devolvia o corpo inteiro (sem limite) no fallback — agora nunca lança e trunca; (2) documento inválido era reenviado a cada ciclo de 30s — agora validado antes de sair; (3) token + CPF poderiam trafegar em `http://` — agora recusado (exceto loopback); (4) falha de cliente era indiagnosticável — `UltimoErroSync`.

**Corrigidos na sequência da revisão (2026-09-20):**
- [x] Resposta `200` com corpo não-JSON (portal cativo) não lança mais: `SoftcomJson.TentarDesserializar<T>` em caixa (abertura), venda e cliente; mensagem gravada passa por `ErroApiExtractor` (truncada).
- [x] HTTPS obrigatório (exceto loopback) em **todos** os endpoints: `ConexaoSegura.Permitida` aplicada em `SoftcomApiClient` (`EnviarAsync` e `BuscarTudoAsync`) e `SoftcomAuthService` (`ObterClienteSecretAsync`, `ObterTokenAsync` — antes de desproteger o secret). Novo `ResultadoEnvioTipo.ConexaoInsegura` tratado explicitamente em caixa (abertura e fechamento), venda e cliente: devolve falha **sem marcar** a entidade nem contar tentativa. Testes: `ConexaoSeguraTests`, `SincronizacaoRespostaHostilTests` (+ testes em ApiClient/AuthService); 233 testes verdes ×3.

**Achados registrados, NÃO corrigidos (fora do escopo da Task 48 — decidir depois):**
- Sem idempotência no push de cliente: a API aceita `api_guid` (nullable), mas `Cliente` não tem um `Guid` gerado na criação (como `Venda.Id`). Se o `POST` chega e a resposta se perde, o reenvio pode duplicar o cliente (ou dar `409` sem id).
- `409` de cliente já existente fica em `FalhaSync` pra sempre: não há reconciliação por CPF/CNPJ quando o catálogo é puxado depois (o cliente da API entraria como uma linha nova, duplicando a local).
- ~~Erro permanente (`422`) é reenviado a cada 30s pra sempre~~ — **corrigido** na Task 50 (espera crescente + teto de 8 tentativas, `PoliticaRetentativa`).

- `pdv.db` guarda CPF/CNPJ em texto puro (só `client_secret`/certificado usam DPAPI) — aceitável no escopo do curso, mas é dado pessoal sem criptografia em repouso.

---

## Task 49: CadastrosViewModel / CadastrosView

**Description:** Lista clientes e produtos já sincronizados (leitura), com busca. Formulário simples de criar cliente (Nome + CPF/CNPJ) — grava local na hora (`SyncStatus = PendenteSync`), sem chamada de rede própria (quem sincroniza é o `SincronizacaoBackgroundService`, Task 50, via Task 48).

**Acceptance criteria:**
- [x] Busca/listagem de clientes e produtos funciona sobre o banco local
- [x] Criar cliente grava local instantaneamente, sem travar a UI esperando rede
- [x] Cliente recém-criado aparece na lista com indicador `🟡 Pendente`, e não some nem duplica quando o `IdExterno` chegar depois

**Verification:**
- [x] Tests pass: `dotnet test --filter Cadastros` (+ `CadastroLocalService`)
- [x] Build: `dotnet build` (0 avisos)

**Dependencies:** Task 38, Task 42, Task 48

**Files likely touched:**
- `SistemaPDV/ViewModels/CadastrosViewModel.cs`
- `SistemaPDV/Views/CadastrosView.axaml` (+ `.cs`)
- `SistemaPDV.Tests/CadastrosViewModelTests.cs`

**Estimated scope:** M (3 arquivos) — na prática 7 (ganhou `CadastroLocalService` + `ResumoCadastro`, e o `ShellViewModel`/`ShellView` ganharam `IrParaCadastrosCommand`).

**Decisões (2026-09-20):** busca manual (Enter/botão) como em `ListaPedidosViewModel`; listas cortadas em 200 com aviso (sem paginação); criar valida nome e CPF/CNPJ (dígitos verificadores + duplicidade) antes de gravar; `Cadastros` segue a mesma regra dos outros botões: só com caixa aberto e sem venda em andamento (decisão do usuário, 2026-09-20). Pendente: conferência visual da tela pelo usuário.

---

## Task 50: SincronizacaoBackgroundService

**Description:** Timer em background com dois ritmos, usando `DispatcherTimer` do Avalonia (decisão do usuário, 2026-09-18 — integrado ao loop de UI, dispara na thread certa sem precisar de `Dispatcher.UIThread.Post` manual): a cada 30s tenta `CaixaSyncService.SincronizarCaixaPendenteAsync`, `VendaSyncService.SincronizarVendasPendentesAsync` e `CatalogSyncService.SincronizarClienteNovoAsync` (outbox); a cada 5min tenta `CatalogSyncService.SincronizarTudoAsync` (catálogo completo). Cada ciclo isolado em try/catch próprio — falha nunca vira exceção não tratada nem crash, só atualiza o indicador de conexão do Shell pra "offline" e tenta de novo no próximo ciclo. Pausado enquanto `ConfiguracaoSincronizacao` não estiver preenchida.

**Acceptance criteria:**
- [x] Usa `DispatcherTimer` (não `System.Threading.Timer`/`Task.Delay` cru) — decisão confirmada com o usuário (2026-09-18)
- [x] Roda os dois ritmos (30s/5min) de forma independente, sem bloquear a UI
- [x] Uma falha de rede num ciclo não derruba o app nem os ciclos seguintes
- [x] Atualiza o indicador online/offline do `ShellViewModel` conforme sucesso/falha do ciclo mais recente
- [x] Não faz nada enquanto não há `ConfiguracaoSincronizacao` preenchida (sem URL/credenciais)

**Verification:**
- [x] Tests pass: `dotnet test --filter SincronizacaoBackground` (21 testes; 289 no total)
- [x] Build: `dotnet build` (0 avisos)
- [ ] Manual check: `dotnet run`, observar o indicador de sync mudando sozinho com a rede ligada/desligada

**Dependencies:** Task 38, Task 42, Task 48

**Files likely touched:**
- `SistemaPDV/Services/Sync/SincronizacaoBackgroundService.cs`
- `SistemaPDV/App.axaml.cs` (inicia/para o timer junto do ciclo de vida do app)
- `SistemaPDV/ViewModels/ShellViewModel.cs` (recebe o indicador)
- `SistemaPDV.Tests/SincronizacaoBackgroundServiceTests.cs`

**Estimated scope:** M (4 arquivos)

**Como ficou (2026-09-20):** dois `DispatcherTimer` (30 s / 5 min) só disparam `Task.Run` de ciclos públicos e testáveis (`ExecutarCicloRapidoAsync` = catálogo inicial se ainda não veio + outbox; `ExecutarCicloOutboxAsync`; `ExecutarCicloCatalogoAsync`). Ciclos nunca sobrepõem (semáforo sem fila), cada etapa do outbox é isolada, outbox só pede token se há pendência, e o estado (`EstadoConexao` Desconhecida/Online/Offline + motivo) vai pro header do Shell via `DefinirConexao`.

**Refinamentos feitos depois (2026-09-20, decididos com o usuário):** (a) vincular com sucesso leva ao Login e dispara a sincronização na hora (`DispositivoVinculado` → `SolicitarAgora`); (b) Pedidos e Cadastros se recarregam sozinhos quando um ciclo mexe no banco (`DadosAlterados` → `NotificarDadosSincronizados` → `IAtualizavelPorSincronizacao`; a tela de venda fica de fora de propósito); (c) espera crescente + teto de retentativas (achado #3 da revisão de segurança) — `PoliticaRetentativa`: 30 s, 1, 2, 4, 8, 10 min (teto da espera), desiste após 8 tentativas até o operador usar "Reenviar falhas"; contadores persistidos (migration `AddRetentativaOutbox`).

**Pendente:** conferência manual do indicador com rede ligada/desligada e do "Reenviar falhas" na tela.

### Correções descobertas ao testar contra a API real (2026-09-20)

O usuário viu "Chave inválida"; investigar levou a descobrir que a sincronização **nunca tinha funcionado** contra a API real. Corrigido (346 testes; o mesmo código sincroniza os 5 recursos numa cópia do banco): rotas sob `softauth/api/v2/` (`SoftcomRotas`), página de formas de pagamento embrulhada em array, `bloqueado`/`vender` booleanos, `estoque` textual, campos nulos da empresa, e login com **bcrypt** (a `pdv_key` da API é hash `$2y$10$`). Ver APRENDIZADOS #53 e #54.

**A conferir/decidir (não verificado):**
- **Venda (2026-09-20):** o POST real devolveu 422 (faltavam `numero_documento`, `cancelada`, `bloqueada`, `produto_empresa_grade_id`). Corrigido com testes; ver APRENDIZADOS #56. **Falta confirmar na 1ª venda real:** (a) ✅ CONFIRMADO em 2026-09-20 (item da venda 333 = código 77) o mapeamento dos ids do produto (grade = `id` da listagem, produto = `produto_id`) — conferir o item da venda no SoftcomShop; (b) ✅ RESOLVIDO (2026-09-20): `numero_documento` é único por empresa (usuário) e haverá mais de um PDV — cada dispositivo tem um "código do PDV" em Configurações e o número enviado vira `<código>-<sequencial de 6 dígitos>` (ver APRENDIZADOS #64); (c) ✅ CONFIRMADO pelo usuário (2026-09-20): uma venda com quantidade fracionada (por peso) foi aceita e sincronizada pela API real, apesar do Swagger dizer inteiro.
- POST de **caixa (abrir/fechar), venda e criar cliente** agora usam `softauth/api/v2/`, por inferência — não foi testado com POST real (criaria dado). Conferir no primeiro caixa/venda de teste.

- Produtos: vieram 200 (uma página cheia) — conferir se há mais páginas a seguir e o total real.

### Confirmado pelo usuário depois da 1ª venda real (2026-09-20)

- ✅ **Ids do produto:** o item da venda 333 no SoftcomShop tem o código **77** (= `produto_id`) — o mapeamento `produto_id` = `Produto.ProdutoIdApi` está certo. (`produto_empresa_grade_id` = `Produto.IdExterno` segue sem contestação: a API aceitou.)
- ⚠️ **`numero_documento` é ÚNICO POR EMPRESA** (não por dispositivo). Hoje `Venda.NumeroPedido` é sequencial **por dispositivo**: dois PDVs na mesma empresa gerariam números repetidos e a segunda venda seria recusada (ou pior, aceita duplicada). **Decidir:** faixa/prefixo por dispositivo, número vindo do servidor, ou reservar um bloco. Não corrigido — só vira problema com mais de um PDV na mesma empresa. Ver "Observações" abaixo.
- ✅ **Quantidade fracionada existe** (venda por peso). O corpo já envia `quantidade` como decimal (`1.0`, `0.5`), mas o Swagger diz `integer($int32)`: **✅ PROVADO (2026-09-20, usuário): venda fracionada aceita pela API real** (Swagger está desatualizado). A tela de venda aceita fração (até 3 casas, `0,5`) e há teste. Pendente só conferir no SoftcomShop se o estoque baixa fracionado.

### Observações da revisão pré-push (2026-09-20) — para rever mais à frente (NADA mudou por causa delas)

Achados de baixo risco deixados de propósito, registrados para a próxima passada:

1. **Login bcrypt sem limite de tentativas.** Confere a chave contra cada funcionário ativo com hash (custo 10 ≈ dezenas de ms cada; ~1 s no pior caso com 14 operadores; roda fora da thread de UI). Não há bloqueio/atraso após erros repetidos — o bcrypt lento já dificulta força bruta, mas um PDV exposto poderia ganhar um atraso progressivo. Se o nº de operadores crescer (dezenas), medir o tempo de login.
2. **`NumeroPedido` = MAX + 1.** Seguro hoje (o botão Finalizar não deixa duas vendas simultâneas no mesmo PDV; o índice único impediria duplicar) — só colide com **dois processos no mesmo banco**. Se colidir, a `DbUpdateException` derruba o registro da venda; considerar 1 nova tentativa. (A unicidade por empresa foi resolvida com o código do PDV — item 10.)
3. **`CatalogSyncService` com ~600 linhas** e duas responsabilidades (catálogo + envio de cliente novo). Longe do limite (~1000), mas é o candidato a dividir (ex: `ClienteSyncService`) se crescer.
4. **Mensagens de erro de `SoftcomAuthService` (Configurações) mostram o corpo bruto da resposta**, sem limite de tamanho. Só aparece na tela local (não vai pro banco); truncar com `ErroApiExtractor` se um dia mostrar HTML de portal cativo.
5. ~~Sem log~~ — **feito (2026-09-20).** Log em arquivo `%LOCALAPPDATA%\SistemaPDV\logs\pdv-AAAAMMDD.log` (`LogArquivo` + `Registro`): exceções de comando, ciclos e etapas de sincronização, falhas de envio que a API recusou (`OutboxHelper`), mudanças de estado da conexão (só quando muda), descartes de venda (auditoria) e início/fim do app. Nunca lança; mascara token, client_secret, pdv_key, hash bcrypt e CPF/CNPJ; limite de 5 MB por dia e 14 dias de retenção. **Limite conhecido:** a máscara é por padrão (regex) — um dado pessoal em formato inesperado (ex: nome do cliente numa mensagem da API) não é reconhecido; por isso o log não grava corpo de requisição, só mensagens de erro.
6. **`catalogoSincronizado` e `MensagemUltimoCiclo`** (`SincronizacaoBackgroundService`) são lidos/escritos de threads diferentes sem `volatile`/lock — benigno (bool e referência), mas vale um `volatile` se o serviço ganhar mais estado.

7. ~~Fechamento de caixa com vendas ainda pendentes~~ — **decidido pelo usuário (2026-09-20): o fechamento SÓ pode acontecer sem venda pendente.** Implementado em 3 camadas: `CaixaService.FecharCaixaLocalAsync` recusa; a tela avisa, bloqueia o botão e se atualiza quando a sincronização termina ('Verificar de novo'); o envio do fechamento espera com motivo visível. **Efeito colateral a resolver depois:** uma venda com erro PERMANENTE (ex: a API sempre recusa) trava o fechamento do caixa para sempre — hoje a saída é "Reenviar falhas" e corrigir a causa; **RESOLVIDO (2026-09-20): "Descartar venda com auditoria"** — ver item 9.
9. ~~Descartar venda com auditoria~~ — **feito (2026-09-20), com reconferência no envio (revisão de código, APRENDIZADOS #67).** Decisões do usuário: só **supervisor**, digitando a `pdv_key` dele; a venda descartada **não entra no "esperado"** do fechamento (tratada como cancelada); só vendas **em falha** podem ser descartadas (interpretado como `SyncStatus.FalhaSync` — inclui as que desistiram após 8 tentativas; as `PendenteSync` ainda serão enviadas). Novo status `Descartada` (não enviada, não conta como pendente, fora do faturamento); auditoria em `Venda` (`DescartadaEm`, `DescartadaPorId`, `SolicitadaPorId`, `MotivoDescarte`); a venda continua na lista como "⚫ Descartada" com quem/por quê. `VendaFiltros` concentra os dois filtros usados por caixa, dashboard e envio. Tela de Pedidos ganhou o painel de descarte (motivo + chave, campo de senha limpo a cada tentativa). **Limitação:** a API não fica sabendo do descarte (a venda nunca chegou lá), e o número (`numero_documento`) descartado não é reaproveitado.
10. ~~`numero_documento` único por empresa com vários PDVs~~ — **feito (2026-09-20).** Escolhas do usuário: prefixo por dispositivo; a tela continua mostrando o `#N` local. Campo "Código deste PDV" (até 6 letras/dígitos, sem hífen) em Configurações → número enviado `02-000045` (`NumeroDocumento`). Sem código, segue o número puro (uma empresa com um PDV só não muda nada). **Limitações:** o app não consegue saber se outro PDV usa o mesmo código (é responsabilidade de quem configura — o texto da tela avisa); mudar o código depois só vale para as vendas ainda não enviadas.
11. ~~Catálogo com mais de 200 produtos (paginação)~~ — **conferido (2026-09-20).** A página é de 200 (`per_page=200`) e o `pdv.db` já tem **201 produtos** sincronizados da API real, ou seja, a segunda página foi seguida (`next_page_url`). Conferido numa cópia do banco, sem chamar a API. Reforço: `BuscarTudoAsync` agora recusa uma `next_page_url` que repete página já lida (evita laço infinito) e há teste de 5 páginas encadeadas.
12. ~~Acesso às Configurações depois do vínculo~~ — **feito (2026-09-20); a exigência de chave de supervisor está DESLIGADA por decisão do usuário (ver item 13).** Faltava o caminho: a tela só abria no 1º uso (sem vínculo), então o "Código deste PDV", "Exigir abertura de caixa" e o Consumidor Final ficavam inalcançáveis. Botão **⚙ Configurações** na barra (qualquer tela logada, sem exigir caixa aberto, bloqueado só com venda em andamento) e **só supervisor** (chave pedida na própria tela; formulário escondido até liberar; salvar/vincular bloqueados também no ViewModel). Re-vincular pelo menu derruba login e caixa. Revisão de código: re-vincular a OUTRA empresa (CNPJ diferente) com caixas/vendas locais agora é recusado (misturaria os dados); mesma empresa ou primeira vinculação seguem livres. Ver APRENDIZADOS #68.
13. **Religar a exigência de chave de supervisor (descartar venda e Configurações) — AGUARDANDO O USUÁRIO.** Decisão (2026-09-20): os dois pontos ficam **abertos** porque ainda não se sabe como obter a chave de supervisor do SoftcomShop; quando souber, o usuário pede para implementar de novo. Está tudo implementado e testado, só desligado: trocar `PoliticaSupervisor.ExigirChave` para `true` (`Services/PoliticaSupervisor.cs`) e, opcionalmente, devolver o tooltip do botão "⚙ Configurações". Enquanto desligado: o descarte grava só quem pediu (`DescartadaPorId` vazio) e as Configurações abrem direto pelo botão. Ver APRENDIZADOS #69.
8. **Fechar caixa sem apuração de bandeiras de cartão — ADIADO por decisão do usuário (2026-09-20, ver `specs/CAPABILITY-MAP.md`: só o valor total por forma de pagamento por enquanto).** (`digitacao_bandeiras` vai vazia) e o 1º envio real do fechamento já foi aceito pela API (caixa 28 com `data_fechamento`), mas o POST de fechar ainda pode pedir campos que só aparecem em outro cenário (ex: apuração de cartão).

---

## Task 51: FecharCaixaViewModel / FecharCaixaView (lacuna descoberta em 2026-09-20)

**Description:** O critério de sucesso "login → abrir caixa → vender → **fechar caixa**" não tinha tela (só o back-end da Fase 3). Tela de conferência: total vendido, apuração por forma de pagamento (pré-preenchida com o esperado, o operador ajusta) e troco final; fechar só grava local (offline-first) e o outbox envia depois.

**Acceptance criteria:**
- [x] Botão "Fechar caixa" na barra (só com caixa aberto; bloqueado com venda em andamento) leva à tela
- [x] Esperado por forma vem das vendas do caixa; valores aceitam vírgula ou ponto (`ValorMonetario`); inválido desabilita o botão
- [x] Confirmar fecha local (`FecharCaixaLocalAsync`); depois: com `ExigirAberturaCaixa` -> Abrir caixa, senão Dashboard, sem caixa aberto; Cancelar volta ao Dashboard
- [x] Fechamento envia o id da API da forma de pagamento (não o local) e espera com motivo visível se a forma ainda não tem id
- [x] Outbox envia abrir -> vendas -> fechar (o fechamento resume o caixa)
- [x] Corrigido junto: leitura de valores com vírgula (`10,50` era lido como 1050)

**Verification:** [x] 440 testes; build 0 avisos. [ ] Conferência manual da tela + fechamento real contra a API (fecha o caixa de verdade no SoftcomShop — só com autorização).

**Fica de fora do v1:** apuração de bandeiras de cartão (`digitacao_bandeiras`, envia vazia); campos que a API real ainda pedir no POST de fechar (só aparecem no 1º envio real).

---

### Checkpoint: pdv-ui completo
- [x] Todos os Success Criteria de `specs/SPEC-pdv-ui.md` atendidos
- [x] Revisão com o usuário (2026-09-20: atalhos de teclado e venda offline conferidos manualmente; fluxo real contra a API — vincular, sincronizar, logar, abrir caixa, vender, fechar — validado com venda 333/334 e caixa 28)

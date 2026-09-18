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
- [ ] Navegação entre as telas funciona sem recriar estado perdido — **não aplicável ainda**: não existe menu de navegação livre nessa task (só o fluxo linear login→config→[AbrirCaixa/Dashboard]); revisitar quando `CadastrosView`/`DashboardView` (com menu de verdade) existirem
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
- [x] `ExigirAberturaCaixa = false` → Shell libera Dashboard direto — a parte de "vira opcional no menu" fica **pendente**: ainda não existe menu de navegação livre no Shell (mesma pendência já registrada na Task 42)
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
- [x] Manual check: `dotnet run` roda sem exceção (XAML carrega) — **teste manual dos 4 atalhos em uso real fica pendente**: `PdvView` ainda não é alcançável navegando pelo app (Shell só chega no placeholder de Dashboard, sem menu/botão "nova venda" ainda) — revisitar na Task 46

**Dependencies:** Task 44

**Files likely touched:**
- `SistemaPDV/Views/PdvView.axaml` (+ `.cs`)
- `SistemaPDV/Views/Controls/SyncStatusIndicator.axaml` (+ `.cs`), `SistemaPDV/Converters/SyncStatusConverters.cs` — não previstos, mas prometidos na spec

**Estimated scope:** S (1-2 arquivos, sem lógica nova) — na prática M (5 arquivos): `SyncStatusIndicator` reaproveitável não estava contado aqui

---

### Checkpoint: Vender offline funciona ponta a ponta
- [ ] Manual check: desligar a rede, vender, venda aparece `PendenteSync` na lista de pedidos — **pendente**: precisa de navegação real até `PdvView` (Task 46, quando existir "nova venda" a partir do Dashboard) e de `ListaPedidosView` (Task 47) pra ver o resultado; a lógica em si já está coberta por testes de ponta a ponta (`PdvViewModelTests`)

## Task 46: DashboardViewModel / DashboardView

**Description:** Faturamento do dia (soma de vendas do caixa aberto), contagem de estoque local, tamanho da fila outbox (caixa+venda+cliente pendentes), última sincronização — todas consultas de leitura simples sobre o banco local, sem chamada de rede própria.

**Acceptance criteria:**
- [ ] Números batem com o estado real do banco local (verificável manualmente)
- [ ] Não dispara sincronização nenhuma sozinho (isso é só `SincronizacaoBackgroundService`, Task 50)

**Verification:**
- [ ] Tests pass: `dotnet test --filter Dashboard`
- [ ] Build: `dotnet build`

**Dependencies:** Task 38, Task 42

**Files likely touched:**
- `SistemaPDV/ViewModels/DashboardViewModel.cs`
- `SistemaPDV/Views/DashboardView.axaml` (+ `.cs`)
- `SistemaPDV.Tests/DashboardViewModelTests.cs`

**Estimated scope:** S (3 arquivos, sem lógica densa)

---

## Task 47: ListaPedidosViewModel / ListaPedidosView

**Description:** Lista as vendas do caixa atual (ou período, a definir na implementação) com número, data/hora, cliente, operador, total, forma de pagamento e `SyncStatus`.

**Acceptance criteria:**
- [ ] Reflete o `SyncStatus` real de cada venda com indicador visual
- [ ] Atualiza sozinha quando uma venda muda de status (ex: depois que o background service sincroniza) — via binding reativo, sem precisar de F5 manual

**Verification:**
- [ ] Tests pass: `dotnet test --filter ListaPedidos`
- [ ] Build: `dotnet build`

**Dependencies:** Task 38, Task 42

**Files likely touched:**
- `SistemaPDV/ViewModels/ListaPedidosViewModel.cs`
- `SistemaPDV/Views/ListaPedidosView.axaml` (+ `.cs`)
- `SistemaPDV.Tests/ListaPedidosViewModelTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 48: CatalogSyncService.SincronizarClienteNovoAsync

**Description:** Nova ponta de outbox, simétrica à de `Caixa`/`Venda`: busca clientes locais com `IdExterno == null && SyncStatus == PendenteSync`, envia via `POST {dominio}/softauth/api/v2/clientes/clientes` (`apiClient.EnviarAsync`), grava o `IdExterno` retornado em sucesso. Reaproveita `OutboxHelper.MarcarFalhaAsync`, `SoftcomJson`, `ErroApiExtractor`.

**Acceptance criteria:**
- [ ] `200` → grava `IdExterno`, `SyncStatus = Sincronizado`
- [ ] `409`/`422`/erro de rede → `SyncStatus = FalhaSync` (via `ErroApiExtractor`), cliente continua elegível pra nova tentativa
- [ ] Payload mapeia `Pessoa`→`pessoa` (`"FISICA"`/`"JURIDICA"`), `CpfCnpj`→`cpf_cnpj`, e usa os defaults decididos na spec (`contribuinte_icms=9`, `indicador_finalidade=0`) pros campos obrigatórios que o formulário simples não pergunta
- [ ] Um cliente com erro não impede outros de serem tentados (mesmo princípio já usado em `catalog-sync`/`sales`)

**Verification:**
- [ ] Tests pass: `dotnet test --filter SincronizarClienteNovo`
- [ ] Build: `dotnet build`

**Dependencies:** None (extensão do `CatalogSyncService` já existente)

**Files likely touched:**
- `SistemaPDV/Services/Sync/CatalogSyncService.cs`
- `SistemaPDV/Services/Sync/Dtos/ClienteApiDto.cs` (novo DTO de request, se o formato de escrita divergir do de leitura)
- `SistemaPDV.Tests/CatalogSyncServiceClienteNovoTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 49: CadastrosViewModel / CadastrosView

**Description:** Lista clientes e produtos já sincronizados (leitura), com busca. Formulário simples de criar cliente (Nome + CPF/CNPJ) — grava local na hora (`SyncStatus = PendenteSync`), sem chamada de rede própria (quem sincroniza é o `SincronizacaoBackgroundService`, Task 50, via Task 48).

**Acceptance criteria:**
- [ ] Busca/listagem de clientes e produtos funciona sobre o banco local
- [ ] Criar cliente grava local instantaneamente, sem travar a UI esperando rede
- [ ] Cliente recém-criado aparece na lista com indicador `🟡 Pendente`, e não some nem duplica quando o `IdExterno` chegar depois

**Verification:**
- [ ] Tests pass: `dotnet test --filter Cadastros`
- [ ] Build: `dotnet build`

**Dependencies:** Task 38, Task 42, Task 48

**Files likely touched:**
- `SistemaPDV/ViewModels/CadastrosViewModel.cs`
- `SistemaPDV/Views/CadastrosView.axaml` (+ `.cs`)
- `SistemaPDV.Tests/CadastrosViewModelTests.cs`

**Estimated scope:** M (3 arquivos)

---

## Task 50: SincronizacaoBackgroundService

**Description:** Timer em background com dois ritmos, usando `DispatcherTimer` do Avalonia (decisão do usuário, 2026-09-18 — integrado ao loop de UI, dispara na thread certa sem precisar de `Dispatcher.UIThread.Post` manual): a cada 30s tenta `CaixaSyncService.SincronizarCaixaPendenteAsync`, `VendaSyncService.SincronizarVendasPendentesAsync` e `CatalogSyncService.SincronizarClienteNovoAsync` (outbox); a cada 5min tenta `CatalogSyncService.SincronizarTudoAsync` (catálogo completo). Cada ciclo isolado em try/catch próprio — falha nunca vira exceção não tratada nem crash, só atualiza o indicador de conexão do Shell pra "offline" e tenta de novo no próximo ciclo. Pausado enquanto `ConfiguracaoSincronizacao` não estiver preenchida.

**Acceptance criteria:**
- [ ] Usa `DispatcherTimer` (não `System.Threading.Timer`/`Task.Delay` cru) — decisão confirmada com o usuário (2026-09-18)
- [ ] Roda os dois ritmos (30s/5min) de forma independente, sem bloquear a UI
- [ ] Uma falha de rede num ciclo não derruba o app nem os ciclos seguintes
- [ ] Atualiza o indicador online/offline do `ShellViewModel` conforme sucesso/falha do ciclo mais recente
- [ ] Não faz nada enquanto não há `ConfiguracaoSincronizacao` preenchida (sem URL/credenciais)

**Verification:**
- [ ] Tests pass: `dotnet test --filter SincronizacaoBackground`
- [ ] Build: `dotnet build`
- [ ] Manual check: `dotnet run`, observar o indicador de sync mudando sozinho com a rede ligada/desligada

**Dependencies:** Task 38, Task 42, Task 48

**Files likely touched:**
- `SistemaPDV/Services/Sync/SincronizacaoBackgroundService.cs`
- `SistemaPDV/App.axaml.cs` (inicia/para o timer junto do ciclo de vida do app)
- `SistemaPDV/ViewModels/ShellViewModel.cs` (recebe o indicador)
- `SistemaPDV.Tests/SincronizacaoBackgroundServiceTests.cs`

**Estimated scope:** M (4 arquivos)

---

### Checkpoint: pdv-ui completo
- [ ] Todos os Success Criteria de `specs/SPEC-pdv-ui.md` atendidos
- [ ] Revisão com o usuário

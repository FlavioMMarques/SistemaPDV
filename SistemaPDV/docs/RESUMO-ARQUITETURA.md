# Resumo da Arquitetura — SistemaPDV

Mapa de tudo que existe no projeto até agora (4 módulos de back-end completos: `data-layer`, `catalog-sync`, `caixa`, `sales`), classe por classe, pra que serve cada uma. Complementa o `docs/APRENDIZADOS.md` (que registra os *porquês* pontuais) — aqui é o panorama de cima.

## Como os módulos se encaixam

```
data-layer     → banco local (SQLite) e as entidades
    ↓
catalog-sync   → puxa produtos/clientes/formas de pagamento/empresa/funcionários da API
    ↓
caixa          → login do operador + abrir/fechar caixa (offline-first)
    ↓
sales          → registrar e enviar vendas (offline-first)
```

Cada módulo depende só do(s) anterior(es), nunca do posterior — `data-layer` não sabe que `sales` existe, por exemplo. Falta só o `pdv-ui` (as telas), que vai consumir os quatro.

---

## 1. `data-layer` — banco local

### Tipos compartilhados (a base de tudo)

| Tipo | O que é | Pra que serve |
|---|---|---|
| `SyncStatus` | enum | Estado de sincronização de uma entidade: `PendenteSync`, `Sincronizado`, `FalhaSync`. Usado por toda entidade que troca dado com a API. |
| `ISincronizavel<TKey>` | interface genérica | Contrato comum (`Id`, `IdExterno`, `SyncStatus`) pra qualquer entidade sincronizável — genérica no tipo da chave porque a maioria usa `int`, mas `Venda` usa `Guid`. |

### Entidades (uma tabela cada, ou parte de uma via owned type)

| Entidade | Pra que serve |
|---|---|
| `Produto` | Catálogo de produtos — sincronizado da API, usado nas vendas. |
| `Cliente` | Cadastro de clientes — sincronizado da API, inclui o "Consumidor Final" (id 1) pra vendas avulsas. |
| `FormaPagamento` | Formas de pagamento aceitas (PIX, dinheiro, cartão...) — sincronizada da API. |
| `Empresa` | Configuração fiscal/cadastral da empresa dona do PDV — inclui campos sensíveis protegidos (certificado digital). |
| `Funcionario` | Operadores do PDV — inclui `PdvKeyHash` (hash da chave de login local, nunca a chave em claro). |
| `Caixa` | Uma sessão de caixa (abertura → vendas → fechamento) — o coração do offline-first: é criado local antes de existir no servidor. Tem `AberturaSincronizada` (bool) separado de `SyncStatus` — rastreiam abertura e fechamento independentemente, porque são duas ações que acontecem em momentos diferentes (ver `docs/APRENDIZADOS.md` #17, achado numa revisão de código). |
| `ItemVenda` | Um produto dentro de uma venda (quantidade, preço, desconto). |
| `PagamentoVenda` | Um pagamento dentro de uma venda (pode ter mais de um — pagamento misto). |
| `Venda` | A venda em si — `Guid` como chave (é também a chave de idempotência enviada pra API). |
| `ConfiguracaoSincronizacao` | Config local do dispositivo: URL da API, credenciais protegidas, marcador de última sincronização por recurso, id do Consumidor Final. |
| `DigitacaoCaixa` / `DigitacaoBandeiraCaixa` | A conferência de valores no fechamento do caixa (por forma de pagamento / por bandeira de cartão). |

### Owned types (não são tabelas próprias — vivem dentro de outra tabela)

| Tipo | Dono | Pra que serve |
|---|---|---|
| `TabelaPreco` | `Cliente` | A tabela de preço do cliente (ex: "PADRAO") — um valor, não uma entidade própria. |
| `ImagemProduto` | `Produto` | Fotos do produto (pode ter mais de uma) — owned *collection*, não owned *type* único. |

### Enums de apoio

`TipoPessoa` (Física/Jurídica, em `Cliente`), `StatusCaixa` (Aberto/Fechado, em `Caixa`).

### Infraestrutura do EF Core

| Classe | Pra que serve |
|---|---|
| `AppDbContext` | O `DbContext` — a "porta de entrada" pro banco, expõe um `DbSet<T>` por entidade. |
| `AppDbContextFactory` | Permite ao `dotnet ef` (linha de comando) criar um `AppDbContext` sem precisar rodar o app inteiro — usado só nas migrations. |
| `Data/Configurations/*.cs` | Uma classe por entidade, configurando chaves, índices, relacionamentos e conversões via Fluent API (em vez de atributos na entidade). |

---

## 2. `catalog-sync` — sincronização com a API

| Classe | Pra que serve |
|---|---|
| `SegredoProtector` | Criptografa/descriptografa segredos (DPAPI, só Windows) — usado pro `client_secret` e pro certificado da empresa. |
| `SoftcomAuthService` | Troca o link de cadastro do dispositivo por um `client_secret`, e troca `client_id`/`client_secret` por um `access_token`. |
| `SoftcomApiClient` | Cliente HTTP genérico: `BuscarTudoAsync` busca qualquer recurso paginado seguindo `next_page_url` sozinho (usado por `catalog-sync`); `EnviarAsync` monta e envia um request de escrita (POST) e classifica a resposta em sucesso/conflito/token expirado/falha (usado por `caixa` e `sales`). Único lugar do projeto que fala HTTP direto. |
| `SoftcomJson` | `JsonSerializerOptions` compartilhada (`PropertyNameCaseInsensitive = true`) — evita recriar a mesma configuração em cada lugar que desserializa resposta da API. |
| `ResultadoEnvio` / `ResultadoEnvioTipo` | Resultado de `EnviarAsync`: o tipo (`Sucesso`/`Conflito`/`TokenExpirado`/`Falha`) mais o conteúdo cru da resposta, pra cada chamador (`CaixaSyncService`, `VendaSyncService`) decidir o que fazer. |
| `OutboxHelper` | `MarcarFalhaAsync<T>` genérico: grava a mensagem de erro numa entidade já carregada e salva — usado por `CaixaSyncService` e `VendaSyncService`, cada um passando sua própria regra de quais campos mexer. |
| `CatalogSyncService` | O orquestrador: autentica e sincroniza os 5 recursos (produtos, clientes, formas de pagamento, funcionários, empresa), fazendo upsert local por `IdExterno`. |
| `PaginaApiDto<T>` | O "envelope" de paginação que toda resposta da API usa (`data[]`, `next_page_url`, `date_sync`...). |
| `ResultadoBusca<T>` | Resultado de uma busca paginada (sucesso/falha + itens). |
| `ResultadoSincronizacaoRecurso` | Resultado de sincronizar UM recurso (sucesso/falha + quantidade) — reaproveitado depois em `caixa` e `sales`. |
| `ResultadoSincronizacaoCompleta` | Resultado de sincronizar TUDO (autenticação + os 5 recursos, cada um com seu próprio resultado). |
| `Dtos/*ApiDto.cs` | Um DTO por recurso (`ClienteApiDto`, `ProdutoApiDto`, `FormaPagamentoApiDto`, `EmpresaApiDto`, `FuncionarioApiDto`) — o formato exato que a API manda, convertido pra entidade local depois. |

---

## 3. `caixa` — login e sessão de caixa

| Classe | Pra que serve |
|---|---|
| `PdvKeyHasher` | Hash SHA-256 da chave de login (`pdv_key`) — compartilhado entre `catalog-sync` (grava o hash) e `caixa` (compara na hora do login). |
| `LoginOperadorService` | Login local do operador, sem rede — compara hash contra hash. |
| `CaixaService` | Abrir e fechar caixa **localmente**, sem rede — o coração do offline-first desse módulo. |
| `CaixaSyncService` | O outbox: quando há rede, confirma a abertura/fechamento com a API. Tem o método `SincronizarCaixaPendenteAsync`, que decide sozinho se é hora de sincronizar abertura ou fechamento. |
| `ResultadoOperacaoCaixa<T>` | Resultado de uma operação local (abrir/fechar) — sucesso/falha + a entidade, pra operações que podem falhar por regra de negócio (ex: caixa já existe). |
| `Dtos/CaixaFuncaoApiDto.cs` | O formato de resposta de `POST .../caixa-funcoes/abrir` e `/fechar`. |

---

## 4. `sales` — registrar e enviar vendas

| Classe | Pra que serve |
|---|---|
| `ErroApiExtractor` | Lê o formato de erro padrão da API (`{"errors": {...}}`) e monta uma mensagem legível — compartilhado entre `caixa` e `sales`. |
| `VendaService` | Registrar a venda **localmente**, sem rede — gera o `Guid`, salva itens e pagamentos. |
| `VendaSyncService` | O outbox da venda: resolve os ids externos de empresa/funcionário/cliente/produtos/formas de pagamento, monta o payload, envia, e trata sucesso/conflito/erro. Tem `SincronizarVendasPendentesAsync`, que reenvia o lote inteiro. |
| `Dtos/VendaApiDto.cs` | O formato de request (`VendaRequestDto` + `VendaProdutoRequestDto` + `VendaPagamentoRequestDto`) e response (`VendaRespostaDto`) de `POST /api/v2/vendas`. |

---

## O padrão que se repete em `catalog-sync`, `caixa` e `sales`

Todo módulo depois do `data-layer` segue a mesma forma, só mudando o que sincroniza:

1. **Serviço local** (`CaixaService`, `VendaService`) — nunca toca rede, sempre funciona, sempre rápido.
2. **Serviço de sincronização** (`CatalogSyncService`, `CaixaSyncService`, `VendaSyncService`) — o outbox: só ele fala com a API, e só quando chamado (nunca é acionado automaticamente pelo serviço local).
3. **Resultado tipado** (`ResultadoSincronizacaoRecurso`, `ResultadoOperacaoCaixa<T>`) em vez de lançar exceção pra "erro esperado" (API fora do ar, validação, regra de negócio) — exceção fica só pra erro de programação de verdade.
4. **DTOs próprios** por recurso, isolando o formato exato da API do formato da entidade local — se a API mudar um nome de campo, só o DTO muda, a entidade e o resto do app nem percebem.

Esse é o motivo de conseguir testar tudo sem rede real: o serviço local não precisa de nada externo, e o serviço de sincronização só precisa de um `HttpClient` — que nos testes é sempre um `HttpMessageHandler` fake (`FakeHttpMessageHandler`, em `SistemaPDV.Tests`).

# Resumo da Arquitetura — SistemaPDV

Mapa de tudo que existe no projeto até agora (5 módulos: os 4 de back-end — `data-layer`, `catalog-sync`, `caixa`, `sales` — e o `pdv-ui`, as telas), classe por classe, pra que serve cada uma. Complementa o `docs/APRENDIZADOS.md` (que registra os *porquês* pontuais) — aqui é o panorama de cima.

## Como os módulos se encaixam

```
data-layer     → banco local (SQLite) e as entidades
    ↓
catalog-sync   → puxa produtos/clientes/formas de pagamento/empresa/funcionários da API
    ↓
caixa          → login do operador + abrir/fechar caixa (offline-first)
    ↓
sales          → registrar e enviar vendas (offline-first)
    ↓
pdv-ui         → telas, navegação e sincronização automática em background
```

Cada módulo depende só do(s) anterior(es), nunca do posterior — `data-layer` não sabe que `sales` existe, por exemplo. O `pdv-ui` consome os quatro.

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
| `Funcionario` | Operadores do PDV — inclui `PdvKeyHash` (o hash **bcrypt** que a API manda; nunca a chave em claro). |
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
| `ResultadoEnvio` / `ResultadoEnvioTipo` | Resultado de `EnviarAsync`: o tipo (`Sucesso`/`Conflito`/`TokenExpirado`/`ConexaoInsegura`/`Falha`) mais o conteúdo cru da resposta, pra cada chamador (`CaixaSyncService`, `VendaSyncService`) decidir o que fazer. |
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
| `PdvKeyHasher` | A API manda a `pdv_key` como **hash bcrypt** (`$2y$10$…`): `EhHashBcrypt` decide o que `catalog-sync` grava (valor que não parece bcrypt nunca é gravado) e `Verificar` confere a chave digitada (`caixa`). |
| `LoginOperadorService` | Login local do operador, sem rede — confere a chave digitada com bcrypt contra cada funcionário ativo (fora da thread de UI: bcrypt é lento de propósito). |
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

## 5. `pdv-ui` — telas, navegação e sincronização automática

A interface (Avalonia + ReactiveUI, MVVM). ViewModels não falam com infraestrutura: só com os serviços que já existem. `AppServices` é a composição — constrói tudo uma vez e o `App` passa ao `ShellViewModel`.

### Telas e navegação

| Classe | Pra que serve |
|---|---|
| `ShellViewModel` / `ShellView` | A casca: decide a tela (Configurações → Login → Abrir caixa/Dashboard → Venda/Pedidos/Cadastros), guarda operador logado e caixa aberto, mostra o indicador de conexão e o banner de erro. Navegação bloqueada durante venda em andamento. |
| `Tela` | Enum das telas que o Shell sabe mostrar. |
| `Configuracoes…`, `Login…`, `AbrirCaixa…`, `Dashboard…`, `Pdv…`, `ListaPedidos…`, `Cadastros…` (`ViewModel` + `View`) | Uma dupla por tela. `AbrirCaixaViewModel` lista os 6 turnos do SoftcomShop; `ConfiguracoesViewModel.VincularCommand` devolve se vinculou (o Shell então leva ao Login). |
| `IAtualizavelPorSincronizacao` | Contrato de telas que se recarregam quando a sincronização mexe no banco (Pedidos e Cadastros; a tela de venda não, pra não mexer no carrinho). O Shell chama, ninguém assina evento (os ViewModels são recriados a cada navegação). |
| `SyncStatusIndicator` + `*Converter` | O 🟢/🟡/🔴 e o indicador Online/Offline: sempre ícone + texto, nunca só cor. |

### Serviços locais da UI (nunca tocam rede)

| Classe | Pra que serve |
|---|---|
| `CatalogoLocalService` | Leitura do catálogo pra tela de venda. |
| `VendaLocalService` | Lista de pedidos do caixa (com número do pedido e motivo de espera/falha) e "Reenviar falhas". |
| `CadastroLocalService` | Busca de clientes/produtos (`LIKE` com curingas escapados, teto de 200) e **criação local** de cliente (valida CPF/CNPJ com `DocumentoValidator`; nasce `PendenteSync`). |
| `FecharCaixaViewModel` / `FecharCaixaView` | Conferência do fechamento: esperado por forma de pagamento (das vendas do caixa), apuração digitada (`LinhaApuracao`) e troco final; só grava local. Depois de fechar o Shell volta a Abrir caixa (com `ExigirAberturaCaixa`) ou ao Dashboard. |
| `ValorMonetario` | Leitor único dos valores digitados (vírgula ou ponto; ambíguo é recusado; 2 casas pra dinheiro, 3 pra quantidade). |
| `DashboardService` / `ConfiguracaoService` | Resumo do dia; leitura/gravação da configuração do dispositivo (vínculo protegido com DPAPI). |

### Sincronização automática

| Classe | Pra que serve |
|---|---|
| `SincronizacaoBackgroundService` | Dois `DispatcherTimer`: a cada 30 s o outbox (clientes novos, caixa, vendas — só autentica se há pendência) e a cada 5 min o catálogo; o catálogo também roda no 1º tick. Os ciclos são métodos públicos e testáveis, rodam em `Task.Run` (o SQLite do EF é síncrono por baixo) e nunca sobrepõem (semáforo). Publica `EstadoConexao` (Online/OnlineComFalhas/Offline + motivo) e `DadosAlterados`. |
| `PoliticaRetentativa` / `IOutboxRetentavel` | Espera crescente (30 s → 10 min) e teto de 8 tentativas nos três outboxes; contadores no banco; "Reenviar falhas" zera. Sucesso também zera. |
| `SoftcomRotas` | Todas as rotas da API, sob `softauth/api/v2/` (sem o prefixo a API responde `500 {"error":""}`). |
| `ConexaoSegura` | Recusa `http://` (exceto loopback) em qualquer chamada à API — token, secret e dados pessoais nunca em texto puro. |
| `TratamentoDeErros` | Handler global do ReactiveUI (registrado em `Program.cs`): exceção de comando vira o banner do Shell em vez de derrubar o app. |

### Regras de negócio que só apareceram contra a API real

- **Login:** a `pdv_key` que a API devolve é **hash bcrypt** (`$2y$10$…`); `PdvKeyHasher` guarda como veio e `LoginOperadorService` confere com BCrypt (fora da thread de UI).
- **Empresa do dispositivo:** o CNPJ do **link de vínculo** (`SoftcomAuthService.ExtrairEmpresaCnpj`) diz qual das empresas da API é a deste PDV; só ela é gravada e usada na venda.
- **Venda:** `numero_documento` = `Venda.NumeroPedido` (sequencial local, gerado offline); produto tem 3 ids — `produto_id` = `Produto.ProdutoIdApi`, `produto_empresa_grade_id` = `Produto.IdExterno`; item e pagamento levam os campos que a API grava (`preco_compra`, `api_nome_pagamento`, parcela única). Venda avulsa usa o cliente com `indicador_finalidade = 1`.
- **Descarte de venda:** venda em `FalhaSync` pode ser descartada na tela de Pedidos por um **supervisor** (chave dele + motivo). Vira `Descartada` (não apagada; auditoria em `Venda`), não é enviada e sai do esperado/faturamento/pendentes — `VendaFiltros` concentra essa regra e destrava o fechamento do caixa.
- **Esperas visíveis:** dependência não sincronizada deixa a venda `PendenteSync` mas grava o motivo em `UltimoErroSync` (a lista mostra).

---

## O padrão que se repete em `catalog-sync`, `caixa` e `sales`

Todo módulo depois do `data-layer` segue a mesma forma, só mudando o que sincroniza:

1. **Serviço local** (`CaixaService`, `VendaService`) — nunca toca rede, sempre funciona, sempre rápido.
2. **Serviço de sincronização** (`CatalogSyncService`, `CaixaSyncService`, `VendaSyncService`) — o outbox: só ele fala com a API, e só quando chamado (nunca é acionado automaticamente pelo serviço local).
3. **Resultado tipado** (`ResultadoSincronizacaoRecurso`, `ResultadoOperacaoCaixa<T>`) em vez de lançar exceção pra "erro esperado" (API fora do ar, validação, regra de negócio) — exceção fica só pra erro de programação de verdade.
4. **DTOs próprios** por recurso, isolando o formato exato da API do formato da entidade local — se a API mudar um nome de campo, só o DTO muda, a entidade e o resto do app nem percebem.

Esse é o motivo de conseguir testar tudo sem rede real: o serviço local não precisa de nada externo, e o serviço de sincronização só precisa de um `HttpClient` — que nos testes é sempre um `HttpMessageHandler` fake (`FakeHttpMessageHandler`, em `SistemaPDV.Tests`).

# Spec: pdv-ui

## Objective

As telas MVVM do PDV, seguindo o protótipo mockado do curso: login do operador, dashboard, tela de venda (PDV), listagem de pedidos e cadastros. É a camada que amarra `catalog-sync`, `caixa` e `sales` numa experiência usável — e onde os conceitos de OOP/MVVM do curso ficam mais visíveis (ViewModels, binding, comandos).

Sucesso = fluxo completo navegável: logar → (se exigido) abrir caixa → vender (offline ou online) → ver o pedido na listagem com o status de sincronização certo → fechar caixa. Sincronização roda sozinha em background, sem travar a UI.

## Tech Stack

- Avalonia 12.1.1, .NET 8
- **Framework de MVVM: ReactiveUI.Avalonia** (decidido em 2026-09-18) — mantém o que já está no `.csproj`/template.
- **Composição de dependências: wiring manual**, sem container de DI (decidido em 2026-09-18) — os serviços de `catalog-sync`/`caixa`/`sales` já existem como classes concretas com construtor explícito (`Func<AppDbContext> contextFactory`, `SoftcomApiClient`, etc.); uma classe `AppServices` monta as instâncias uma vez em `App.axaml.cs` e passa pros ViewModels via construtor. Evita introduzir mais um conceito novo (container, ciclo de vida de serviço) num módulo que já é o mais denso do curso em termos de UI/binding.
- Depende de `catalog-sync`, `caixa`, `sales`

## Commands

```
dotnet build
dotnet run --project SistemaPDV
dotnet test --filter Ui
```

## Project Structure

```
SistemaPDV/
  AppServices.cs                 # composition root: monta e expõe os serviços de todos os módulos
  ViewModels/
    LoginViewModel.cs
    AbrirCaixaViewModel.cs       # só navegável se ConfiguracaoSincronizacao.ExigirAberturaCaixa
    DashboardViewModel.cs
    PdvViewModel.cs               # tela de venda
    ListaPedidosViewModel.cs
    CadastrosViewModel.cs         # clientes (leitura + criação simples) / produtos (leitura)
    ConfiguracoesViewModel.cs     # toggle ExigirAberturaCaixa e outras configs locais
    ShellViewModel.cs             # navegação entre as telas acima + indicador de sync/conexão
  Views/
    LoginView.axaml
    AbrirCaixaView.axaml
    DashboardView.axaml
    PdvView.axaml
    ListaPedidosView.axaml
    CadastrosView.axaml
    ConfiguracoesView.axaml
  Services/
    Sync/
      SincronizacaoBackgroundService.cs   # timer que dispara os 3 outboxes existentes sozinho
```

## Code Style

Colocar View + ViewModel lado a lado por tela (nome espelhado: `PdvView.axaml` ↔ `PdvViewModel.cs`), sem code-behind com lógica — code-behind só faz `InitializeComponent()`. Toda lógica de apresentação fica no ViewModel, testável sem instanciar UI.

## Convenções de UI (adaptado de `frontend-ui-engineering` pra Avalonia/XAML)

A skill `frontend-ui-engineering` é escrita pra web (React/Tailwind/ARIA) — aqui vai a tradução pra desktop/Avalonia, aplicada a cada View da Fase 5:

- **Composição em vez de duplicação de XAML.** Um elemento visual repetido em mais de uma tela vira `UserControl` próprio, não copy-paste. O caso claro aqui é o indicador de `SyncStatus` (🟢/🟡/🔴) — usado em `Cadastros`, `ListaPedidos` e `Pdv` — vira um `SyncStatusIndicator.axaml` único, reaproveitado nas três.
- **Design tokens no lugar de valores soltos.** Cores e tipografia ficam como `StaticResource` num `ResourceDictionary` próprio (`Themes/PdvTheme.axaml`, incluído em `App.axaml`), não hex/pixel cravado em cada `.axaml`. Valores extraídos direto do CSS do protótipo real (`treinamento-dotnet-softcom-tecnologia.jhonycosmo.workers.dev/pdv-prototype`, 2026-09-19) — não é suposição, é o mesmo tema, com nome de token igual ao original pra facilitar comparar:

  | Token (prototype) | Hex | Uso | Nome do resource Avalonia |
  |---|---|---|---|
  | `--bg-main` | `#0b0f17` | fundo da janela | `BgMainBrush` |
  | `--bg-card` | `#111722` | fundo de card/painel | `BgCardBrush` |
  | `--bg-card-hover` | `#161f2e` | hover de card | `BgCardHoverBrush` |
  | `--bg-input` | `#0e141f` | fundo de campo de texto | `BgInputBrush` |
  | `--border-color` | `#232d3f` | borda padrão | `BorderColorBrush` |
  | `--border-focus` | `#fed400` | borda de campo focado | `BorderFocusBrush` |
  | `--text-main` | `#e2e8f0` | texto principal | `TextMainBrush` |
  | `--text-muted` | `#94a3b8` | texto secundário | `TextMutedBrush` |
  | `--text-dark` | `#0f172a` | texto sobre fundo claro/amarelo | `TextDarkBrush` |
  | `--brand-yellow` | `#fed400` | cor de marca / ação primária | `BrandYellowBrush` |
  | `--accent-green` | `#10b981` | 🟢 Sincronizado | `AccentGreenBrush` |
  | `--accent-blue` | `#38bdf8` | info/online | `AccentBlueBrush` |
  | `--accent-red` | `#f43f5e` | 🔴 Falha/erro | `AccentRedBrush` |

  Tema escuro (dark), fonte `Inter` (texto) + `Fira Code` (monoespaçada — usada no protótipo pra números/valores em R$, combina com o `CultureInfo.InvariantCulture` já usado nos DTOs). `CornerRadius` do protótipo varia por elemento: 4-8px (botões/inputs pequenos), 10-14px (cards), `50%` (círculos de status), `999px` (badges tipo pílula) — replicar como `CornerRadius` nos estilos do Avalonia, não um valor único pra tudo.
- **Estado nunca só por cor.** Cada indicador de `SyncStatus` sempre combina ícone + texto (🟢 Sincronizado / 🟡 Pendente / 🔴 Falha) — nunca só uma bolinha colorida. Já é como o protótipo mockado mostra.
- **Acessibilidade por teclado (equivalente Avalonia da seção WCAG da skill):** todo botão só-ícone (ex: os atalhos F2/F4/F10/Esc da tela de venda, "➕ Cadastrar Cliente") ganha `AutomationProperties.Name`; toda a tela de venda precisa ser operável 100% por teclado, sem depender de mouse (já é Success Criterion da spec); ordem de `TabIndex` sensata em cada formulário (Login, AbrirCaixa, Configurações, criar cliente).
- **Estados vazio/carregando/erro em toda lista.** `ListaPedidosView` (sem vendas hoje), `CadastrosView` (sem clientes/produtos ainda sincronizados) e `DashboardView` (antes da primeira sincronização) mostram uma mensagem própria — nunca uma tela em branco sem explicação.
- **Layout proporcional, não pixel fixo.** PDV é um app desktop de janela única (não precisa dos breakpoints mobile da skill), mas o layout usa `Grid`/`DockPanel` com proporções (`*`, `Auto`), não posições em pixel fixo — sobrevive a redimensionar a janela sem quebrar.

## Novidades descobertas nessa revisão (além do que o mockup já mostrava)

Estas três coisas não existiam nos módulos já construídos e precisam ser adicionadas como parte do `pdv-ui`:

1. **`AppServices` (composition root)** — hoje `App.axaml.cs` só faz `new MainViewModel()`; não existe nenhum lugar que monte `CaixaService`, `VendaService`, `CatalogSyncService`, `LoginOperadorService` e suas dependências (`SoftcomApiClient`, `SegredoProtector`, etc.). Precisa ser criado do zero.

2. **`ConfiguracaoSincronizacao.ExigirAberturaCaixa`** (novo campo `bool`, default `true`) — decisão do usuário (2026-09-18): abertura de caixa obrigatória pós-login vira uma **configuração**, não uma regra fixa. Quando `true`, login → (sem caixa aberto hoje) → `AbrirCaixaView` obrigatória antes de Dashboard/PDV. Quando `false`, login → Dashboard direto, abrir caixa vira uma ação opcional acessível pelo menu. Precisa de uma migration nova (`AddExigirAberturaCaixa`).

3. **Criação de cliente simples, com push real pra API** — o endpoint `POST {dominio}/softauth/api/v2/clientes/clientes` existe e devolve `{ "data": { "id": <int> } }` (200), `409` (conflito) e `422` (validação) — mesmo formato de resposta já tratado em `SoftcomApiClient.EnviarAsync`. Isso muda o que "cadastrar cliente" significa: não é só uma tela, é uma nova ponta de sincronização (outbox pra cliente, simétrico ao que já existe pra caixa/venda). Ver detalhe abaixo.

   - Formulário simples: **Nome** + **CPF/CNPJ**. `Pessoa` é inferido do tamanho do documento (11 dígitos → Física, 14 → Jurídica) — sem campo extra na tela.
   - Defaults pros campos obrigatórios da API que o formulário não pergunta: `contribuinte_icms = 9` ("Não Contribuinte", o caso comum de cliente de balcão) e `indicador_finalidade = 0` ("Normal" — `1` é reservado pro Consumidor Final, não se aplica aqui).
   - Cliente criado localmente nasce com `SyncStatus = PendenteSync` e `IdExterno = null` (comportamento padrão já existente na entidade, nenhuma mudança de schema necessária).
   - **Novo método** `CatalogSyncService.SincronizarClienteNovoAsync(clienteId, accessToken, ct)` (um) e `SincronizarClientesNovosPendentesAsync(accessToken, ct)` (lote, o que o timer chama) — busca clientes com `IdExterno == null && SyncStatus == PendenteSync`, envia via `apiClient.EnviarAsync(POST, .../clientes/clientes, corpo, token)`, grava o `IdExterno` retornado. Mesmo padrão de outbox já usado em `CaixaSyncService`/`VendaSyncService` (reaproveita `OutboxHelper.MarcarFalhaAsync`, `SoftcomJson`, `ErroApiExtractor`).
   - **Limitação até esse push rodar:** a tela de venda só deixa selecionar clientes que já têm `IdExterno` — um cliente recém-criado localmente fica indisponível pra venda até o próximo ciclo de sincronização confirmar o cadastro (mesma regra que `VendaSyncService` já aplica pra qualquer dependência não sincronizada).
   - Endpoint ainda não testado contra a API real (assim como os outros) — prefixo `softauth/` assumido por analogia ao `GET` de clientes; `409`/`422` tratados como falha (sem tratamento especial de "cliente duplicado" no v1).

4. **`SincronizacaoBackgroundService`** — timer em background que tenta sincronizar sozinho quando percebe rede disponível (decisão do usuário: automático já no v1, não só botão manual). Dispara em dois ritmos diferentes:
   - A cada **30s**: `CaixaSyncService.SincronizarCaixaPendenteAsync`, `VendaSyncService.SincronizarVendasPendentesAsync`, `CatalogSyncService.SincronizarClientesNovosPendentesAsync` (outboxes — coisas que o operador está esperando confirmar).
   - A cada **5 min**: `CatalogSyncService.SincronizarTudoAsync` (catálogo — produtos/clientes/formas de pagamento/funcionários/empresa, menos urgente).
   - Uma falha de rede num ciclo não deve gerar exceção não tratada nem popup — só atualiza o indicador de conexão da `ShellView` pra "offline" e tenta de novo no próximo ciclo.
   - Ambos os ritmos e o próprio timer ficam pausados enquanto a `ConfiguracaoSincronizacao` não estiver preenchida (sem URL/credenciais configuradas, não tem o que tentar).

## Testing Strategy

- ViewModels testados sem Avalonia rodando (são POCOs/ReactiveObject, não dependem de janela real) — cobre lógica de comando (ex: `PodeFinalizarVenda` só habilitado com caixa aberto e itens no carrinho).
- `SincronizacaoBackgroundService` e `CatalogSyncService.SincronizarClienteNovoAsync` testados como os outros serviços de sync já existentes (mesmo padrão: `FakeHttpMessageHandler`, `SqliteInMemoryFixture`).
- Fluxo de UI ponta-a-ponta fica fora do escopo automatizado por ora — verificação manual (rodar `dotnet run` e navegar o fluxo) antes de considerar uma tela pronta.

## Boundaries

- **Sempre:** refletir o `SyncStatus` de cada entidade sincronizável na UI com indicador visual (🟢/🟡/🔴), igual ao protótipo mockado.
- **Sempre:** qualquer chamada de rede (sync, envio de venda, criação de cliente) roda em background com feedback visual, nunca bloqueia um clique de comando.
- **Perguntar antes:** qualquer edição de cadastro além do formulário simples de cliente (nome + CPF/CNPJ) — ex: editar cliente existente, cadastrar/editar produto — é escopo novo.
- **Perguntar antes:** mudar os ritmos do timer de sincronização (30s/5min) ou o comportamento em caso de falha repetida (hoje: silencioso, sempre retenta).
- **Nunca:** deixar a tela de venda travar esperando rede.
- **Nunca:** deixar o timer de background derrubar o app com exceção não tratada — falha de sync é sempre silenciosa + indicador visual, nunca crash.

## Success Criteria

- [ ] Fluxo login → (se `ExigirAberturaCaixa`) abrir caixa → vender → fechar caixa navegável de ponta a ponta
- [ ] Toggle `ExigirAberturaCaixa` em Configurações muda o fluxo pós-login de verdade (testável nos dois estados)
- [ ] Indicadores de sincronização (🟢/🟡/🔴) refletem o estado real do banco local
- [ ] Vender offline funciona e a venda aparece como pendente até sincronizar
- [ ] Timer de background sincroniza outbox (caixa/venda/cliente) a cada 30s e catálogo a cada 5min, sem travar a UI e sem crashar em falha de rede
- [ ] Cadastrar cliente simples (nome + CPF/CNPJ) grava local na hora e sincroniza sozinho quando o timer rodar, aparecendo disponível na tela de venda só depois de ter `IdExterno`
- [ ] Atalhos de teclado do protótipo (F2 novo, F4 buscar, F10 pagar, Esc cancelar) funcionam na tela de venda

## Open Questions

```
ASSUMPTIONS I'M MAKING:
1. O endpoint de criar cliente usa o prefixo `softauth/api/v2/clientes/clientes`, por
   analogia ao GET do mesmo recurso — não testado contra a API real ainda.
2. Defaults pro formulário simples de cliente: contribuinte_icms=9 (Não Contribuinte),
   indicador_finalidade=0 (Normal), pessoa inferida por tamanho do documento
   (11 dígitos=Física, 14=Jurídica).
3. Ritmo do timer de sync: 30s pra outbox (caixa/venda/cliente), 5min pra catálogo
   completo — números escolhidos por mim, sem confirmação explícita do usuário.
4. AppServices é uma classe simples com propriedades públicas pros serviços já
   prontos (não um container genérico) — construída uma vez em
   App.axaml.cs/OnFrameworkInitializationCompleted.
→ Corrija agora ou sigo com essas assunções pro Plan.
```

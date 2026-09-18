# Spec: pdv-ui

## Objective

As telas MVVM do PDV, seguindo o protótipo mockado do curso: login do operador, dashboard, tela de venda (PDV), listagem de pedidos e cadastros. É a camada que amarra `catalog-sync`, `caixa` e `sales` numa experiência usável — e onde os conceitos de OOP/MVVM do curso ficam mais visíveis (ViewModels, binding, comandos).

Sucesso = fluxo completo navegável: logar → abrir caixa → vender (offline ou online) → ver o pedido na listagem com o status de sincronização certo → fechar caixa.

## Tech Stack

- Avalonia 12.1.1, .NET 8
- **Framework de MVVM: ReactiveUI.Avalonia** (decidido em 2026-09-18) — mantém o que já está no `.csproj`/template, em vez de trocar pro `CommunityToolkit.Mvvm` do projeto de referência.
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
  ViewModels/
    LoginViewModel.cs
    DashboardViewModel.cs
    PdvViewModel.cs            # tela de venda
    ListaPedidosViewModel.cs
    CadastrosViewModel.cs      # clientes/produtos (leitura; edição fora de escopo inicial)
    ShellViewModel.cs          # navegação entre as telas acima
  Views/
    LoginView.axaml
    DashboardView.axaml
    PdvView.axaml
    ListaPedidosView.axaml
    CadastrosView.axaml
```

## Code Style

Colocar View + ViewModel lado a lado por tela (nome espelhado: `PdvView.axaml` ↔ `PdvViewModel.cs`), sem code-behind com lógica — code-behind só faz `InitializeComponent()`. Toda lógica de apresentação fica no ViewModel, testável sem instanciar UI.

## Testing Strategy

- ViewModels testados sem Avalonia rodando (são POCOs/ObservableObject, não dependem de janela real) — cobre lógica de comando (ex: `PodeFinalizarVenda` só habilitado com caixa aberto e itens no carrinho).
- Fluxo de UI ponta-a-ponta fica fora do escopo automatizado por ora — verificação manual (rodar `dotnet run` e navegar o fluxo) antes de considerar uma tela pronta.

## Boundaries

- **Sempre:** refletir o `SyncStatus` de cada entidade sincronizável na UI com indicador visual (🟢/🟡/🔴), igual ao protótipo mockado.
- **Perguntar antes:** adicionar edição de cadastro de produto/cliente direto no PDV (o protótipo mostra só leitura + "Cadastrar Cliente" simples) — se isso virar CRUD completo, é escopo novo.
- **Nunca:** deixar a tela de venda travar esperando rede — qualquer chamada de rede (sync, envio de venda) roda em background com feedback visual, nunca bloqueia o clique de "Finalizar Venda".

## Success Criteria

- [ ] Fluxo login → abrir caixa → vender → fechar caixa navegável de ponta a ponta
- [ ] Indicadores de sincronização (🟢/🟡) refletem o estado real do banco local
- [ ] Vender offline funciona e a venda aparece como pendente até sincronizar
- [ ] Atalhos de teclado do protótipo (F2 novo, F4 buscar, F10 pagar, Esc cancelar) funcionam na tela de venda

## Open Questions

```
ASSUMPTIONS I'M MAKING:
1. Cadastros (clientes/produtos) começam como tela de LEITURA (busca/listagem), sem
   criar/editar — alinhado ao mockup, que só mostra "➕ Cadastrar Cliente" como botão
   solto, sem detalhar o formulário.
2. Não há tela de "abrir caixa" como página separada — é um passo obrigatório logo
   após o login (não dá pra navegar pro Dashboard/PDV sem caixa aberto), seguindo a
   regra de negócio definida em SPEC-caixa.
→ Corrija agora ou sigo com essas assunções.
```

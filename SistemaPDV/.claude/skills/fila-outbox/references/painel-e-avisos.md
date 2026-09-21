# Painel "Fila Outbox", pílula e avisos

## Peças e como se ligam

```
SincronizacaoBackgroundService                    (thread do sistema)
   ├─ Log (LogDeSincronizacao) ── Novas ──►  App.axaml.cs ── Post(UI) ──► ShellViewModel.AdicionarAtividade
   ├─ EstadoConexaoAlterada ─────────────►  App ── Post(UI) ──► ShellViewModel.DefinirConexao ──► toasts
   └─ DadosAlterados ────────────────────►  App ── Post(UI) ──► ShellViewModel.NotificarDadosSincronizados
                                                                   ├─ tela aberta (IAtualizavelPorSincronizacao) recarrega
                                                                   └─ AtualizarPendentesAsync ──► "Sync: N pendentes"

ShellViewModel.SincronizarAgoraCommand ─► SolicitarSincronizacao ─► toast + linha no log ─► SincronizacaoSolicitada ─► App ─► serviço.SolicitarAgora
```

O Shell **não conhece** o serviço de fundo: o `App` faz a ponte e leva cada valor de volta para a thread de interface.

## Log de execução

- `LogDeSincronizacao`: as **100** linhas mais recentes, em memória (some ao fechar o app; o histórico é o arquivo de log). Da mais recente para a mais antiga.
- Uma linha é `EntradaDeLog(Quando, Nivel, Texto)`, nível `Info`, `Sucesso`, `Aviso` ou `Erro` (cor pelo `NivelAtividadeParaCorConverter`).
- `Registrar` pode ser chamado de qualquer thread; a emissão fica **dentro da trava** para não haver `OnNext` concorrente nem fora de ordem.
- O ciclo **ocioso não escreve nada** (senão vira parede de ruído). O catálogo (a cada 5 min) só vira linha quando trouxe algo, ou quando uma falha **nova** aparece.

### O que pode e o que não pode aparecer

| Pode | Não pode |
|---|---|
| "POST /vendas: Pedido #1003 sincronizado com sucesso!" | Token, senha, `client_secret`, chave do PDV |
| O motivo da recusa **como a API devolveu** (o mesmo texto da lista de pedidos) | Corpo bruto da resposta de autenticação que falhou |
| "erro inesperado (detalhes no log do aplicativo)" | Mensagem ou stack de exceção (vai só para `Registro`, o arquivo de log) |

Dados de venda em texto livre (nome de cliente etc.) também **não** entram: o log fala de número de pedido.

## Painel lateral (`PainelFilaOutbox`)

- Abre pela pílula da barra do topo (`AbrirPainelOutboxCommand`). Fecha por **Esc**, pelo ✕ ou clicando no fundo escurecido (`FecharPainelOutboxCommand`).
- O fundo é um **botão** transparente (não só um clique) para o teclado e o leitor de tela também o alcançarem; fica fora da ordem do Tab.
- Enquanto aberto, a tela de trás fica **desabilitada** (`ContentControl` do Shell), então o painel cuida do foco: `GuardaDeFoco` lembra o último elemento focado fora dele e devolve o foco ao fechar. Painel que **já nasce visível** não dispara "ficou visível": também foca ao anexar.
- O atalho **Esc** vem por `AtalhosDeTela.Ativos="True"` (um manipulador na janela): `KeyBinding` de `UserControl` só dispara com um descendente focado.
- Mostra `TextoFilaLocal` ("0 itens", "1 item", "N itens"), o botão "Disparar Sincronização Agora" e a lista (`SelectableTextBlock`, para o operador copiar uma linha).

## Avisos (toasts)

`ToastCentral` (`Toasts`) com `Publicar(texto, ícone, tipo, chave, duração)`. A **chave** substitui o aviso anterior do mesmo assunto em vez de empilhar.

| Quando | Texto | Chave |
|---|---|---|
| Ficou `Offline` | "Internet desconectada. O PDV continua operando 100% no banco local!" | `conexao` |
| Voltou de `Offline` | "Conexão com a nuvem SoftcomShop restaurada! Sincronizando fila..." | `conexao` |
| Fila esvaziou depois de voltar | "Nenhuma pendência na fila local. Todos os pedidos estão na nuvem!" | `fila` |
| Clicou em sincronizar | Diz a verdade: **offline** ("Sem conexão: o que está na fila sai assim que a internet voltar."), **fila vazia** ("Nenhuma pendência na fila local.") ou **sincronizando** | `sincronizacao` |

Regras:

- **Só a mudança avisa.** Cada ciclo republica o mesmo estado; sem comparar com o anterior haveria um aviso a cada 30 s. Abrir o app **já offline** avisa; abrir online não (não há nada "restaurado").
- `OnlineComFalhas` conta como **conectado** (a API respondeu; o que falhou vai no log do catálogo).
- O aviso "fila vazia" tem duas rotas para não ficar a dúvida "será que mandou tudo?": a recontagem **imediata** ao reconectar (se não há nada a enviar, nenhum ciclo mexeria no banco e ninguém recontaria) e a recontagem depois do ciclo.
- Ao ficar offline, `avisarFilaVazia` é zerado (senão o aviso saltaria sozinho depois).
- O toast de "sincronizar" é publicado **antes** de sinalizar o serviço, para o operador ver a resposta ao clique antes de o trabalho começar.
- Limitação conhecida (FYI): o aviso "Internet desconectada" aparece para **qualquer** `Offline`, inclusive credencial recusada. Tornar o texto sensível ao motivo está no backlog.

## Ao mexer nesta parte

1. Use `frontend-ui-engineering` (foco, teclado, estados vazio/erro, responsivo).
2. Renderize numa janela de teste sem cabeça (Avalonia.Headless + Skia) para ver o painel; registre lá os conversores novos usados como `StaticResource`.
3. Atualize `FilaOutboxLogTests` e `ToastsTests`; conferir que **nada de segredo** chega ao texto de nenhuma linha.
4. Um teste que lê um valor que uma tarefa de fundo muda **espera a tarefa** (o `RecontagemAposReconexao` existe para isso).

---
name: fila-outbox
description: A fila de saída (outbox) do SistemaPDV — o que entra nela, os estados de cada item, o ciclo de envio, a espera crescente e a desistência, "Reenviar falhas" e o descarte por supervisor, o painel lateral "Fila Outbox" com o log para o operador e os avisos de conexão. Use ao mexer no envio de caixa, venda ou cliente novo, ao colocar uma entidade nova na fila, ao investigar "por que este item está pendente/parado", ao mexer no painel da fila ou nos avisos (toasts), ou ao escrever testes da fila.
---

# Fila outbox

## Visão geral

Tudo o que o operador faz (abrir caixa, vender, cadastrar cliente, fechar caixa) é gravado no SQLite local **na hora**. A fila outbox é o que leva isso à API SoftcomShop depois, quando há rede, sem o operador esperar nem perder nada. A fila **não é uma tabela própria**: cada entidade sincronizável carrega o seu estado (`SyncStatus`, `TentativasEnvio`, `ProximaTentativaEm`, `UltimoErroSync`), e o "N pendentes" da tela é uma contagem sobre essas entidades.

Esta skill cobre a fila como **funcionalidade** (estados, ciclo, retentativa, o que o operador vê). Para o que a **API** exige (rotas, campos, formatos), use a skill `softcomshop-sync`.

## Quando usar

- Vai criar ou alterar o envio de caixa, venda ou cliente novo.
- Vai colocar uma entidade nova na fila.
- Alguém pergunta "por que esta venda está pendente / parada / em falha?".
- Vai mexer no painel "Fila Outbox", na pílula "Sync: N pendentes" ou nos avisos de conexão.
- Vai escrever ou revisar testes da fila.

## O que entra na fila

| Entidade | Está na fila quando | Enviada em |
|---|---|---|
| Cliente criado localmente | `IdExterno` nulo e `SyncStatus` ≠ `Sincronizado` | 1º |
| Caixa (abertura) | `AberturaSincronizada` falso | 2º |
| Venda | `PendenteSync` ou `FalhaSync` | 3º |
| Caixa (fechamento) | caixa `Fechado` e `SyncStatus` ≠ `Sincronizado` | 4º |

A ordem é fixa: a venda referencia cliente e caixa, e o fechamento **resume** o caixa (se chegasse antes das vendas, a API fecharia um caixa vazio).

## Os estados de um item

```
PendenteSync ──envio ok──► Sincronizado
     │  ▲                       ▲
     │  └─ espera por          │ 409 (venda/abertura)
     │     dependência          │
     └──erro da API/rede──► FalhaSync ──"Reenviar falhas"──► (volta à fila, contador zerado)
                               │
                               └─ só venda, só supervisor ──► Descartada (nunca mais enviada)
```

- **Espera por dependência ≠ falha.** Funcionário, empresa, cliente, produto ou forma de pagamento sem `IdExterno` ⇒ o item continua `PendenteSync`, **não gasta tentativa**, e o motivo fica em `UltimoErroSync` (a lista de pedidos mostra). O motivo some sozinho quando o item é enviado.
- **`Descartada`** é tratada como cancelada nos totais, mas continua visível como trilha de auditoria. Nunca `DELETE`.

## O ciclo

`SincronizacaoBackgroundService` (um por app, ligado pelo `App`):

| Ritmo | O quê |
|---|---|
| 30 s | Ciclo do outbox: **só fala com a rede se houver algo elegível** (senão nem autentica) |
| 5 min | Catálogo completo |
| 15 s + evento do Windows | Verificação de conexão (HEAD leve) |
| Sob demanda | Botão "Disparar Sincronização Agora" (`SolicitarAgora`) |

Regras que valem para qualquer ciclo:

1. **Um ciclo por vez** (semáforo, sem fila): se o anterior ainda roda, o novo é ignorado.
2. **Nunca na thread de UI**: o timer dispara nela, mas o trabalho vai para `Task.Run`.
3. **Exceção nunca escapa** (só cancelamento): vira `Offline` + mensagem.
4. **Cada etapa é isolada** (`ExecutarEtapaAsync`): uma etapa que falha não impede as seguintes; dentro de uma etapa, um item que falha não impede os outros.
5. Quando o ciclo mexeu no banco, emite `DadosAlterados` e as telas abertas se recarregam (`IAtualizavelPorSincronizacao`). Ciclo ocioso **não** emite.

## Retentativa (`PoliticaRetentativa`)

Falhou ⇒ espera **30 s → 1 → 2 → 4 → 8 → 10 min** (teto) e **desiste na 8ª tentativa** (~35 min). O contador e a próxima tentativa ficam **no banco** (sobrevivem a reabrir o app). O filtro `Elegivel<T>` é uma expressão que o EF traduz para SQL.

Desistir precisa de saída: **"Reenviar falhas"** zera o contador (`VendaLocalService.ReenviarFalhasAsync`, `CadastroLocalService.ReenviarFalhasAsync`); dar certo também zera. Detalhes e números em [references/ciclo-e-retentativa.md](references/ciclo-e-retentativa.md).

## O que o operador vê

- **Pílula da barra do topo:** "Sync: N pendentes" / "tudo enviado" (contagem de `DashboardService.ContarPendentesAsync`). É um botão que abre o painel.
- **Painel lateral "Fila Outbox":** número de pendências, botão de sincronizar agora e o **log de execução** (100 linhas, em memória, da mais recente para a mais antiga, colorido por nível). O log é para o **operador**: nunca token, senha nem detalhe de exceção.
- **Avisos rápidos (toasts):** conexão perdida, conexão restaurada, "Nenhuma pendência na fila local…", resposta ao clique de sincronizar. Só a **mudança** de estado avisa.

Como cada peça se liga (e por quê) está em [references/painel-e-avisos.md](references/painel-e-avisos.md).

## Como colocar uma entidade nova na fila

1. A entidade implementa `IOutboxRetentavel` e tem `IdExterno`, `SyncStatus` (enum guardado como **texto**), `UltimoErroSync`. Migration junto (gere numa cópia limpa do projeto e confira com o teste "migrations = modelo").
2. Gere a **chave de idempotência no cliente** ao criar (o `guid` da venda), nunca só na hora de enviar.
3. Crie o serviço de envio com dois métodos: **um item** e **lote** (o lote filtra com `PoliticaRetentativa.Elegivel<T>` e isola cada item em `try/catch`).
4. Falha: `PoliticaRetentativa.RegistrarFalha` + `OutboxHelper.MarcarFalhaAsync`. Sucesso: `Zerar` + limpar `UltimoErroSync`.
5. Dependência sem `IdExterno` ⇒ **espera**, não falha.
6. Devolva `ResultadoSincronizacaoRecurso` com `Detalhes` (uma linha por item) — o log do painel é alimentado por eles.
7. Encaixe a etapa em `ExecutarCicloOutboxAsync` **na ordem certa** e inclua a entidade em `ContarPendentesAsync` (do serviço de fundo **e** do `DashboardService`, que são duas contagens com o mesmo critério).
8. Decida o que `409` significa **para essa entidade** (venda/abertura: já existe = sucesso; cliente: a resposta não traz o id = falha).
9. Testes (veja abaixo) e `docs/APRENDIZADOS.md` com o "porquê".

## Diagnosticar "este item está parado"

Pergunte na ordem — [references/ciclo-e-retentativa.md](references/ciclo-e-retentativa.md) tem o passo a passo com as consultas:

1. Está `PendenteSync` com `UltimoErroSync` preenchido? ⇒ **espera por dependência** (o texto diz o que falta).
2. `FalhaSync` com `ProximaTentativaEm` no futuro? ⇒ **em espera crescente**; passa sozinho.
3. `FalhaSync` com `TentativasEnvio` = 8? ⇒ **desistiu**; precisa de "Reenviar falhas" (ou de corrigir a causa).
4. Nada disso e o app diz `Offline`? ⇒ conexão, credencial recusada ou URL sem HTTPS (a dica da pílula de conexão diz qual).
5. O caixa não fecha? ⇒ ainda há venda não enviada (`ContarNaoEnviadasAsync`).

Só investigue numa **cópia** do banco e nunca escreva na API real para "testar" (skill `softcomshop-sync`).

## Testes que a fila exige

| Caso | Onde já existe |
|---|---|
| Espera crescente, teto, `Elegivel`, "Reenviar falhas" zera | `PoliticaRetentativaTests`, `RetentativaOutboxTests` |
| Envio de venda: sucesso, 409, falha, dependência faltando | `VendaSyncServiceTests`, `VendaSyncServiceCorrecoesTests`, `VendaSyncServiceIntegracaoTests` |
| Ciclo: ordem, ocioso não autentica, um item não trava o lote | `SincronizacaoBackgroundServiceTests` |
| Log do painel (linhas por venda, sem segredo) | `FilaOutboxLogTests` |
| Detecção de queda e volta (só a mudança age) | `VerificacaoDeConexaoTests` |
| Avisos (só a mudança avisa, "sincronizar" diz a verdade) | `ToastsTests` |
| Descarte por supervisor | `DescarteDeVendaTests`, `ListaPedidosDescarteTests` |
| Resposta hostil (HTML com 200, corpo enorme) | `SincronizacaoRespostaHostilTests` |

Regras de higiene: teste o **ciclo** (método público), não o timer; espere o **sinal de saída**, não a operação de entrada; injete `TimeProvider` em vez de `Task.Delay`; rode a suíte várias vezes seguidas (instável = bug de teste); confira o **código de saída** do build antes de commitar (`build | grep && commit` esconde o erro).

## Armadilhas que já custaram caro

| Armadilha | Consequência / regra |
|---|---|
| `SyncStatus` único para duas confirmações (abertura e fechamento do caixa) | O fechamento nunca era detectado como pendente ⇒ campo `AberturaSincronizada` separado |
| Retentativa sem teto | Erro 422 permanente reenviado a cada 30 s para sempre, com token novo a cada vez |
| Marcar `Sincronizado` num 409 de cliente | `IdExterno` nulo e toda venda daquele cliente travada em silêncio |
| Ciclos ociosos não falam com a rede | Uma queda levava até 5 min para aparecer ⇒ verificador de conexão (15 s + evento do Windows) |
| Repetir "alcançável" disparando ciclo | Credencial errada viraria um login a cada 15 s ⇒ só a **mudança** de estado age; recuperação tenta no máximo 3 vezes |
| Dizer "conexão perdida" para qualquer `Offline` | `Offline` também é credencial recusada e URL sem HTTPS ⇒ o texto não pode mandar o operador olhar o cabo |
| Publicar estado de várias threads sem trava | `Subject` do Rx não aceita `OnNext` concorrente ⇒ trava em `Publicar` e em `Registrar` |
| Log com corpo de resposta ou exceção | Vazamento na tela ⇒ o detalhe vai só para o arquivo de log |
| Contagem "N pendentes" diferente da que o ciclo usa | O painel diz 3 e o ciclo não envia nada (item em espera) ⇒ documente qual conta é qual (a do painel inclui itens em espera; a do ciclo só os elegíveis) |

## Ver também

- `softcomshop-sync`: rotas, corpo exigido pela API, `409`, investigação segura (GET numa cópia do banco).
- `frontend-ui-engineering`: ao mexer no painel e nos avisos.
- `docs/APRENDIZADOS.md`: o "porquê" de cada decisão, com o contexto do erro.

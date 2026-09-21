# Ciclo, retentativa e diagnóstico

## Um ciclo do outbox, passo a passo

`SincronizacaoBackgroundService.ExecutarCicloOutboxAsync` (a cada 30 s, ou sob demanda):

1. Tenta o semáforo **sem esperar**. Se outro ciclo roda, sai (sem fila).
2. Sem configuração completa (URL + `client_id` + segredo) ⇒ sai. Dispositivo não vinculado pausa tudo.
3. **Conta o que é elegível agora** (`ContarPendentesAsync`). Zero ⇒ sai **sem autenticar**: uma requisição a cada 30 s sem motivo seria ruído para uma API de produção.
4. Autentica. Falhou ⇒ publica `Offline` com a mensagem e sai.
5. Publica `Online` e registra "Disparando sincronização de N item(ns) pendente(s)...".
6. Etapas, **nesta ordem**, cada uma isolada:
   1. Clientes novos
   2. Abertura de caixa (repete até acabar, teto de 20 caixas)
   3. Vendas (uma linha de log por pedido)
   4. Fechamento de caixa (repete, teto de 20)
7. Emite `DadosAlterados` (as telas abertas se recarregam e o "N pendentes" é recontado).

Exceção em qualquer ponto: `Registro.Erro` (arquivo de log) + publica `Offline`. Só `OperationCanceledException` passa.

## Elegível × pendente (duas contagens, dois critérios)

| Contagem | Onde | Critério |
|---|---|---|
| **"Sync: N pendentes"** da barra e do painel | `DashboardService.ContarPendentesAsync` | Tudo o que ainda não chegou à API, **inclusive** item em espera ou que desistiu |
| **O que o ciclo envia agora** | `SincronizacaoBackgroundService.ContarPendentesAsync` | O mesmo, mais `PoliticaRetentativa.Elegivel` (fora de espera e sem ter desistido) |

Por isso o painel pode dizer "3 itens" enquanto o ciclo não envia nada: os 3 estão em espera crescente ou desistiram. Isso é o comportamento certo (senão autenticaria a cada 30 s à toa), mas o operador precisa de uma saída: **"Reenviar falhas"**.

## Retentativa

`PoliticaRetentativa.RegistrarFalha` incrementa `TentativasEnvio` e agenda `ProximaTentativaEm`:

| Tentativa que falhou | Próxima tentativa em |
|---|---|
| 1ª | 30 s |
| 2ª | 1 min |
| 3ª | 2 min |
| 4ª | 4 min |
| 5ª | 8 min |
| 6ª e 7ª | 10 min (teto) |
| **8ª** | **desiste**: `ProximaTentativaEm = null`, e `UltimoErroSync` ganha o aviso "parou de tentar após 8 tentativas — use "Reenviar falhas" para tentar de novo" |

Total ≈ 35 min de insistência: tempo para uma queda passar sem virar loop eterno.

- **Persistido no banco**, não na memória: reabrir o app não "perdoa" um erro permanente.
- `Zerar` roda em qualquer sucesso e no "Reenviar falhas".
- Enviar **um** item explicitamente (por id) ignora a espera, mas conta a tentativa.
- Espera por **dependência** não passa por aqui: não conta tentativa.

## "Reenviar falhas" e descarte

| Ação | Quem | O que faz |
|---|---|---|
| Reenviar falhas (Pedidos) | Qualquer operador | Zera contador e espera das vendas em `FalhaSync` do caixa e do próprio caixa, se o envio dele falhou |
| Reenviar falhas (Cadastros) | Qualquer operador | O mesmo para clientes que falharam ou desistiram |
| Descartar venda | Venda **em falha** + motivo (obrigatório, até 300 caracteres) | `SyncStatus = Descartada`; grava quem pediu, quando e por quê; nunca é enviada nem apagada |

A chave de supervisor no descarte está **desligada por decisão do usuário** (`PoliticaSupervisor.ExigirChave = false`); só religue se ele pedir. Sem a chave, a trilha registra só quem **pediu**.

O caixa **não fecha** com venda não enviada (`VendaLocalService.ContarNaoEnviadasAsync`, que ignora a descartada). A mensagem manda usar "Reenviar falhas" em Pedidos.

## Diagnóstico: "por que este item está parado?"

Faça sempre numa **cópia** do banco (`pdv.db`, `pdv.db-wal`, `pdv.db-shm`) e nunca imprima dados pessoais, token nem segredo. Colunas: `SyncStatus` é **texto** (`PendenteSync`, `FalhaSync`, `Sincronizado`, `Descartada`); tabelas `Vendas`, `Caixas`, `Clientes`.

```sql
-- panorama da fila de vendas
SELECT SyncStatus, COUNT(*) FROM Vendas GROUP BY SyncStatus;

-- o que está parado e por quê (sem dado pessoal)
SELECT NumeroPedido, SyncStatus, TentativasEnvio, ProximaTentativaEm, UltimoErroSync
FROM Vendas
WHERE SyncStatus IN ('PendenteSync','FalhaSync')
ORDER BY NumeroPedido;
```

Leitura das colunas:

| O que aparece | Significa | O que fazer |
|---|---|---|
| `PendenteSync`, `TentativasEnvio` 0, `UltimoErroSync` com texto | Espera por dependência (o texto diz qual) | Sincronizar o catálogo; conferir se o produto/cliente/forma tem `IdExterno` |
| `FalhaSync`, `ProximaTentativaEm` no futuro | Em espera crescente | Esperar, ou "Reenviar falhas" |
| `FalhaSync`, `TentativasEnvio` = 8 | Desistiu | Corrigir a causa (a mensagem tem o nome do campo que a API recusou) e "Reenviar falhas" |
| `PendenteSync` sem erro e app `Offline` | Sem rede/credencial/HTTPS | A dica da pílula de conexão diz qual |
| `Sincronizado` mas a API não mostra a venda | `409` tratado como sucesso ou `VendaIdExterno` nulo | Conferir `VendaIdExterno`; comparar o `guid` |

Tempo de UTC: `ProximaTentativaEm` é **UTC**.

## O estado de conexão que acompanha a fila

`EstadoConexao`: `Desconhecida` (nenhum ciclo falou com a API ainda), `Online`, `OnlineComFalhas` (autenticou, mas algum recurso do catálogo falhou), `Offline`.

- `Publicar` grava a mensagem **antes** de emitir o estado (quem reage já lê o texto novo) e trava a emissão (um `Subject` não aceita `OnNext` concorrente).
- `Offline` **não** é só "sem internet": é também credencial recusada e URL sem HTTPS. O log usa `faltaDeRede` para escolher o texto ("Conexão perdida…" × "Não foi possível falar com a API (autenticação ou comunicação)…").
- Detecção de queda: `NetworkChange` do Windows (na hora, sem placa ativa) + verificação a cada 15 s (`VerificadorDeConexao`, HEAD com prazo de 5 s; qualquer resposta HTTP = alcançável). Só a **mudança** age: voltou ⇒ roda catálogo e envio na hora; recuperação tenta no máximo 3 vezes.
- Sem internet o ciclo publica `Offline` a cada 30 s, mas `RegistrarMudancaDeEstado` só escreve no log quando o estado **muda** ("caiu às 14:02, voltou às 14:20").

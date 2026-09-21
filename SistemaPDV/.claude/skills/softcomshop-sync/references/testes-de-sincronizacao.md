# Testes de sincronização

## A lição mais cara do projeto

Cinco tarefas ficaram "verdes" e a sincronização **nunca funcionou de verdade**. O `FakeHttpMessageHandler` respondia **qualquer URL** com o JSON que o teste mandasse, então os DTOs eram testados contra o formato que a equipe **supunha**, nunca contra o que a API entrega. Ao rodar contra a API real: 5 de 7 rotas sem prefixo, formas de pagamento dentro de um array, booleanos e números em formatos diferentes, `pdv_key` que era bcrypt.

**Regra:** um teste de sincronização só vale se o servidor falso **se comporta como o real**.

## Servidor falso realista

- **Rota errada = 500** (`{"error":""}`), como na API. Só responde bem no caminho exato.
- Respostas com a **forma real** (envelopes, tipos-como-texto, `null`s, array embrulhado), copiadas de uma resposta real com **valores inventados** (nada de dado pessoal de verdade nos testes).
- Um `HttpClient` de teste: `FakeHttpMessageHandler.CriarHttpClient(requisicao => resposta)`. O teste registra o método+caminho de cada requisição para provar **onde** e **em que ordem** o app chamou.
- Um servidor que se **liga e desliga** (lança `HttpRequestException` quando "sem internet") para testar queda/volta.

## O que testar em cada recurso

| Caso | Por quê |
|---|---|
| Formato real lido corretamente (inclusive tipos-como-texto e zero à esquerda) | Evita o "verde falso" |
| Rota exata chamada (`Assert.Equal(new[]{"softauth/api/v2/…"}, visitadas)`) | Pega prefixo/`page` errados |
| Paginação: várias páginas encadeadas; mesma `next_page_url` repetida (para em poucas chamadas); página fora do intervalo | Laço infinito e 500 |
| Registro repetido entre páginas não quebra o índice único | Dedupe |
| Segunda rodada igual ⇒ "mudou 0" | Não recarregar telas à toa |
| Falha no meio da busca **não apaga** o que já existe; resposta vazia de quem já tinha dados também não | Guardas da substituição |
| `HTTP 200` com HTML ⇒ falha tratada, sem exceção | Portal cativo/proxy |
| Um item falha (ou lança), os outros seguem | Lote isolado |
| Dependência sem `IdExterno` ⇒ espera **sem** gastar tentativa e **com** motivo | Espera ≠ falha |
| `409`: venda/abertura = sucesso; cliente = falha | Premissa diferente |
| Retentativa: espera 30 s→…→10 min, desiste na 8ª, "Reenviar falhas" zera, sucesso zera | Loop eterno |
| Ordem do ciclo: cliente → abertura → vendas → fechamento | Fechar antes das vendas |
| URL sem HTTPS ⇒ nada sai, item não vira falha, segredo nem é descriptografado | Segurança |
| Token/segredo **nunca** aparece em log/mensagem/JSON exibido | Vazamento |
| Corpo enviado × corpo exibido no modal (comparação campo a campo) | Fonte única |
| Migrations × modelo (`HasPendingModelChanges` depois de `Migrate()`) | Migration feita à mão pode divergir |

## Regras de higiene

- **Espere o sinal de saída, não a operação de entrada**: reações desacopladas (`Subscribe` fire-and-forget) só se testam aguardando o efeito (um `Task` que completa quando o valor esperado aparece), com timeout.
- **Nunca leia um valor que uma tarefa de segundo plano vai mudar sem esperar essa tarefa** — teste que passa sozinho e falha na suíte inteira é bug de teste, não "azar". Rode a suíte várias vezes seguidas antes de commitar.
- SQLite `:memory:` compartilhado: evite trabalho de banco "fire-and-forget" dentro do teste (o banco some quando o teste acaba).
- Relógio: injete `TimeProvider` (nos testes de retentativa e log) em vez de `Task.Delay`.
- Ao confirmar contra a API real, faça com **cópia do banco e só GET** — testes automáticos **nunca** falam com a API de verdade.
- Cuidado com a cadeia `build | grep && commit`: o `grep` esconde o código de saída do build. Confira o **código de saída** do build antes de commitar.

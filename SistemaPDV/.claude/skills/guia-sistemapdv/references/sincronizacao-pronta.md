# Sincronização pronta: o que chamar e o que enviar, fase por fase

A parte que mais custou tempo neste projeto foi **descobrir** o que a API exige. Aqui está a resposta já mastigada, na ordem em que você vai precisar. Os campos completos, os formatos e as pegadinhas estão em `../../softcomshop-sync/references/api-real.md` (a fonte única — abra a seção indicada em cada passo).

**Regra de ouro:** não descubra, consulte. Se um corpo de POST der 422/500 e ele não estiver descrito lá, aí sim investigue — e depois **acrescente** ao `api-real.md`.

## A sequência de chamadas

| Quando | Chamada | O que sai / entra | Onde ver os campos |
|---|---|---|---|
| 1. Vincular | `GET <link>&device_id=…` | recebe o `client_secret` (guardar protegido) | api-real §1 |
| 2. Sempre, com o segredo | `POST /softauth/authentication/token` (form) | recebe o token → `Authorization: Bearer` + `Api-Version: v2` em tudo | §1 |
| 3. Catálogo (a cada 5 min) | `GET` formas de pagamento, funcionários, empresa, clientes, produtos, grupos, cartões | grava local (upsert por `id`) | §2, §3, §5 |
| 4. Abrir caixa | `POST …/caixa-funcoes/abrir` | 5 campos; `409` = já aberto | §6 |
| 5. Cliente novo | `POST …/clientes/clientes` | `razao_social` sempre; contato e endereço opcionais | §6 |
| 6. Produto novo | `POST …/produtos/produtos` | um por requisição; **preço/estoque ficam zerados** (limitação) | §6 |
| 7. Venda | `POST …/vendas` | `guid`, ids do produto (dois), pagamentos | §6 |
| 8. Fechar caixa | `POST …/caixa-funcoes/fechar` | por chave natural + apuração | §6 |
| Só leitura | `GET …/caixa-funcoes` | lista os caixas da empresa | §6 |

**Ordem de envio dentro de um ciclo da fila (30 s):** clientes novos → produtos novos → abrir caixa → vendas → fechar caixa. Motivo: a venda referencia o cliente, o produto e o caixa; o fechamento resume as vendas.

## O que construir em cada fase (sincronização)

**Fase 2 — catálogo.** Cliente HTTP único (`SoftcomApiClient`), token, e os `GET` paginados (`per_page=200`, `ultima_sincronizacao`). Faça já: HTTPS obrigatório, dedupe por `id`, guarda contra paginação em laço, `200` sem JSON tratado como falha. Tipos: números como **texto**, códigos com zero à esquerda como **texto**, booleanos aceitando `true/false` e `0/1`. Funcionário: guarde o **hash bcrypt** como veio. Empresa: grave **só a do dispositivo** (o CNPJ está no link de vínculo).

**Fase 3 — caixa.** `abrir`: `data_caixa` (`yyyy-MM-dd`), `operador_id`, `turno`, `usuario_abertura_id`, `troco_inicial` **como texto** com ponto (`"10.00"`). `fechar`: chave natural (`data_caixa` + `turno` + `operador_id`), `usuario_fechamento_id`, `troco_final`, `digitacao` (**id da API** da forma de pagamento) e `digitacao_bandeiras` (**nome** da bandeira). Abrir e fechar têm estados **separados**.

**Fase 4 — vendas.** Cabeçalho: `guid` (idempotência, gerado no app), `data_hora` (unix em **segundos**), `empresa_id`, `usuario_id`/`funcionario_id`, `cliente_id`, `numero_documento` (**único por empresa**; use prefixo por dispositivo: `01-000045`), `cancelada:false`, `bloqueada:false`, dados do caixa. Item: **dois ids do produto** (`produto_id` e `produto_empresa_grade_id`), `preco`, **`preco_compra` (obrigatório)**, `quantidade` (pode ser fracionada), descontos e comissões zerados. Pagamento: `forma_pagamento_id` (da API), `api_nome_pagamento`, `api_codigo_pagamento`, `valor_pagamento`, `valor_recebido` (hoje igual ao valor), `parcelas:1`. Se a venda depende de algo que ainda não subiu (produto, cliente, caixa), ela **espera com motivo visível**, não falha.

**Fase 5 em diante — cadastros.** Cliente: `razao_social` sempre + padrões explícitos; endereço com `c_cidade` = **código IBGE** (o ViaCEP devolve como `ibge`; **não** confunda com o `cidade_id` interno). Produto: lote de **um**, com `grupo_id` de um grupo existente; não mande custo/margem zerados. Guarde os ids que a resposta devolve (`data.id` no cliente; `created[].id` e `produto_empresas[].produto_empresa_grade.id` no produto).

## Como tratar as respostas (vale para todos os envios)
- **Sucesso:** grave o id devolvido e marque `Sincronizado`. Nunca confie cegamente no id (0/negativo/ausente = falha).
- **`409`:** significa coisas diferentes por rota (abrir caixa: "já existe"; venda: veja `arquitetura-outbox.md`). Não trate como sucesso por reflexo.
- **`422`/`500`:** o corpo do erro nomeia o campo. Extraia, **trunque em 300 caracteres** e mostre na linha do item (o operador precisa ver o motivo).
- **Falha de rede:** o item fica pendente e é tentado de novo com **espera crescente** (30 s, 1, 2, 4, 8, 10 min; desiste após 8 tentativas até alguém usar "Reenviar falhas"). O estado e o contador ficam **no banco**, não na memória.
- **Um item com erro não trava o lote.**

## O que ainda não está resolvido (avise quem for construir)
- Preço e estoque do produto criado pelo PDV chegam **zerados** na empresa; falta o endpoint de atualização.
- O bairro do cliente chega à API mas a **tela** do SoftcomShop não o mostra.
- O `valor_recebido`/troco ainda não foi confirmado com venda real.
- A chave de supervisor (descartar venda e abrir Configurações) está **desligada**; não se sabe onde obtê-la no SoftcomShop.

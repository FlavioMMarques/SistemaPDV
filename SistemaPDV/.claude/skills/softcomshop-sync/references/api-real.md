# A API SoftcomShop REAL — o que o Swagger não conta

Tudo aqui foi **verificado contra a API de verdade** (leitura numa cópia do banco; os POST só com autorização de quem é dono dos dados). Onde algo ainda é hipótese, está marcado **[hipótese]**. Valores de exemplo são inventados: **nunca cole aqui dado real** (host, client_id, tokens, CPF, telefone).

O domínio (`https://<dominio>`) **muda por cliente/dispositivo**: vem do link de vínculo. Só o caminho de cada rota é fixo.

## 1. Vínculo do dispositivo e token

1. O SoftcomShop gera um **link de vínculo** com `client_id`, `empresa_name` e `empresa_cnpj` na query string.
2. `GET <link>&device_id=<nome do dispositivo>` → `{"data":{"client_secret":"…"}}`.
3. O `client_secret` é guardado **protegido** (DPAPI do Windows, amarrado ao usuário do Windows — `SegredoProtector`), nunca em claro.
4. Token: `POST <dominio>/softauth/authentication/token` (corpo `application/x-www-form-urlencoded`: `grant_type=client_credentials`, `client_id`, `client_secret`) → `{"data":{"token":"…"}}` (o código também aceita `access_token`).
5. Todas as chamadas seguintes levam `Authorization: Bearer <token>` e `Api-Version: v2`.
6. Antes de qualquer envio com credencial ou dado pessoal: **HTTPS obrigatório** (exceto loopback). `ConexaoSegura.Permitida` decide pelo `Uri.IsLoopback`, nunca por "a string começa com localhost" (`http://localhost.evil.com` não é loopback).

## 2. Rotas (relativas ao domínio)

| Recurso | Método e caminho |
|---|---|
| Formas de pagamento | `GET softauth/api/v2/financeiro/forma-pagamento/page/1` |
| Clientes | `GET softauth/api/v2/clientes/clientes` · `POST` no mesmo caminho para criar |
| Produtos | `GET softauth/api/v2/produtos/produtos` |
| Grupos (categorias) | `GET softauth/api/v2/produtos/grupos` — **sem** `/page/N` (com ele dá 500) |
| Funcionários | `GET softauth/api/v2/funcionarios` |
| Empresa | `GET softauth/api/v2/empresa/empresas/1` |
| Cartões/bandeiras | `GET softauth/api/financeiros/cartoes/page/{n}` — **sem `v2`** (exceção!) |
| Abrir caixa | `POST softauth/api/v2/financeiro/caixa-funcoes/abrir` |
| Fechar caixa | `POST softauth/api/v2/financeiro/caixa-funcoes/fechar` |
| Venda | `POST softauth/api/v2/vendas` |
| Token | `POST softauth/authentication/token` (sem `api/v2`) |

**Rota errada = `HTTP 500` com `{"error":""}`** (ou "Resource not found."), **não 404**. Se uma rota "deveria existir" e dá 500, teste com/sem `v2` e com/sem `/page/N` antes de concluir que ela não existe. A dica que achou a rota dos cartões: olhar o caminho do **token**, que também não tem `api/v2`.

## 3. Formas de resposta (leitura)

- **Envelope padrão de página:** `{current_page, data:[…], next_page_url, per_page, total, date_sync, …}`. Vale para clientes, produtos, funcionários, empresa, grupos.
- **Formas de pagamento** vêm **embrulhadas num array**: `[ {current_page, data:[…]} ]`.
- **Cartões** têm **outro envelope**: `{code, message, human, data:[…], meta:{page:{current, prev, next, count}}, date_sync}`, sem `current_page`/`next_page_url`. Página **fora do intervalo devolve `500 {"error":"Invalid pagination interval."}`**, não lista vazia.
- **Paginação:** `?per_page=200`. Incremental: `&ultima_sincronizacao=<unix>` (o `date_sync` da resposta anterior). A mesma linha **pode repetir em páginas diferentes** (dedupe por `id`). Uma API que devolve a mesma `next_page_url` para sempre prende o app num laço: guarde as URLs visitadas e pare ao repetir.
- **`next_page_url` absoluto** nunca pode apontar para outro domínio (senão o token vazaria).
- **Um `HTTP 200` não garante JSON:** portal cativo, proxy e gateway devolvem `200` com HTML. `Deserialize` lança `JsonException`: converta "não é o JSON esperado" em falha tratada (`SoftcomJson.TentarDesserializar`).

## 4. Tipos e valores que enganam

- **Números vêm como texto** em vários recursos (`estoque`, preços, `peso`, `ordem`, e nos cartões **tudo**): use `JsonNumberHandling.AllowReadingFromString`.
- **Códigos com zero à esquerda são texto** (`bandeira_id: "02"`): com `int` viraria `2`.
- **Booleanos** (`vender`, `bloqueado`) chegam como `true/false` de verdade, embora a documentação diga `0/1` — aceite os dois (`BooleanoFlexivelConverter`).
- Muitos campos da empresa vêm `null` onde o esperado era inteiro.
- **Envio de número:** use `CultureInfo.InvariantCulture`. Em Windows pt-BR, `ToString("F2")` produz `"10,00"` e o servidor quebra. Alguns campos esperam **texto** (`troco_inicial: "10.00"`).

## 5. Dados de referência que têm pegadinha

- **Funcionários:** `usuario.pdv_key` é hash **bcrypt** (`$2y$10$…`, 60 caracteres), não a chave. Guarde o hash como veio e confira a chave digitada com bcrypt. Valor sem cara de bcrypt é **ignorado**, nunca gravado. Nem todo funcionário tem chave.
- **Empresa:** a API devolve **todas** as empresas do cliente (trazendo até certificado digital e senha). A do dispositivo é a do **`empresa_cnpj` do link de vínculo**. Grave **só** essa; "a primeira da lista" acerta por sorte.
- **Produto tem três ids:** `id` (item da listagem), `produto_id` (produto-base) e `produto_empresa_id`. Na venda: `produto_empresa_grade_id` = `id` da listagem e `produto_id` = `produto_id`. Mandar o `id` como `produto_id` registra **outro produto**. Guarde os dois.
- **Cliente "Consumidor Final":** é o cliente com `indicador_finalidade = 1` (na API vista, id 1). Deduza pelo indicador; não exija configuração manual.
- **Grupo (categoria):** produto → `grupo_id` → grupo `id`/`nome` (sem hierarquia nos dados vistos). Todos os `grupo_id` de produtos resolveram.

## 6. Escrita (corpo que a API EXIGE — descoberto pelos 422/500)

O Swagger lista ~100 campos, quase todos "opcionais". O que a API realmente exige só aparece quando ela recusa. Mantenha o **nome do campo** na mensagem de erro (`numero_documento: É obrigatório.`).

### Abrir caixa
`data_caixa` (`yyyy-MM-dd`), `operador_id`, `turno`, `usuario_abertura_id`, `troco_inicial` (texto `"10.00"`). Resposta: `data.success.id`. `409` = já existe caixa aberto para data/operador/turno ⇒ tratar como aberto (a resposta **não traz id**).

### Fechar caixa
Identifica o caixa pela **chave natural** (`data_caixa` + `turno` + `operador_id`), não por id. Campos: `data_caixa`, `turno`, `operador_id`, `usuario_fechamento_id`, `troco_final` (texto, invariante) e a apuração: `digitacao` (por forma de pagamento: **id da API** da forma, não o local) e `digitacao_bandeiras` (`[{"bandeira":"MASTERCARD","valor":40.0}]` — o **nome** da bandeira, não o id; aceito). O fechamento **resume o caixa**: as vendas têm de ter chegado antes.

### Venda
Cabeçalho: `guid` (chave de idempotência gerada no app), `data_hora` (**unix em segundos**, inteiro), `empresa_id`, `usuario_id` e `funcionario_id` (ambos o id do funcionário na API), `cliente_id`, `numero_documento`, `cancelada:false`, `bloqueada:false`, `caixa_data`, `caixa_turno`, `caixa_funcoes_id`.
Cada item: `produto_id`, `produto_empresa_grade_id`, `preco`, `preco_compra` (**obrigatório**: sem ele "Column cannot be null"), `quantidade`, `desconto_valor_item`, `acrescimo_valor_item`, `comissao`, `comissao_atendente`, `percentual_comissao_venda`, `composicao_automatica`, `promocao_aplicada`.
Cada pagamento: `forma_pagamento_id` (id da API), `api_nome_pagamento`, `api_codigo_pagamento` (código NFC-e da forma), `valor_pagamento`, `valor_parcela`, `valor_recebido`, `parcelas` (1 à vista), `numero_parcela` (`"1"`).
Resposta de sucesso: `data.id`.

- **Quantidade fracionada é aceita** (venda por peso), embora o Swagger diga inteiro.
- **`numero_documento` é único por empresa**, e cada PDV gera o seu offline. Sem cuidado, dois PDVs colidem já na 1ª venda. Solução adotada: **prefixo por dispositivo** (`01-000045`; texto aceito pela API). O hífen é proibido no código do PDV (é o separador). O app não tem como saber que dois dispositivos usam o mesmo código: a unicidade depende de quem configura.
- A **bandeira do cartão não vai na venda**; só o fechamento a usa.
- **[hipótese]** `valor_recebido` diferente de `valor_pagamento` (troco) ainda **não foi confirmado** com venda real: hoje vai igual ao valor.

### Criar cliente
`pessoa` (`FISICA`/`JURIDICA`), `nome`, `cpf_cnpj` (só dígitos), `razao_social` (obrigatória se jurídica), `contribuinte_icms` (9 = não contribuinte), `indicador_finalidade` (0 normal; 1 é o Consumidor Final). Campos nulos são **omitidos**. Resposta: `data.id`. `409` **não** traz o id (veja `arquitetura-outbox.md`).

## 7. Erros

Formato mais comum: `{"errors":{"campo":["mensagem"], "message":["geral"]}}`. Um `500` com texto de PHP (`Undefined index: x`, `Column 'y' cannot be null`) **nomeia o próximo campo que o servidor lê**: as tentativas falhas não deixaram venda pela metade, o que permite iterar — mas só com autorização (é escrita).

Trate o corpo do erro como **não confiável**: pode ser HTML ou um stack trace. Extraia, **trunque (300)** e nunca repita o corpo inteiro em banco ou tela.

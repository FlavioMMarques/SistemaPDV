# Capability Map: SistemaPDV

PDV (ponto de venda) offline-first em C#/.NET 8 + Avalonia + MVVM, com SQLite local (EF Core 8) sincronizando com a API SoftcomShop. Projeto de curso (aprendizado de OOP/MVVM) — ver `SPEC-*.md` de cada módulo para o Specify completo.

| Module id | Responsabilidade | Depende de |
|---|---|---|
| `data-layer` | DbContext (EF Core 8 + Sqlite), entidades locais, migrations | — |
| `catalog-sync` | Pull de produtos, clientes, formas de pagamento, empresa e funcionários (incl. `pdv_key`) da API SoftcomShop | `data-layer` |
| `caixa` | Login local do operador (via `pdv_key` sincronizado), abertura/fechamento de caixa | `data-layer`, `catalog-sync` |
| `sales` | Registrar venda local, outbox de sincronização, envio para a API com rastreamento de status | `data-layer`, `catalog-sync`, `caixa` |
| `pdv-ui` | Telas MVVM: login, dashboard, PDV (venda), listagem de pedidos, cadastros | `catalog-sync`, `caixa`, `sales` |

**Ordem de build:** `data-layer` → `catalog-sync` → `caixa` → `sales` → `pdv-ui`

## Reativado depois de adiado

- **Apuração por bandeira de cartão no fechamento de caixa** — adiada em 2026-09-20 ("só o valor total") e **reativada no mesmo dia** pelo usuário depois que a rota de cartões do SoftcomShop foi achada (`GET softauth/api/financeiros/cartoes/page/N`, sem `v2`): sincroniza os cartões, o operador escolhe a bandeira no pagamento em cartão e o fechamento apura por bandeira. Ver `tasks/todo.md` (Fase 6) e APRENDIZADOS #71.

## Fora de escopo (adiado)

- **Sangria / Suprimento** (`POST /api/v2/financeiro/sangria` e `/suprimento`) — endpoints já documentados no contrato da API, mas o fluxo de UI/domínio fica para depois do protótipo inicial.
- **Emissão de NFC-e** — os campos fiscais da empresa (`empresa_certificado`, config de NFC-e) são sincronizados por completude, mas a emissão em si não faz parte do escopo do protótipo. **Decisão do usuário (2026-09-20): o app não vai focar em emissão de NFC-e por enquanto** — só sincroniza os dados fiscais; nada da emissão entra no backlog até o usuário reabrir o assunto.

## Contrato externo (referência)

O contrato completo da API SoftcomShop (autenticação OAuth2 client_credentials, endpoints, formatos de request/response, envelopes de erro) está fora deste repositório de specs — foi levantado e vive na memória do agente (`softcomshop-api-contract`). Cada `SPEC-*.md` abaixo reproduz apenas os trechos relevantes ao seu módulo.

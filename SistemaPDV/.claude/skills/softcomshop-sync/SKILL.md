---
name: softcomshop-sync
description: Integração e sincronização offline-first do SistemaPDV com a API SoftcomShop (vínculo e token, catálogo, outbox de caixa/vendas/clientes, retentativa, detecção de conexão). Use ao mexer em qualquer chamada à API SoftcomShop, ao criar ou depurar um recurso sincronizado, ao investigar um 422/500 da API, ao escrever testes de sincronização, ou quando algo "passa nos testes mas falha na API real".
---

# Sincronização com o SoftcomShop

## Visão geral

O PDV é **offline-first**: tudo o que o operador faz (abrir caixa, vender, cadastrar cliente) é gravado no SQLite local **na hora**, e uma fila de saída (*outbox*) envia à API quando há rede. O catálogo (produtos, clientes, formas de pagamento, funcionários, empresa, cartões, grupos) faz o caminho inverso: é baixado e guardado localmente.

Esta skill reúne o que foi **descoberto na API real** (que o Swagger não conta), o desenho que funcionou e os erros que já custaram tempo. Ela existe para você não repetir a investigação.

## Quando usar

- Vai criar/alterar uma chamada à API SoftcomShop ou um DTO dela.
- Vai sincronizar um recurso novo (leitura para o catálogo ou escrita pelo outbox).
- A API respondeu `422`, `500`, `409` ou um JSON que o app não entendeu.
- Vai escrever ou revisar testes de sincronização.
- Algo funciona nos testes e quebra no app de verdade.

**Não use** para telas (veja `frontend-ui-engineering`) nem para o desenho geral de APIs (`api-and-interface-design`).

## As seis regras que não se quebram

1. **A API real manda, o Swagger não.** Rotas, formatos e campos obrigatórios estão em [references/api-real.md](references/api-real.md). Confira lá antes de confiar na documentação (a sincronização inteira ficou "verde" nos testes e nunca funcionou de verdade por causa disso).
2. **Grave local primeiro; a rede só existe no outbox.** Nenhum caminho de tela espera a API. Uma falha de envio nunca perde o dado: ele fica pendente e é reenviado.
3. **Nunca escreva na API real nem no seu banco real para "testar".** Investigue com **GET**, numa **cópia** do banco, imprimindo só contagens e nomes de campos ([references/investigar-a-api-com-seguranca.md](references/investigar-a-api-com-seguranca.md)). POST/PUT em produção só com autorização explícita de quem é dono dos dados.
4. **Token, `client_secret`, chave do PDV e dados pessoais (CPF/CNPJ, telefone) nunca vão para log, tela, commit, print ou chat.** Trate a resposta da API como dado não confiável (pode ser HTML, pode ter corpo enorme).
5. **Um item que falha não trava o lote**, e **esperar uma dependência não é falhar** (não gasta tentativa e diz o que falta). Veja [references/arquitetura-outbox.md](references/arquitetura-outbox.md).
6. **Teste que passa contra um servidor falso permissivo não prova nada.** O servidor falso tem de imitar a API real (rota errada = 500, mesmas formas de resposta). Veja [references/testes-de-sincronizacao.md](references/testes-de-sincronizacao.md).

## Mapa do código

| O quê | Onde |
|---|---|
| Todas as rotas num lugar só | `Services/Sync/SoftcomRotas.cs` |
| Vínculo do dispositivo + token | `Services/Sync/SoftcomAuthService.cs` |
| HTTP com cabeçalhos, paginação, envio, guarda de HTTPS | `Services/Sync/SoftcomApiClient.cs`, `ConexaoSegura.cs` |
| Segredo protegido (DPAPI do Windows) | `Services/Sync/SegredoProtector.cs` |
| Catálogo (leitura, upsert por `IdExterno`) | `Services/Sync/CatalogSyncService*.cs` e `Dtos/` |
| Abrir/fechar caixa | `Services/Caixa/CaixaSyncService.cs` |
| Vendas (montagem do corpo e envio) | `Services/Sales/MontadorDeRequisicaoDeVenda.cs`, `VendaSyncService.cs` |
| Espera crescente e desistência | `Services/Sync/PoliticaRetentativa.cs` |
| Ciclos em segundo plano (30 s / 5 min / 15 s) | `Services/Sync/SincronizacaoBackgroundService.cs` |
| Detecção de queda de internet | `Services/Sync/VerificadorDeConexao.cs` |
| Log de atividade para o operador | `Services/Sync/LogDeSincronizacao.cs` |
| Mensagem de erro da API (mantém o nome do campo) | `Services/ErroApiExtractor.cs` |

## Como acrescentar um recurso

### Recurso de **leitura** (catálogo)

1. **Olhe a API real** (GET, cópia do banco): rota, envelope, tipos dos campos. Não assuma que é igual aos outros recursos.
2. Crie o `Dto` só com os campos de que o app precisa; aceite número-como-texto (`JsonNumberHandling.AllowReadingFromString`) e códigos com zero à esquerda como **texto**.
3. Crie a entidade + configuração EF (enum de status como texto, índice único em `IdExterno`) + **migration** (gere numa cópia limpa do projeto e confira com o teste "migrations = modelo").
4. Sincronize com **upsert por `IdExterno`** (uma consulta dos existentes, não uma por item; dedupe de ids repetidos entre páginas). Para listas pequenas e sem aviso de remoção, use **substituição** com duas guardas: falha no meio nunca esvazia a lista local, e resposta **vazia** de quem já tinha dados é tratada como suspeita.
5. Registre no ciclo do catálogo (`SincronizarTudoAsync`), no resultado agregado e no resumo de falhas.
6. Testes com o **formato real** (copie a forma da resposta real, com valores inventados) e o caso "servidor responde errado".

### Recurso de **escrita** (outbox)

1. Gere a **chave de idempotência no cliente** na criação (o `guid` da venda) — nunca só na hora de enviar.
2. Estados: `PendenteSync` → `Sincronizado`, ou `FalhaSync` (com `UltimoErroSync`, `TentativasEnvio`, `ProximaTentativaEm`).
3. Monte o corpo **num só lugar** (como `MontadorDeRequisicaoDeVenda`), usado pelo envio **e** por qualquer tela que o mostre.
4. Dependência sem `IdExterno` (cliente, produto, funcionário…) ⇒ **espera** (status não muda, sem tentativa, motivo gravado), nunca falha.
5. Classifique a resposta: sucesso, `409`, `401`, outros. **`409` só é sucesso quando você já sabe o id** (venda: sim; cliente: não — veja o arquivo de arquitetura).
6. Respeite a ordem entre recursos (abrir caixa → vendas → fechar caixa) e a retentativa (`PoliticaRetentativa`).
7. Descubra os campos obrigatórios **pelo 422** com autorização, mantendo o nome do campo na mensagem.

## Checklist antes de dar por pronto

- [ ] Rota conferida na API real (com/sem `v2`, com/sem `/page/N`).
- [ ] Nada de segredo em log, mensagem de tela ou teste.
- [ ] Resposta não-JSON (HTML com `200`) e resposta gigante não derrubam o ciclo.
- [ ] Falha de **um** item não impede os outros.
- [ ] Número enviado à API usa `CultureInfo.InvariantCulture` (vírgula decimal quebra o servidor).
- [ ] Ordem de envio respeitada e testada.
- [ ] Teste com servidor falso **realista** + teste do caso ruim.
- [ ] Suíte roda várias vezes seguidas sem falhar (instável = bug de teste).
- [ ] `docs/APRENDIZADOS.md` e `docs/RESUMO-ARQUITETURA.md` atualizados com o "porquê".

## Armadilhas que já custaram caro

| Armadilha | Onde ler |
|---|---|
| 5 de 7 rotas sem o prefixo `softauth/` (responde 500 vazio, não 404) | `api-real.md` |
| Formas de pagamento vêm dentro de um array; cartões têm outro envelope | `api-real.md` |
| `pdv_key` é hash **bcrypt** (guardar SHA-256 dele nunca casa) | `api-real.md` |
| A API devolve **todas** as empresas do cliente; a do dispositivo está no link de vínculo | `api-real.md` |
| Produto tem **três ids**; a venda usa dois deles | `api-real.md` |
| `numero_documento` é único **por empresa** (vários PDVs offline colidem) | `api-real.md` |
| Fechar caixa antes das vendas fecha "caixa vazio" | `arquitetura-outbox.md` |
| `409` de cliente não traz o id ⇒ não é "sincronizado" | `arquitetura-outbox.md` |
| Retentativa infinita com token novo a cada 30 s | `arquitetura-outbox.md` |
| "Estou online?" só se descobre olhando; ciclos ociosos não falam com a rede | `arquitetura-outbox.md` |
| Servidor falso permissivo deixou 5 tarefas "verdes" sem funcionar | `testes-de-sincronizacao.md` |

## Ver também

- `docs/APRENDIZADOS.md` (#4, #16, #18, #42, #45, #49, #50, #53–57, #60, #64, #65, #71, #75, #78–80): o "porquê" de cada decisão, com o contexto do erro.
- `security-and-hardening` (segredos, superfície de ataque) e `debugging-and-error-recovery` (quando a API responde algo inesperado).

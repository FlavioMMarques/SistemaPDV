# Roteiro por fases

A ordem em que o projeto foi construído, e que serve para quem vai reconstruí-lo aprendendo. Cada fase depende das anteriores. **Não copie o código pronto:** leia a spec da fase, faça sozinho e compare depois. As tarefas detalhadas e os critérios de aceitação estão em `tasks/todo.md` (procure o título "Fase N"); os porquês, em `docs/APRENDIZADOS.md`.

Em toda fase, a definição de pronto é a mesma: **build sem avisos, testes verdes, e o checkpoint da fase conferido** (o que ele exige está em `tasks/todo.md`, sob "Checkpoint").

**Em toda fase, tenha à mão dois arquivos:** [sincronizacao-pronta.md](sincronizacao-pronta.md) (o que chamar na API e os campos que ela exige, sem precisar descobrir) e [telas.md](telas.md) (o que cada tela deve mostrar e fazer). Nas fases 2 a 4 e nos cadastros, o primeiro é o que mais poupa tempo; da fase 5 em diante, o segundo.

## Fase 1 — `data-layer`: o banco local
- **Objetivo:** entidades e banco SQLite com EF Core 8, configuradas em classes próprias (Fluent API), com migrations.
- **Você aprende:** EF Core (DbContext, migrations), tipos "owned", chave natural × chave substituta, por que `Venda` usa `Guid`.
- **Leia:** `specs/SPEC-data-layer.md`. APRENDIZADOS #1–#12, #23.
- **Cuidado:** um teste com SQLite em memória precisa manter a conexão aberta (#2); chave estrangeira pega bug no teste (#10).
- **Pronto quando:** o `Fase 1 / Checkpoint: data-layer completo` passa.

## Fase 2 — `catalog-sync`: baixar o catálogo
- **Objetivo:** autenticar na API, baixar produtos, clientes, formas de pagamento, empresa e funcionários, e guardar tudo localmente.
- **Você aprende:** OAuth2 client_credentials, paginação, proteção de segredo com DPAPI (`SegredoProtector`), o padrão "um único cliente HTTP" (`SoftcomApiClient`).
- **Leia:** `specs/SPEC-catalog-sync.md`, a skill `softcomshop-sync`. APRENDIZADOS #6, #13, #16, #20, #45, #53–#55.
- **Cuidado (a maior lição do projeto):** o servidor falso dos testes era permissivo demais e a sincronização nunca funcionou de verdade (#53). **Ao terminar esta fase, rode contra a API real** (só leitura, cópia do banco).

## Fase 3 — `caixa`: login e caixa offline-first
- **Objetivo:** login local do operador e abrir/fechar caixa **sem rede**, com envio depois.
- **Você aprende:** o padrão serviço local + serviço de sincronização, resultado tipado em vez de exceção, por que a abertura e o fechamento têm estados separados.
- **Leia:** `specs/SPEC-caixa.md`. APRENDIZADOS #5, #17–#19, #42, #54, #60, #61.
- **Cuidado:** um campo só para dois estados independentes causou 4 bugs (#17). A chave do operador que a API manda é hash **bcrypt** (#54).

## Fase 4 — `sales`: vendas offline-first
- **Objetivo:** registrar a venda local, montar o corpo do POST, enviar, tratar sucesso/conflito/erro.
- **Você aprende:** idempotência com chave gerada no cliente (#4), envio em lote que isola a falha de um item (#18), "esperar dependência não é falhar" (#57).
- **Leia:** `specs/SPEC-sales.md`. APRENDIZADOS #4, #18, #56, #57, #64.
- **Cuidado:** o corpo tem que ser o que a API **exige**, descoberto pelo 422, não o que o Swagger lista (#56). O número do pedido precisa ser único por empresa mesmo com vários PDVs (#64).

## Fase 5 — `pdv-ui`: as telas
- **Objetivo:** login, configuração, abrir caixa, painel, venda, pedidos, cadastros, fechar caixa, e a sincronização automática em segundo plano.
- **Você aprende:** MVVM com ReactiveUI, Avalonia/XAML, composition root (`AppServices`), navegação por um Shell, timers que não travam a tela.
- **Leia:** `specs/SPEC-pdv-ui.md`, a skill `frontend-ui-engineering`, `fila-outbox`. APRENDIZADOS #24–#36, #48–#51, #58, #61, #68.
- **Cuidado:** carregue os dados **antes** de mostrar a tela (#61); exceção não tratada em comando derruba o app (#58); um campo novo sem caminho até ele "não existe" (#68).
- **Pronto quando:** dá para fazer login → abrir caixa → vender **sem rede** → ver a venda pendente → ela subir quando a rede volta.

## Fase 6 — Cartões e bandeiras
- Sincronizar os cartões, escolher a bandeira na venda e apurar por bandeira no fechamento. **Lição:** quando o Swagger erra o caminho, olhe onde a API **já** funciona (#71).

## Fase 7 e 7b — Visual da tela de venda e pagamento misto
- Refazer a tela pelo protótipo (cores, cartões de produto, painel de pagamento) e permitir mais de uma forma de pagamento, com troco. **Lição:** renderize a tela de verdade para conferir (#72); atalhos de teclado dependem de foco (#73); o valor recebido e o troco ainda não vão à API (em espera).

## Fase 8 — Demais telas no visual do protótipo
- Painel, pedidos com filtros, detalhes do pedido, painel lateral da fila, cadastros com abas, avisos (toasts). **Lições:** #74–#80.

## Depois das fases (evolução real do projeto)
Em ordem, cada uma virou um PR:
1. Login escolhendo o operador antes da chave, e o botão de sair (#82, #83).
2. Cadastro de cliente e de produto em modal, com envio pela fila (#84–#87). **Descobertas:** a API exige `razao_social` sempre; o cadastro de produto não grava preço nem estoque por empresa (#86, #89).
3. Tela "Caixas no SoftcomShop", só de leitura (#88).
4. Endereço completo com busca de CEP e o código IBGE da cidade (#90).

O que ficou **em espera** está registrado nesses mesmos itens do `APRENDIZADOS` (preço/estoque do produto, bairro na tela do SoftcomShop, valor recebido e troco, chave de supervisor).

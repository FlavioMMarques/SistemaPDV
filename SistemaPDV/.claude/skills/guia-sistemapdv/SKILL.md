---
name: guia-sistemapdv
description: Guia para quem quer construir, entender ou continuar o SistemaPDV (PDV offline-first em C#/.NET 8 + Avalonia + SQLite/EF Core que sincroniza com a API SoftcomShop) — o mapa do projeto, a ordem das fases, as telas que precisam ser feitas, o que chamar e enviar à API em cada fase (rotas e campos já descobertos), as regras de trabalho, as armadilhas já pagas e um modo de ensinar por tentativa e explicação. Use quando alguém novo vai fazer ou continuar este projeto, quando pedir "por onde começo", "me explique como esse sistema funciona", "quero aprender fazendo", ou quando um agente precisa se situar no repositório antes de mexer.
---

# Guia do SistemaPDV

## Visão geral

O SistemaPDV é um ponto de venda para Windows que **funciona sem internet**: tudo o que o operador faz (abrir caixa, vender, cadastrar cliente e produto) é gravado no banco local na hora, e uma fila envia à API do SoftcomShop quando há rede. O catálogo (produtos, clientes, formas de pagamento, funcionários, empresa, cartões, grupos) faz o caminho inverso.

Esta skill é o **ponto de entrada**: ela não repete o que os outros documentos já dizem, ela diz **o que ler, em que ordem, o que cuidar e como aprender**. Foi escrita para um leitor de nível intermediário: sabe C# e LINQ, mas ainda não conhece EF Core com migrations, MVVM com ReactiveUI, Avalonia/XAML nem o padrão de fila de saída (*outbox*). Esses conceitos são explicados na primeira vez que aparecem, não antes.

## Quando usar

- Alguém vai **construir o projeto do zero** seguindo as fases (para aprender).
- Alguém vai **continuar** de onde parou, ou corrigir/estender uma parte.
- Um agente precisa se situar antes de alterar código.
- Perguntas do tipo "por onde começo?", "por que foi feito assim?", "isso já deu problema antes?".

**Skills irmãs, usadas dentro deste guia:** `softcomshop-sync` (o que a API real exige), `fila-outbox` (a fila de envio), `test-driven-development`, `frontend-ui-engineering` (telas), `git-workflow-and-versioning` (branches e PRs).

## O mapa em uma página

```
data-layer    → banco local (SQLite) e as entidades
catalog-sync  → baixa o catálogo da API
caixa         → login do operador + abrir/fechar caixa
sales         → registrar e enviar vendas
pdv-ui        → telas, navegação e sincronização em segundo plano
```

Cada módulo depende só dos anteriores. Onde está cada coisa:

| Quero saber… | Leia |
|---|---|
| o que cada classe faz | `docs/RESUMO-ARQUITETURA.md` |
| **por que** foi feito assim e o que deu errado | `docs/APRENDIZADOS.md` (90 entradas) |
| a ordem e as tarefas de cada fase | `tasks/plan.md`, `tasks/todo.md` |
| o que cada módulo deve entregar | `specs/SPEC-*.md` e `specs/CAPABILITY-MAP.md` |
| o que a API real faz de diferente do Swagger | skill `softcomshop-sync` |
| a fila de envio | skill `fila-outbox` |
| **o que chamar e enviar à API**, fase por fase (já mastigado) | [references/sincronizacao-pronta.md](references/sincronizacao-pronta.md) |
| **as telas que precisam ser feitas** e o que cada uma faz | [references/telas.md](references/telas.md) |
| os campos completos e as pegadinhas de cada rota | `softcomshop-sync/references/api-real.md` |

Detalhe por fase: [references/roteiro-por-fases.md](references/roteiro-por-fases.md). Preparação do ambiente: [references/ambiente-e-acessos.md](references/ambiente-e-acessos.md).

**Sincronização e telas são as duas coisas em que mais se perde tempo.** Para a sincronização, **não descubra, consulte**: as rotas, os campos que a API exige (que o Swagger não lista) e a ordem dos envios já estão prontos em `sincronizacao-pronta.md`. Para as telas, `telas.md` é a lista do que construir e a definição de pronto de cada uma.

## As regras de trabalho (o que mais custou aprender)

1. **Grave local primeiro; a rede só existe na fila.** Nenhuma tela espera a API. Uma falha de envio nunca perde o dado.
2. **Escreva o teste que falha antes de corrigir.** Vale para bug e para funcionalidade. Veja o teste falhar pelo motivo certo; só então conserte.
3. **"Verde" nos testes não é "funciona".** A sincronização inteira passou em cinco tarefas contra um servidor falso e nunca funcionou de verdade (APRENDIZADOS #53). O que fala com a API só está pronto depois de conferido com a API **real**.
4. **O Swagger mente em detalhes.** Campos "obrigatórios só para X" eram obrigatórios sempre; rotas do Swagger davam 500 (#56, #71, #84). Confie na API real e na cópia do banco.
5. **Nunca escreva na API real nem no seu banco real para "testar".** Investigue com GET, numa **cópia** do banco, imprimindo só nomes de campos e contagens (skill `softcomshop-sync`). POST real só com autorização de quem é dono dos dados.
6. **Segredo não sai do aparelho.** Token, `client_secret`, chave do operador e dados pessoais nunca vão para log, tela, commit, print ou conversa. A resposta da API é dado não confiável.
7. **Uma regra importante vale nas três camadas**: serviço, tela e envio (#62). A tela avisa; o serviço recusa; o envio confere de novo.
8. **Branch e PR para cada mudança.** Nada direto na `main`. Cada PR descreve o que muda, o que foi testado e o que **não** foi (a honestidade sobre o que não foi conferido é parte do trabalho).

## Como começar (primeiros 30 minutos)

1. Prepare o ambiente e rode `dotnet build` e `dotnet test` (tudo deve estar verde) — [ambiente-e-acessos](references/ambiente-e-acessos.md).
2. Leia `docs/RESUMO-ARQUITETURA.md` (o mapa) e a `specs/CAPABILITY-MAP.md`.
3. Escolha o caminho:
   - **Aprender construindo:** siga as fases na ordem, pelo [roteiro](references/roteiro-por-fases.md). Reescreva cada módulo depois de ler a spec dele, sem copiar o código pronto.
   - **Continuar/estender:** leia `tasks/todo.md` (o que está feito e o que está em espera), escolha uma tarefa e siga o ciclo abaixo.

## O ciclo de cada tarefa

1. **Entenda o porquê** em duas ou três frases (a spec e o `APRENDIZADOS` respondem).
2. **Escreva o teste primeiro** e veja-o falhar.
3. **Implemente o mínimo** para passar; depois rode a suíte inteira (um teste que passa sozinho e falha na suíte é bug de teste, #38).
4. **Se tocar a API:** confira contra a API real, com cuidado (regras 3 a 5).
5. **Se tocar a tela:** rode o app e olhe; a compilação não prova que o XAML faz o que você pensa (#72).
6. **Registre**: uma entrada nova em `docs/APRENDIZADOS.md` quando algo surpreendeu, e atualize `docs/RESUMO-ARQUITETURA.md`.
7. **Abra o PR** com o que muda, como conferir e o que ficou sem verificar.

## Ensinar (o modo "meio termo")

Quem aprende tenta primeiro; o guia ajuda em degraus (pista → esqueleto → solução), explica o **porquê** depois, e aponta a armadilha correspondente. O passo a passo, com exemplos de perguntas e do nível de ajuda em cada momento, está em [references/como-ensinar.md](references/como-ensinar.md).

## Armadilhas já pagas

Antes de mexer numa área, olhe o índice em [references/armadilhas.md](references/armadilhas.md): ele diz **em qual entrada do `APRENDIZADOS`** está cada problema (banco e migrations, testes, MVVM/Avalonia, sincronização, números e dinheiro, segurança, processo).

## Sinais de que algo saiu do trilho

- "Está tudo verde, então está pronto" sem ter chamado a API real.
- Uma tela ou campo novo que ninguém consegue abrir (#68).
- Um POST de teste na API de verdade.
- Lógica de negócio dentro do code-behind da tela.
- Um `try/catch` que engole erro em silêncio, ou um log com token ou CPF.
- Uma regra "temporariamente" apagada em vez de desligada por um interruptor testado (#69).
- Um PR sem a lista do que **não** foi testado.

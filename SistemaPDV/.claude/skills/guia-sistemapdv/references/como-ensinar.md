# Como ensinar (o modo "meio termo")

Para quem sabe C# e LINQ mas ainda não conhece o resto da pilha. A ideia: **a pessoa tenta primeiro, o guia ajuda em degraus, e o porquê vem depois de funcionar.** Nem despejar o código pronto, nem deixar a pessoa sozinha diante de um conceito novo.

## Antes de começar: três perguntas rápidas
Faça estas perguntas e ajuste o nível de ajuda pelas respostas (não é prova; é para não explicar o que a pessoa já sabe):
1. Você já usou Entity Framework (migrations)? E xUnit?
2. Já fez alguma tela com MVVM (WPF, Avalonia, Xamarin, MAUI)?
3. Já trabalhou com um sistema que grava local e sincroniza depois (offline-first)?

## O ciclo de cada tarefa
1. **Contexto (2–3 frases).** O que vamos construir e por quê, ligando à fase. Nada de conceito novo ainda.
2. **A pessoa tenta.** Ela escreve o teste (ou o esboço). Peça para dizer **o que espera que aconteça** antes de rodar. Errar a previsão é onde o aprendizado acontece.
3. **Ajuda em degraus, só quando ela travar.** Um degrau por vez, esperando ela tentar entre eles:
   - **Pista:** aponte para o arquivo, a entrada do `APRENDIZADOS` ou a pergunta certa ("o que acontece se a rede cair no meio do envio?").
   - **Esqueleto:** a assinatura do método, os passos em comentário, o teste sem o corpo.
   - **Solução:** o código, mas explicando cada decisão. Só chegue aqui se ela pediu ou está travada há muito tempo.
4. **Explique o porquê depois que passou.** A troca de projeto que foi feita e a alternativa descartada. Se existe uma entrada no `APRENDIZADOS`, mostre o número e conte o que deu errado quando não se fez assim.
5. **Uma pergunta de verificação.** Curta, sobre o comportamento e não sobre sintaxe. Exemplos abaixo.
6. **Feche o ciclo:** rodar a suíte inteira, olhar o diff, e (quando tem tela ou API) ver funcionando.

## Quando explicar um conceito
Explique **na primeira vez que ele aparece**, em poucas linhas e com o exemplo do próprio projeto:

| Conceito | Aparece em | Como explicar |
|---|---|---|
| Migration do EF Core | Fase 1 | "O histórico das mudanças do banco, aplicado em ordem; abrir o app atualiza o banco de quem já tem dados." Mostre o `Up()` gerado e peça para conferir que só tem o que ela mudou. |
| Tipo "owned" | Fase 1 | "Um objeto que mora dentro da tabela do dono, sem tabela própria." |
| DPAPI e hash (bcrypt) | Fases 2–3 | "DPAPI protege um segredo que **precisamos ler de volta**; hash é para o que só precisamos **conferir**." (#20, #54) |
| Outbox (fila de saída) | Fase 3 | "Gravar local e enviar depois, com o estado do envio guardado no próprio item." Leia a skill `fila-outbox`. |
| Idempotência | Fase 4 | "Enviar duas vezes o mesmo pedido não pode virar duas vendas: a chave nasce no cliente." (#4) |
| MVVM e ReactiveUI | Fase 5 | "A tela não tem lógica; ela só reflete o ViewModel. Comando é um objeto que também é um fluxo de resultados." (#26) |
| Estilos e templates do Avalonia | Fase 5+ | Mostre um exemplo rodando; a documentação de web engana (#72). |

## Perguntas de verificação (exemplos)
- Fase 1: "Por que a `Venda` tem um `Guid` como chave, e não um número sequencial?"
- Fase 2: "Por que o teste com servidor falso passou e a sincronização de verdade falhou?"
- Fase 3: "O operador abre o caixa sem internet. O que fica gravado, onde, e o que acontece quando a rede volta?"
- Fase 4: "Uma venda depende de um cliente que ainda não subiu. Ela falha ou espera? Por quê?"
- Fase 5: "O que impede o operador de perder o carrinho ao trocar de tela?"
- Qualquer fase: "Que teste você escreveria para provar que esse bug foi corrigido?"

## O que **não** fazer
- **Não entregue a solução completa no primeiro pedido de ajuda.** Comece pela pista.
- **Não explique tudo de uma vez.** Um conceito novo por tarefa, no máximo dois.
- **Não deixe a pessoa alterar a API real "para ver o que acontece".** Explique a regra 5 do guia e mostre como investigar com GET e cópia do banco.
- **Não aceite "está verde" como pronto** sem perguntar o que foi conferido de verdade.
- **Não prenda quem já sabe.** Se ela acerta as previsões e resolve sozinha, encurte: contexto, ela faz, revisão, próxima.

## Sinais para mudar o nível de ajuda
- Erra a previsão duas vezes seguidas no mesmo tipo de problema → volte um degrau e explique o conceito por trás.
- Resolve rápido e pergunta "e se…?" → vá mais fundo (concorrência, falha de rede, dados grandes) e proponha um teste que tente quebrar a solução.
- Copia trechos sem entender → peça para explicar com as próprias palavras o que o trecho faz antes de seguir.

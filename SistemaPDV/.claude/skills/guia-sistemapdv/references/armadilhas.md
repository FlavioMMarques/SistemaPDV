# Armadilhas já pagas (índice do `docs/APRENDIZADOS.md`)

Cada número é uma entrada do `docs/APRENDIZADOS.md`, com o problema, a causa e a correção. Leia as da área em que vai mexer **antes** de começar.

## Sincronização com a API (a área mais cara)
- **#53** cinco tarefas "verdes" e a sincronização nunca funcionou: o servidor falso era permissivo demais.
- **#56** o corpo do POST é o que a API **exige** (descoberto pelo 422), não o que o Swagger lista.
- **#84** a API real exige `razao_social` sempre, mesmo o Swagger dizendo "só para jurídica".
- **#71** quando o Swagger erra o caminho, olhe onde a API já funciona.
- **#54** a chave do operador é hash **bcrypt**, não SHA-256. **#55** "a primeira da lista" não é "a do dispositivo".
- **#45** resposta `200` não garante JSON. **#42** `409` nem sempre significa "já sincronizado".
- **#65** antes de "provar" com a API, olhe o que o banco local já provou; proteja o laço de paginação.
- **#86, #89** o cadastro de produto sobe, mas preço e estoque chegam zerados na empresa.
- **#90** dois códigos de cidade: o interno da API e o do IBGE.

## Fila de envio e offline-first
- **#4** idempotência com chave gerada no cliente. **#5** chave natural × substituta (o caixa).
- **#17** um campo para dois estados independentes causou 4 bugs. **#18** um item que falha não pode travar o lote.
- **#50** retentativa: contar e agendar no **banco**, não na memória, e "desistir" precisa de saída.
- **#57** "pendente sem erro" precisa dizer o que espera. **#60** o que o fechamento resume tem que chegar antes.
- **#64** número único por empresa com vários PDVs offline: prefixo por dispositivo.
- **#87** cadastrar algo tem que atualizar na hora o contador de pendentes.

## Banco de dados (EF Core / SQLite)
- **#2** ciclo de vida do SQLite `:memory:` nos testes. **#10** FK pega bug no teste.
- **#11, #12** tipos "owned". **#23** `= true` em C# não é o default da coluna. **#14, #46** `Contains` não é busca (`LIKE` tem curingas).
- **#22** N+1: tirar a consulta de dentro do laço. **#7** `DateOnly` × `DateTime`.

## Testes
- **#31** para testar uma reação desacoplada, espere o sinal de saída. **#38** teste que passa sozinho e falha na suíte é bug de teste.
- **#49** teste o ciclo, não o timer. **#43** teste a decodificação, não o texto escapado. **#67** reproduza a corrida num teste antes de "consertar".
- **#37** a revisão de código achou 3 problemas reais mesmo com tudo testado.

## Telas (MVVM / ReactiveUI / Avalonia)
- **#26** `ReactiveCommand` também é um fluxo. **#29** inicialização assíncrona não mora no construtor. **#61** carregue antes de mostrar a tela.
- **#33** `WhenAnyValue` e coleções. **#36** a ordem de atribuição importa quando algo observa. **#58** exceção em comando derruba o app.
- **#34** quando o code-behind é a exceção documentada (só apresentação). **#73** atalhos dependem de foco.
- **#72** redesenhar por um print: renderize de verdade. **#68** uma tela que ninguém consegue abrir não existe.
- **#83** em `Subscribe(_ => ...)` o `_` é o parâmetro; `_ = Tarefa()` vira atribuição. **#82** os estilos de uma tela não chegam a popups (a lista de um combo): estilos de popup ficam globais.

## Números, dinheiro e texto
- **#16** `CultureInfo.InvariantCulture` ao enviar número. **#59** `"10,50"` lido como 1050 (leitor próprio para dinheiro).
- **#47** valide na entrada com a mesma regra do envio.

## Segurança e dados sensíveis
- **#20** hash × criptografia reversível. **#41** modelo de ameaça antes do código. **#66** o que registrar em log e o que **nunca** registrar.
- **#63** descartar com auditoria, não com `DELETE`. **#69** desligar uma regra com um interruptor central, sem apagar o código.
- **#70** atraso progressivo no login.

## Processo
- **#52** `perl -pi` com `$` e `"` morde. **#21** extraia duplicação só depois que ela aparece de verdade.
- **#3** `TargetFramework` precisa casar entre projetos. **#8** `ImplicitUsings`. **#13, #25** atributos de plataforma sobem a cadeia de chamada.

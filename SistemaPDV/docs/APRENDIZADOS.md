# Aprendizados do SistemaPDV

Registro dos porquês por trás das decisões de design/código tomadas durante a construção do projeto — pra revisar e fixar os conceitos no final. Cada entrada é curta: o conceito, onde apareceu, e por que foi essa a escolha.

## 1. Interfaces genéricas e polimorfismo — `ISincronizavel<TKey>`

**Onde:** `specs/SPEC-data-layer.md`, `Models/ISincronizavel.cs`

A maioria das entidades usa `int` como chave local, mas `Venda` usa `Guid`. Em vez de duas interfaces (duplicação) ou deixar `Venda` fora do contrato comum (perde polimorfismo), a interface é genérica no tipo da chave: `ISincronizavel<TKey>`. Cada classe concreta escolhe seu `TKey` (`Produto : ISincronizavel<int>`, `Venda : ISincronizavel<Guid>`) e o compilador garante type-safety em cada uma — sem cast, sem `object`. É o mesmo mecanismo por trás de `List<T>`/`IEnumerable<T>` no .NET: parametrizar o tipo em vez de repetir código.

## 2. Ciclo de vida de uma conexão Sqlite `:memory:`

**Onde:** `SistemaPDV.Tests/SqliteInMemoryFixture.cs`

Um banco Sqlite `:memory:` só existe enquanto aquela conexão específica está aberta — fechar e reabrir não reconecta ao mesmo banco, cria um novo vazio. Por isso a fixture de teste guarda a `SqliteConnection` aberta durante todo o teste, em vez do padrão usual de abrir/fechar por operação.

## 3. `TargetFramework` precisa casar entre projetos referenciados

**Onde:** `SistemaPDV.Tests.csproj`

Um projeto não pode ter `ProjectReference` pra outro que rode numa versão do .NET mais nova (ex: um projeto `net8.0` não referencia um `net10.0`). O SDK instalado era o 10.0.401, então `dotnet new xunit` gerou `net10.0` por padrão — precisou trocar pra `net8.0` manualmente pra bater com `SistemaPDV.csproj`.

## 4. Idempotência via chave gerada no cliente

**Onde:** `specs/SPEC-sales.md`

O `Guid` da `Venda` é gerado no PDV (offline, sem rede) e é o mesmo valor enviado como `guid` pro servidor. Isso resolve o problema clássico de "enviei mas não sei se chegou": se a venda já existe do lado do servidor (checagem pelo `guid`), ele responde `409 Conflict` em vez de duplicar — e o PDV trata esse 409 como sucesso (já estava sincronizado), não como erro. Sem uma chave gerada no cliente, não teria como distinguir "nunca enviei" de "enviei mas a resposta se perdeu antes de eu confirmar".

## 5. Chave natural vs. chave substituta (surrogate key) — Caixa offline-first

**Onde:** `specs/SPEC-caixa.md`, `specs/SPEC-sales.md`

O caixa tem um id numérico gerado pelo servidor (chave substituta/surrogate), mas esse id só existe *depois* que a abertura sincroniza. A API aceita a venda referenciando o caixa pela combinação `data`+`turno`+`funcionário` (a chave *natural* — os atributos que já identificam o caixa no mundo real, sem precisar de um id artificial). Isso é o que permite abrir caixa e vender 100% offline: o PDV sempre sabe a chave natural sem rede; o id numérico é só um dado extra, preenchido quando (e se) sincronizar.

## 6. Documentação Swagger auto-gerada pode confundir request e response

**Onde:** `specs/SPEC-sales.md`, contrato da API na memória do agente

Um campo marcado como "não nullable" no schema de um endpoint POST não significa necessariamente "obrigatório no envio" — pode só refletir que aquele campo não aceita `null` na tabela do banco, mesmo sendo preenchido pelo servidor depois (ex: `xml`, `numero_nfe` da venda, claramente gerados na resposta, não no request). Ferramentas como Swagger geradas automaticamente de um framework (aqui, aparentemente Laravel) costumam usar o mesmo "Model" pra descrever tanto o que se envia quanto o que se recebe. Não dá pra confiar cegamente nisso — testar o endpoint de verdade é a única forma de saber o que é realmente exigido na criação.

## 7. `DateOnly` vs. `DateTime`

**Onde:** `Models/Caixa.cs`

`DataCaixa` usa o tipo `DateOnly` (C# 10+), não `DateTime`. A diferença importa aqui: "caixa do dia 18/09" é conceitualmente uma **data**, sem hora — duas aberturas de caixa às 08:00:00 e 08:00:01 do mesmo dia são o *mesmo* caixa. Se `DataCaixa` fosse `DateTime`, cada abertura teria um horário ligeiramente diferente, e o índice único em `(DataCaixa, Turno, FuncionarioId)` nunca bateria de verdade — cada linha pareceria "diferente" por causa dos segundos. `DateTime` continua sendo o tipo certo pra `DataAbertura`/`DataFechamento`, que *são* instantes no tempo (têm hora, minuto, segundo com significado). Regra geral: usar o tipo que representa exatamente o conceito, nem mais nem menos informação do que ele carrega.

## 8. `ImplicitUsings` e por que um projeto pediu `using System;` e o outro não

**Onde:** `Models/Caixa.cs` vs. `SistemaPDV.Tests.csproj`

O projeto de testes (`SistemaPDV.Tests.csproj`) tem `<ImplicitUsings>enable</ImplicitUsings>`, que faz o compilador incluir automaticamente `using System;`, `using System.Linq;` etc. em todo arquivo — é o padrão em projetos `dotnet new` mais recentes. O projeto principal (`SistemaPDV.csproj`) foi criado pelo template do Avalonia, que **não** liga essa opção — por isso `Caixa.cs` precisou de um `using System;` explícito pra enxergar `DateOnly`/`DateTime` (tipos que, sem `ImplicitUsings`, não vêm "de graça"). Não é um bug, é só uma configuração de projeto diferente — vale conferir o `.csproj` quando um tipo "básico" não é encontrado sem motivo aparente.

## 9. Um pequeno atrito de Interface Segregation, aceito conscientemente

**Onde:** `Models/Venda.cs`

`Venda` implementa `ISincronizavel<Guid>`, que exige a propriedade `IdExterno` — mas a API não devolve um id de venda "casável" nesse padrão (ela devolve um id de venda próprio, guardado em `VendaIdExterno`, uma propriedade separada). Ou seja, `Venda.IdExterno` existe só porque a interface pede, e nunca é usado de verdade.

Isso é tecnicamente uma violação pequena do **Interface Segregation Principle** (o "I" do SOLID: nenhuma classe deveria ser forçada a depender de membros que não usa). A alternativa "mais pura" seria quebrar `ISincronizavel<TKey>` em duas interfaces menores (uma só com `SyncStatus`, outra com `IdExterno`) e `Venda` implementar só a primeira. Optei por não fazer isso aqui: o ganho de manter as duas entidades (a maioria com `int`/`IdExterno` de verdade, e `Venda` com `Guid`) no mesmo contrato polimórfico simples supera o custo de uma propriedade não usada numa única classe. É uma troca consciente, não um descuido — vale saber nomear quando um princípio está sendo levemente dobrado por uma razão válida, em vez de seguir a regra às cegas.

## 10. Restrição de chave estrangeira (FK) pega bug no teste, não só no app

**Onde:** `SistemaPDV.Tests/ItemVendaEPagamentoVendaConfigurationTests.cs`

Os primeiros testes de `ItemVenda`/`PagamentoVenda` (Task 9) usavam um `Guid.NewGuid()` qualquer como `VendaId`, sem uma `Venda` de verdade por trás — funcionaram até a Task 10 adicionar a relação `Venda → Itens/Pagamentos` com FK de verdade. A partir daí, o SQLite passou a rejeitar esses inserts (`FOREIGN KEY constraint failed`), porque agora existe uma restrição garantindo que todo `ItemVenda`/`PagamentoVenda` aponte pra uma `Venda` que realmente existe. A constraint fez exatamente o trabalho dela: impedir dado órfão — só que dessa vez pegou um teste malformado, não um bug do app. Corrigir o teste (criar a `Venda` de verdade antes) é o caminho certo; "afrouxar" a constraint pra passar seria esconder o problema, não resolvê-lo.

## 11. Owned types no EF Core — composição em vez de uma tabela + FK

**Onde:** `Models/TabelaPreco.cs`, `Data/Configurations/ClienteConfiguration.cs`

O campo `tabela_preco` da API vem como um objeto aninhado (`{ "descricao": "PADRAO", "id": null }`), sem vida própria fora do cliente — não faz sentido uma tabela `TabelasPreco` separada com FK, porque ninguém vai consultar "todas as tabelas de preço" independente de um cliente. Pra isso, EF Core tem os **owned types**: `TabelaPreco` é uma classe comum, mas configurada com `builder.OwnsOne(c => c.TabelaPreco)` — o EF guarda os campos dela como colunas extras dentro da própria tabela `Clientes` (`TabelaPreco_Descricao`, etc.), sem criar tabela nem FK novas. É a diferença entre **composição** ("tabela de preço é uma característica do cliente") e **associação** ("tabela de preço é uma entidade que o cliente referencia") — a segunda pediria uma entidade de verdade com `ISincronizavel<int>`, `DbSet` próprio e FK; a primeira, não.

**Detalhe extra:** como `Cliente.TabelaPreco` é opcional (`TabelaPreco?`), o EF avisou que precisava de pelo menos uma propriedade **não anulável** dentro de `TabelaPreco` — sem isso, um `TabelaPreco` com todos os campos nulos fica idêntico, no banco, a "esse cliente não tem `TabelaPreco` nenhum", e o EF não teria como saber qual dos dois casos é o real ao reconstruir o objeto na leitura. `Descricao` virou `required string` (não `string?`) por causa disso — e bate com o que a API sempre manda de verdade.

## 12. `OwnsMany` precisa de chave própria — `OwnsOne` não

**Onde:** `Data/Configurations/ProdutoConfiguration.cs`

`OwnsOne` (como `TabelaPreco` em `Cliente`) não precisa de chave: só existe **um** por dono, então o próprio `ClienteId` já identifica a linha univocamente. `OwnsMany` (como `Imagens` em `Produto`) é uma **lista** — o EF precisa de alguma forma de diferenciar item 1, item 2, item 3 dentro da mesma coleção, e como `ImagemProduto` não tem nenhum campo que sirva de identificador natural, é preciso configurar uma chave "sombra" (`shadow property` — existe só no banco/no modelo do EF, sem propriedade C# correspondente na classe) e marcar explicitamente como auto-incremento (`ValueGeneratedOnAdd()`). Sem isso, o SQLite reclama de `NOT NULL constraint failed` porque o EF tentou inserir a linha sem saber gerar um valor pra essa chave. Foi exatamente o erro que apareceu no primeiro teste — corrigido configurando a chave explicitamente.

## 13. `[SupportedOSPlatform]` — declarar restrições de plataforma pro compilador

**Onde:** `Services/Sync/SegredoProtector.cs`, `SistemaPDV.Tests/SegredoProtectorTests.cs`

O .NET tem um analisador de compatibilidade de plataforma (`CA1416`) que avisa quando código "genérico" (que roda em qualquer SO) chama uma API específica de uma plataforma — nesse caso, `ProtectedData` (DPAPI) só existe no Windows. Em vez de silenciar o aviso ou ignorar, a forma correta é declarar a restrição com o atributo `[SupportedOSPlatform("windows")]`: isso não muda nada em tempo de execução (não é uma checagem real), mas documenta a restrição de um jeito que o *compilador* entende — e qualquer código que chame algo marcado assim também precisa declarar a mesma restrição, formando uma cadeia rastreável de "isso só funciona no Windows" em vez de descobrir isso só quando quebrar em produção noutro SO.

**Ajuste de granularidade:** a primeira versão colocou o atributo na classe `SoftcomAuthService` inteira, mas só o método `ObterTokenAsync` de fato chama `SegredoProtector` — isso vazou a restrição pro `CatalogSyncService`, que só usa `ExtrairDominio` (parsing puro de URI, sem DPAPI) e não deveria carregar essa restrição nenhuma. Mover o atributo da classe pro método específico corrigiu isso. Lição: declarar a restrição no nível mais estreito possível (método, não classe) evita espalhar uma limitação de plataforma pra código que não a tem de verdade.

## 14. `string.Contains` substring solto é uma cilada clássica

**Onde:** `SistemaPDV.Tests/SoftcomApiClientTests.cs`

No teste de paginação, o handler fake decidia qual página devolver checando se a URL continha `"page=2"`. Só que a URL da *primeira* chamada já é `...?per_page=200` — e `"per_page=200"` contém a substring `"page=2"` escondida dentro do `200`! Resultado: o teste "de duas páginas" respondia a segunda página já na primeira chamada, e o teste passava por acidente (ou, nesse caso, falhava de um jeito enganoso — só 1 chamada em vez de 2). A correção foi checar `"?page=2"` (com o `?` na frente), que só existe de verdade na URL da segunda página. Lição: `Contains` sem âncora (início/fim/delimitador) é um risco real toda vez que um número aparece dentro de outro texto — vale conferir com um exemplo mental antes de confiar num `Contains` solto em lógica de teste ou produção.

## 15. Namespace com o mesmo nome de um tipo — resolve, mas fica explícito

**Onde:** `Services/Caixa/CaixaService.cs`

O serviço está em `SistemaPDV.Services.Caixa` (o namespace termina em "Caixa"), e ele referencia o tipo `SistemaPDV.Models.Caixa` (a entidade). Escrito como `Models.Caixa` (não só `Caixa` solto) — o C# consegue resolver `Models` porque ele sobe a cadeia de namespaces que envolvem o arquivo (`SistemaPDV.Services.Caixa` → `SistemaPDV.Services` → `SistemaPDV`) e encontra `SistemaPDV.Models` como vizinho de `SistemaPDV.Services`. Funcionaria mesmo sem o prefixo `Models.` (o compilador resolveria sozinho), mas deixar explícito evita a leitura confusa de "essa classe `Caixa` é a entidade, ou é alguma coisa do namespace `Services.Caixa`?" — clareza para quem lê vale mais que economizar cinco letras.

## 16. `CultureInfo.InvariantCulture` ao formatar número pra enviar numa API

**Onde:** `Services/Caixa/CaixaSyncService.cs` (`FormatarValor`)

A API espera `troco_inicial` como string tipo `"10.00"` (ponto decimal). `decimal.ToString("F2")` sem especificar cultura usa a configuração regional do Windows onde o app está rodando — no Brasil (pt-BR), isso produz `"10,00"` (vírgula), porque é assim que o Windows do usuário formata números por padrão. Se a API não aceitar vírgula (a maioria não aceita, pois separa por vírgula em CSV/JSON), o valor seria rejeitado ou mal interpretado silenciosamente. `CultureInfo.InvariantCulture` força o formato "neutro" (ponto decimal), independente de como o Windows da máquina está configurado — regra prática: toda formatação de número que vai **para uma API ou arquivo** (não pra tela do usuário) deveria usar `InvariantCulture`; só a exibição na UI é que deve respeitar a cultura local.

## 17. Um campo só tentando guardar dois estados independentes — a causa raiz de 4 bugs

**Onde:** `Models/Caixa.cs`, `Services/Caixa/CaixaSyncService.cs` (revisão de código, 2026-09-18)

A revisão de código encontrou 4 bugs em caixa que pareciam separados, mas tinham a mesma causa: `Caixa` só tinha **um** campo (`SyncStatus`) pra representar se estava sincronizado — só que abrir e fechar caixa são **duas ações independentes**, que acontecem em momentos diferentes (às vezes um caixa fica dias aberto entre a abertura e o fechamento). Um único campo não dá conta de responder "a abertura confirmou?" e "o fechamento confirmou?" ao mesmo tempo — ele só sabe responder sobre a última coisa que aconteceu.

Isso causava, junto: a consulta que decidia "isso é abertura ou fechamento pendente?" não filtrava por `Status`, então um caixa fechado offline antes da abertura sincronizar era tratado só como "abertura pendente" — e quando essa abertura confirmava, o `SyncStatus` virava `Sincronizado`, apagando pra sempre o sinal de que o fechamento ainda precisava ser enviado. **O fechamento — troco final, conferência por forma de pagamento — se perdia silenciosamente.**

A correção: um campo novo, `AberturaSincronizada` (bool), dedicado **só** à abertura. `SyncStatus` continua existindo, mas agora seu significado depende do `Status` atual do caixa: enquanto `Aberto`, ele reflete a abertura; assim que fecha, `FecharCaixaLocalAsync` já reseta ele pra `PendenteSync` e ele passa a representar o fechamento. A regra de ouro que ficou clara: **quando dois eventos independentes podem acontecer em momentos diferentes, cada um precisa do seu próprio campo de status** — tentar economizar um campo reaproveitando o mesmo lugar pra duas coisas é o tipo de decisão que "funciona" nos casos simples (testados primeiro) e quebra silenciosamente nos casos de borda (testados depois, ou nunca).

**Bônus: a correção errou da primeira vez, e o teste pegou.** A primeira tentativa fez a abertura sempre marcar `SyncStatus = Sincronizado` no sucesso — só que isso tinha o MESMO problema original, só que num ponto diferente: se o caixa já tinha sido fechado offline antes da abertura confirmar, essa marcação apagava de novo o "fechamento pendente". O teste de integração (`CaixaAbertoEFechadoInteiramenteOfflineNaoPerdeOFechamentoAoSincronizar`) pegou isso na hora — a segunda sincronização não achava o fechamento pendente. A correção final só marca `Sincronizado` se o caixa **ainda estiver aberto** no momento em que a abertura confirma; se já fechou, deixa o `SyncStatus` como estava (o fechamento cuida dele depois). Isso é um exemplo real de por que vale ter teste cobrindo o cenário de ponta a ponta, não só cada método isolado — o bug só aparecia na combinação abrir→fechar→sincronizar, não em nenhum passo sozinho.

## 18. Sincronização em lote precisa isolar a falha de UM item, não só "confiar" que não vai lançar

**Onde:** `Services/Sales/VendaSyncService.cs` (revisão de código, 2026-09-18)

`SincronizarVendasPendentesAsync` já tinha um comentário prometendo "uma venda com erro não impede as outras" — mas a promessa só valia se `SincronizarVendaAsync` **nunca lançasse exceção**, e ela lançava (`throw new InvalidOperationException`) em duas situações que "não deveriam acontecer" (caixa ou configuração sumidos). Sem `try/catch` no laço, a primeira venda nessas condições derrubava o lote inteiro — as vendas seguintes nunca eram tentadas. A lição: uma garantia como "item com erro não trava os outros" não é automática só porque cada item *devolve* um resultado de erro em vez de lançar no caminho feliz — precisa proteger o laço explicitamente contra qualquer coisa inesperada, porque "não deveria acontecer" não é o mesmo que "nunca vai acontecer".

## 19. Validar antes de mudar estado, não depois

**Onde:** `Services/Caixa/CaixaService.cs` (`FecharCaixaLocalAsync`), `Services/Sync/CatalogSyncService.cs` (revisão de código, 2026-09-18)

Dois bugs pequenos, mesmo padrão: `FecharCaixaLocalAsync` alterava o `Caixa` (mudava `Status`, `SyncStatus`, adicionava digitações) **antes** de confirmar que os dados recebidos eram válidos — um `formaPagamentoId` inexistente só estourava no `SaveChangesAsync`, como uma exceção de banco crua em vez do `ComFalha` que o resto do método usa. A correção move a validação pra ANTES de qualquer mutação: se algo é inválido, o método retorna cedo, sem o objeto `caixa` ter sido tocado — nenhum estado parcialmente mudado pra desfazer. Regra prática: valide tudo que pode falhar primeiro, só depois comece a mudar o objeto — assim uma falha no meio nunca deixa o objeto "meio mudado".

Parecido, mas ao contrário: `CatalogSyncService` sobrescrevia `PdvKeyHash` mesmo quando a API não mandava `pdv_key` dessa vez, apagando um login que já funcionava. Ali a correção foi o oposto — só mudar o campo se o dado novo realmente veio (`if (novoPdvKeyHash is not null) entidade.PdvKeyHash = novoPdvKeyHash;`), em vez de sobrescrever incondicionalmente. Duas versões da mesma ideia: **não mude estado com base em dado que pode não ter vindo de verdade.**

## 20. Hash vs. criptografia reversível pra dados sensíveis

**Onde:** `specs/SPEC-caixa.md` (`PdvKeyHash`) vs. o `client_secret` protegido por DPAPI no projeto de referência

Duas situações parecidas, tratamentos diferentes, porque o uso é diferente:
- `pdv_key` (login do operador): só precisa ser **comparado** ("o que o operador digitou bate com o que está salvo?"). Hash (SHA-256) resolve — nunca precisa recuperar o valor original, só comparar hash com hash.
- `client_secret` (credencial OAuth): precisa ser **reenviado** pra API a cada renovação de token — o app precisa conseguir recuperar o valor original. Hash não serve aqui (é uma via de mão única); precisa de criptografia reversível (DPAPI), que permite proteger em repouso e ainda assim descriptografar quando necessário.

## 21. Extrair duplicação só depois que ela aparece de verdade (e nem sempre)

**Onde:** `Services/SoftcomJson.cs`, `Services/OutboxHelper.cs`, `Services/Sync/SoftcomApiClient.cs` (`EnviarAsync`), `Services/Sync/ResultadoEnvio.cs` — polimento pós-revisão, 2026-09-18

A revisão de código apontou três duplicações reais entre `CaixaSyncService` e `VendaSyncService` (que nasceram como cópias uma da outra, já que resolvem o mesmo problema — outbox — pra entidades diferentes):
- `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` repetido em 3 lugares → virou `SoftcomJson.Opcoes`, uma constante estática.
- A sequência "montar `HttpRequestMessage`, adicionar headers, mandar, ler conteúdo, decidir sucesso/409/401/falha" repetida em cada método de escrita → virou `SoftcomApiClient.EnviarAsync`, que devolve um `ResultadoEnvio` (par Tipo+Conteudo) pro chamador só reagir.
- O `MarcarFalhaAsync` privado de cada serviço (grava mensagem de erro + `SaveChanges`) → virou `OutboxHelper.MarcarFalhaAsync<T>`, genérico, com um `Action<T,string> aplicarErro` pra cada chamador decidir quais campos exatos mexer (só `Venda` incrementa `TentativasEnvio`).

Importante: **isso não virou uma regra geral**. `CatalogSyncService` tem uma duplicação parecida (5 métodos quase idênticos fazendo upsert) e ficou de propósito sem extrair — o comentário no topo da classe explica por quê: um upsert genérico via `ISincronizavel<TKey>` dependeria do EF Core conseguir traduzir `e.IdExterno` (acesso por membro de INTERFACE) pro nome de coluna certo da entidade concreta, o que não é garantido e só quebraria em runtime. Duplicação pequena e sem esse risco vale a pena extrair; duplicação que só se resolve com um padrão arriscado, não.

## 22. N+1: mover a consulta pra FORA do laço, não só otimizar o que está dentro dele

**Onde:** `Services/Sync/CatalogSyncService.cs` (os 4 métodos `Sincronizar*Async` de listas) — polimento pós-revisão, 2026-09-18

Cada upsert de lote fazia `await context.X.FirstOrDefaultAsync(x => x.IdExterno == dto.Id, ct)` **dentro** do `foreach` — uma ida ao banco por item do lote. Com paginação de até 200 itens por página, isso é até 200 consultas SQL pra sincronizar uma página só (o clássico problema N+1: 1 consulta "principal" que devolve N itens, mais N consultas extras, uma por item).

A correção: antes do laço, juntar todos os `Id` externos do lote numa lista e fazer **uma única consulta em lote** (`Where(x => idsExternos.Contains(x.IdExterno)).ToDictionaryAsync(...)`), preenchendo o mesmo dicionário `processados` que já existia pra deduplicar registros repetidos entre páginas. Dentro do laço, só sobra `TryGetValue` (em memória, sem ida ao banco) — de N+1 consultas pra exatamente 2 (uma de leitura em lote, um `SaveChanges` no final). Detalhe técnico: `dto.Id` é `int` (a API sempre manda um id) mas `IdExterno` na entidade é `int?` (registros só criados localmente ainda não têm par externo) — `List<int>.Contains(int?)` não compila, por isso a lista de ids precisou ser `List<int?>` (`(int?)i.Id`) pra bater com o tipo da coluna.

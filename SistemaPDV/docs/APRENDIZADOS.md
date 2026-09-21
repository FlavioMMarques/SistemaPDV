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

## 23. Inicializador de propriedade em C# (`= true`) não é o mesmo que default de coluna no EF Core

**Onde:** `Models/ConfiguracaoSincronizacao.cs` (`ExigirAberturaCaixa`), `Data/Configurations/ConfiguracaoSincronizacaoConfiguration.cs` — Fase 5 (pdv-ui), Task 37

Escrever `public bool ExigirAberturaCaixa { get; set; } = true;` parece bastar pra garantir que o campo "começa como true" — e começa, mas só pro lado do C#: todo `new ConfiguracaoSincronizacao()` criado pelo próprio app já nasce com `true` em memória, e como o EF sempre manda o valor explícito no INSERT, isso funciona no caminho normal do app.

O problema é a **coluna do banco em si**. Quando rodei `dotnet ef migrations add` pela primeira vez sem configurar nada além do inicializador, o EF gerou `AddColumn<bool>(..., defaultValue: false)` — ele ignora completamente o `= true` do C#, porque o inicializador de propriedade não é metadado que o EF consegue inspecionar na hora de montar a migration; ele só olha o que foi configurado explicitamente via Fluent API (`HasDefaultValue`) ou Data Annotations. Isso importa em dois cenários que o "sempre passa pelo app" não cobre: uma migration que faz `ALTER TABLE ADD COLUMN` numa tabela que **já tem linhas** (o SQL precisa de um valor pra preencher essas linhas existentes — e sem `HasDefaultValue`, esse valor é o default do tipo, `false` pra bool) e qualquer inserção que não passe pelo construtor do C# (script SQL manual, outra ferramenta).

Corrigido com `builder.Property(c => c.ExigirAberturaCaixa).HasDefaultValue(true);` na classe de configuração, **antes** de gerar a migration — com isso o SQL gerado vira `defaultValue: true`, e o default fica correto tanto pra linhas novas quanto pra qualquer linha que um dia seja preenchida por fora do C#. Regra prática: pra qualquer coluna nova com default diferente do "zero value" do tipo (`false` pra bool, `0` pra número, `null` pra string), configurar `HasDefaultValue` explicitamente e só depois gerar a migration — nunca confiar só no inicializador de propriedade.

## 24. Composition root — um único lugar que monta as dependências

**Onde:** `AppServices.cs`, `App.axaml.cs` — Fase 5 (pdv-ui), Task 38

Até agora, todo serviço (`CaixaService`, `VendaService`, etc.) só era construído dentro de **testes** — cada teste montava suas próprias instâncias com um `Func<AppDbContext>` de um banco in-memory descartável. Isso nunca precisou de um lugar central porque cada teste é independente. Mas o app de verdade só tem UMA instância de cada serviço, vivendo durante toda a execução, e várias telas (ViewModels) vão precisar delas.

`AppServices` é esse lugar central — o **composition root** do app: o único ponto que sabe como montar cada peça (qual `HttpClient`, qual `Func<AppDbContext>`, em que ordem) e entrega o resultado já pronto. A regra que vem junto: **todo o resto do app só recebe instâncias prontas via construtor** — um `ViewModel` nunca vai fazer `new CaixaService(...)` sozinho, só recebe um `CaixaService` já existente. Isso é o que a decisão "sem container de DI" significa na prática: em vez de um framework genérico resolvendo isso automaticamente (como `Microsoft.Extensions.DependencyInjection` faria), é uma classe comum, com construtor comum, fazendo isso manualmente — mesmo padrão, sem a ferramenta.

## 25. Atributo de plataforma sobe a cadeia de chamada até o ponto de entrada real

**Onde:** `AppServices.cs` (construtor), `App.axaml.cs` (classe inteira), `Program.cs` (`Main`/`BuildAvaloniaApp`) — Fase 5 (pdv-ui), Task 38

`[SupportedOSPlatform("windows")]` não é uma checagem que roda — é só uma anotação que o analisador de compatibilidade de plataforma (regra CA1416) usa pra saber onde é seguro chamar código Windows-only sem aviso. A regra é simples: **quem chama um método marcado também precisa estar marcado** (ou fazer uma checagem em runtime, que não é o caso aqui). Isso significa que a anotação se propaga: `AppServices` (usa `SegredoProtector`, que é DPAPI) → `App.OnFrameworkInitializationCompleted` (constrói `AppServices`) → `Program.Main`/`BuildAvaloniaApp` (constrói `App`). Cada aviso do compilador (CA1416) apontava exatamente o próximo elo que faltava marcar — segui a cadeia até o topo real (`Main`, que não tem mais ninguém chamando ele dentro do próprio programa). Como o app inteiro só faz sentido rodando em Windows (a dependência de DPAPI é permanente, não uma feature opcional), não faz sentido nenhum caminho de código ficar "sem a marcação" só pra evitar o aviso — o aviso está certo, o app É Windows-only por completo.

## 26. `ReactiveCommand<TParam, TResult>` também é um `IObservable<TResult>`

**Onde:** `ViewModels/LoginViewModel.cs` (`EntrarCommand`) — Fase 5 (pdv-ui), Task 40

Depois que o operador loga, alguém precisa saber "deu certo, pode navegar pra frente" — mas `LoginViewModel` não conhece o `ShellViewModel` (não devia conhecer: senão toda tela precisaria saber sobre a tela seguinte, um acoplamento que cresce sem parar). A solução mais óbvia seria um evento C# comum (`event EventHandler<Funcionario>? LoginSucedido`), mas o ReactiveUI já resolve isso sem precisar inventar nada: todo `ReactiveCommand<TParam, TResult>` **é, ele mesmo, um `IObservable<TResult>`** — toda vez que o comando executa com sucesso, ele emite o resultado (aqui, o `Funcionario?` autenticado) pra quem quiser se inscrever. Então `EntrarCommand.Subscribe(funcionario => ...)` (que a Task 42 vai usar no `ShellViewModel`) funciona exatamente como qualquer outro observable do Rx — sem acoplar `LoginViewModel` a quem está ouvindo. É o mesmo princípio por trás de expor `IEnumerable<T>` em vez de `List<T>` concreta: o comando expõe *o que ele produz*, não *quem vai consumir*.

Detalhe de teste: pra `await viewModel.EntrarCommand.Execute()` funcionar (em vez de `.Subscribe`/callback), precisa de `using System.Reactive.Linq;` — é essa biblioteca que dá o `GetAwaiter()` num `IObservable<T>`, permitindo tratar a execução do comando como se fosse uma `Task` comum dentro do teste.

## 27. Biblioteca nova, API pode exigir inicialização explícita que documentação antiga não menciona

**Onde:** `SistemaPDV.Tests/ReactiveUiTestInitializer.cs` — Fase 5 (pdv-ui), Task 40

Primeiro teste de ViewModel (`LoginViewModelTests`) falhou com `TypeInitializationException` / `ReactiveUI has not been initialized`, mesmo o código do `LoginViewModel` estando correto. Causa: a versão do ReactiveUI usada no projeto (23.2.28, bem recente) trocou a forma como a biblioteca inicializa internamente — antes acontecia sozinha na primeira chamada estática; agora exige uma chamada explícita (`RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp()`) em algum lugar antes de usar `WhenAnyValue`/`ObservableForProperty`. No app real isso já acontece escondido dentro de `.UseReactiveUI()` (chamado em `Program.cs`), mas o projeto de testes nunca passa por ali — só instancia ViewModels diretamente.

A correção foi um `[ModuleInitializer]` (`SistemaPDV.Tests/ReactiveUiTestInitializer.cs`) — um método que o .NET garante rodar automaticamente uma única vez, antes de qualquer teste do assembly, sem precisar chamar ele manualmente em cada classe de teste. Só `WithCoreServices()` (sem `WithPlatformServices()`, que registraria scheduler de UI real do Avalonia — desnecessário e potencialmente problemático rodando fora de uma janela de verdade). Lição prática: quando uma biblioteca lança um erro de inicialização que "não devia acontecer", vale considerar que a versão instalada pode ter mudado um contrato que exemplos/tutoriais mais antigos não cobrem — o erro geralmente já vem com a solução no próprio texto (esse veio com o link da doc de migração).

## 28. Um ViewModel que precisa de infraestrutura nova primeiro ganha um serviço, não um atalho

**Onde:** `Services/ConfiguracaoService.cs` — Fase 5 (pdv-ui), Task 41

Na hora de montar `ConfiguracoesViewModel`, apareceu um buraco: nenhum serviço das fases anteriores expõe "ler/gravar `ConfiguracaoSincronizacao`" pronto — só existia acesso via `Func<AppDbContext>` cru, e a Task 38 decidiu de propósito que `AppServices` nunca expõe isso pra um ViewModel (só serviços prontos). O atalho mais rápido seria abrir uma exceção só pra essa tela ("ok, só essa View recebe o `Func<AppDbContext>` direto") — mas isso quebraria a regra logo na primeira tela real que a testaria, e por um motivo ruim (economizar 80 linhas de código).

Em vez disso, `ConfiguracaoService` nasceu seguindo exatamente o molde que `LoginOperadorService`/`CaixaService`/`VendaService` já usam: recebe `Func<AppDbContext>` (e, nesse caso, também `SoftcomAuthService`+`SegredoProtector`, porque o provisionamento do dispositivo depende dos dois), expõe métodos com nome de intenção de negócio (`ObterOuCriarAsync`, `VincularDispositivoAsync`, `AtualizarAsync`), e é só isso que `AppServices`/`ConfiguracoesViewModel` conhecem. Reaproveitou até o mesmo formato de `OutboxHelper.MarcarFalhaAsync<T>` do polimento anterior: `AtualizarAsync(Action<ConfiguracaoSincronizacao> aplicar)` recebe um delegate que decide **o quê** mudar, sem duplicar o "abrir contexto, achar a linha, salvar" em cada chamador. Regra prática: quando uma nova tela precisa de uma capacidade que nenhum serviço existente cobre, o serviço novo é parte do trabalho da tela — não uma desculpa pra furar a regra que a própria tela criou.

## 29. Inicialização assíncrona de ViewModel não pode morar no construtor

**Onde:** `ViewModels/ConfiguracoesViewModel.cs` (`IniciarAsync`) — Fase 5 (pdv-ui), Task 41

`ConfiguracoesViewModel` precisa carregar a `ConfiguracaoSincronizacao` atual do banco antes de mostrar qualquer campo preenchido — mas isso é uma operação assíncrona (`await context...`), e construtores em C# não podem ser `async` (a linguagem não tem essa sintaxe: um construtor sempre devolve o objeto pronto na hora, não uma `Task`). Não dá pra simplesmente chamar `.GetAwaiter().GetResult()` dentro do construtor pra "forçar" o resultado — isso bloqueia a thread de UI esperando I/O, exatamente o que o Boundary da spec proíbe ("nunca travar a tela esperando rede/disco").

A solução comum em MVVM (não é truque específico desse projeto) é separar **construção** de **inicialização**: o construtor só monta o objeto num estado válido "vazio" (aqui, os campos como string vazia / `ExigirAberturaCaixa = true`), e um método `IniciarAsync()` à parte carrega o estado real — chamado explicitamente por quem está navegando pra essa tela (a Task 42, `ShellViewModel`, vai chamar isso na hora de trocar pra `ConfiguracoesView`). O teste reflete exatamente esse contrato: todo teste que precisa do estado carregado chama `await viewModel.IniciarAsync()` primeiro, igual ao Shell vai fazer de verdade.

## 30. `ViewLocator`: convenção de nome resolve ViewModel → View sozinha

**Onde:** `ViewLocator.cs` (já existia do template), `Views/ShellView.axaml`/`MainWindow.axaml` — Fase 5 (pdv-ui), Task 42

O template do Avalonia já vinha com um `ViewLocator` registrado em `App.axaml` (`<Application.DataTemplates><local:ViewLocator /></Application.DataTemplates>`), mas só ficou claro pra que serve quando o app passou a ter mais de uma tela. A regra dele é simples: qualquer `ContentControl` (ou coisa parecida) cujo `Content`/binding aponte pra um objeto que herda de `ViewModelBase` é automaticamente trocado pela View correspondente — ele pega o nome completo da classe (`SistemaPDV.ViewModels.LoginViewModel`), troca `"ViewModel"` por `"View"` no texto (vira `SistemaPDV.Views.LoginView`), acha esse tipo por reflexão, e cria uma instância nova via construtor sem parâmetros.

Isso é o que permite `MainWindow.axaml` ser só `<ContentControl Content="{Binding}" />` com `DataContext = shellViewModel`, e virar a `ShellView` de verdade sem nenhum código escrito à mão ligando os dois — e o mesmo vale dentro de `ShellView.axaml`, cujo `<ContentControl Content="{Binding CurrentViewModel}" />` troca sozinho entre `LoginView`/`ConfiguracoesView`/etc conforme `ShellViewModel.CurrentViewModel` muda. A regra prática que isso implica: **toda View nova precisa ter o mesmo nome do ViewModel (trocando só o sufixo) e um construtor sem parâmetros** — senão o `ViewLocator` não acha ela (e mostra um `TextBlock` de erro no lugar, que é como esse tipo de problema se manifestaria na tela).

## 31. Testar uma reação desacoplada (`Subscribe` fire-and-forget) exige esperar o sinal de saída, não a operação de entrada

**Onde:** `SistemaPDV.Tests/ShellViewModelTests.cs` — Fase 5 (pdv-ui), Task 42

`ShellViewModel` escuta `LoginViewModel.EntrarCommand` via `.Subscribe(...)` (ver #26) — de propósito, pra não acoplar as duas classes. Só que isso tem uma consequência que só apareceu na hora de testar: `await loginViewModel.EntrarCommand.Execute()` espera **só o login em si** terminar (a chamada a `LoginOperadorService.AutenticarAsync`); o código que reage a esse resultado dentro do `Subscribe` (`AposLoginAsync`, que decide a próxima tela) roda **depois**, como uma continuação separada, e nada obriga esse `await` a esperar por ela também — são duas tarefas assíncronas independentes.

Os primeiros testes escritos falhavam de forma consistente (não-flaky, sempre o mesmo jeito) porque a asserção rodava antes de `AposLoginAsync` terminar. A tentação seria "resolver" isso com um `Thread.Sleep`/delay arbitrário — exatamente o anti-padrão de teste flaky que a skill `test-driven-development` pede pra evitar. A correção certa: esperar pelo **sinal de saída observável** (a mudança de `TelaAtual`), não pela operação de entrada:

```csharp
var telaMudou = shell.WhenAnyValue(s => s.TelaAtual).Where(t => t != Tela.Login).FirstAsync().ToTask();
await loginViewModel.EntrarCommand.Execute();
await telaMudou;
```

Isso assina a mudança de propriedade **antes** de disparar o login, e só afirma o resultado depois que ela de fato aconteceu — determinístico, sem depender de quanto tempo a continuação leva pra rodar. É a mesma ideia de testar o resultado (o que mudou) em vez de como aconteceu (quais métodos foram chamados), só aplicada a um cenário assíncrono/reativo em vez de síncrono.

## 32. `IValueConverter`: traduzir entre o formato do domínio e o formato do controle de UI

**Onde:** `Converters/TurnoParaIndiceConverter.cs`, `Views/AbrirCaixaView.axaml` — Fase 5 (pdv-ui), Task 43

`AbrirCaixaViewModel.Turno` é `1`/`2`/`3` (o valor que `CaixaService`/a API esperam) — mas o controle de UI escolhido pra selecionar o turno, `ComboBox.SelectedIndex`, é 0-based (`0`/`1`/`2`, a posição na lista). Ligar os dois direto (`SelectedIndex="{Binding Turno}"`) selecionaria sempre o item errado (turno 1 marcaria o índice 1 = "2 - Tarde").

Um `IValueConverter` resolve isso sem o ViewModel precisar saber nada sobre `ComboBox` (ele continua expondo `Turno` no formato que faz sentido pro domínio) e sem a View precisar de lógica além do binding: `Convert` roda quando o valor vai do ViewModel pra tela (`turno - 1`), `ConvertBack` quando o usuário troca a seleção e o valor volta pro ViewModel (`índice + 1`). É o mesmo princípio dos DTOs de API (traduzir formato externo ↔ formato interno), só que aqui a "borda externa" é o próprio controle visual, não uma API HTTP.

## 33. `WhenAnyValue` não enxerga mudança dentro de uma `ObservableCollection` sozinho

**Onde:** `ViewModels/PdvViewModel.cs` (`PodeFinalizarVenda`, `Total`) — Fase 5 (pdv-ui), Task 44

`PodeFinalizarVenda` (`Itens.Count > 0`) e `Total` (`Itens.Sum(...)`) dependem do **conteúdo** de `Itens`, uma `ObservableCollection<ItemCarrinho>` — mas `this.WhenAnyValue(vm => vm.Itens)` só dispararia se a propriedade `Itens` em si fosse trocada por uma coleção inteira nova (`Itens = novaLista`), nunca quando alguém faz `Itens.Add(...)`/`Itens.Remove(...)`. São dois eventos completamente diferentes: `INotifyPropertyChanged` (que `WhenAnyValue` escuta) avisa "essa propriedade agora aponta pra outro objeto"; `INotifyCollectionChanged` (que `ObservableCollection` implementa à parte) avisa "o conteúdo desse mesmo objeto mudou" — e `WhenAnyValue` só entende o primeiro.

A solução usada aqui foi direta: assinar `Itens.CollectionChanged` no construtor e, a cada mudança, chamar `this.RaisePropertyChanged(nameof(PodeFinalizarVenda))` e `this.RaisePropertyChanged(nameof(Total))` manualmente — como se essas duas propriedades computadas tivessem acabado de mudar de verdade (o que, semanticamente, aconteceu: o carrinho mudou, então o que elas calculam também mudou). Isso faz `this.WhenAnyValue(vm => vm.PodeFinalizarVenda)` (usado pra alimentar o `CanExecute` do `FinalizarVendaCommand`) funcionar normalmente, porque do ponto de vista dele é só mais um `PropertyChanged` chegando. Existe uma biblioteca (`DynamicData`) que oferece observables prontos pra coleção (`ObserveCollectionChanges`, `ToObservableChangeSet`), mas pra um projeto desse tamanho o `RaisePropertyChanged` manual é mais simples e não pede uma dependência nova.

## 34. Quando o "sem lógica no code-behind" precisa de uma exceção pontual e documentada

**Onde:** `Views/PdvView.axaml.cs` (atalho F4) — Fase 5 (pdv-ui), Task 45

A convenção do projeto (`Code Style` de `specs/SPEC-pdv-ui.md`) é clara: code-behind só chama `InitializeComponent()`, toda lógica fica no ViewModel. F2/F10/Esc respeitam isso perfeitamente — são só `KeyBinding` no XAML apontando pra um `ReactiveCommand`. F4 ("buscar produto") quebra esse padrão de um jeito genuíno: a ação dele não é "rodar uma lógica de negócio", é **mover o foco do teclado pra um controle específico da tela** — e não existe no Avalonia (nem em WPF/UWP) um jeito declarativo de expressar "focar este controle" via `Command`. Foco é inerentemente um conceito de `Control`/UI, não algo que um ViewModel deveria saber manipular (um ViewModel não tem — e não devia ter — uma referência pro `TextBox` de verdade).

A saída foi um `AddHandler(KeyDownEvent, ...)` no construtor de `PdvView.axaml.cs`, com um comentário explicando por que essa é a única exceção. A distinção que importa: **lógica de negócio** (o que aconteceu, o que deve ser salvo, o que é válido) sempre no ViewModel; **orquestração pura de apresentação** (o que está em foco, qual animação tocar) pode, às vezes, exigir código na View — o teste prático é "isso mudaria o resultado de um teste de ViewModel se eu removesse?" (não, foco não aparece em nenhuma asserção de `PdvViewModelTests`) — se a resposta é não, não é lógica de negócio vazando pro lugar errado.

## 35. Extrair um `UserControl` reaproveitável quando a spec já prometeu, não só na "terceira repetição"

**Onde:** `Views/Controls/SyncStatusIndicator.axaml`, `Converters/SyncStatusConverters.cs` — Fase 5 (pdv-ui), Task 45

A regra geral de not-over-engineering diz pra não extrair uma abstração antes do terceiro uso repetido — mas `specs/SPEC-pdv-ui.md` (aprovada com o usuário) já tinha decidido, antes de qualquer código, que o indicador de `SyncStatus` (🟢/🟡/🔴) seria um `UserControl` único reaproveitado em `Pdv`/`Cadastros`/`ListaPedidos`. Essa é uma decisão de design já tomada e registrada, não uma dúvida a resolver de novo a cada tela — construir três indicadores separados agora (um por tela, "esperando a terceira repetição") teria contrariado o que já foi combinado, só pra depois precisar desfazer.

`SyncStatusIndicator` usa uma `StyledProperty<SyncStatus>` (`Status`) — a forma "de verdade" de dar uma propriedade bindável a um `UserControl` customizado no Avalonia, diferente de uma propriedade CLR comum: ela participa do sistema de binding/estilo do framework (dá pra fazer `Status="{Binding SyncStatus}"` de fora, como qualquer propriedade de controle nativo). Os dois `IValueConverter`s (texto e cor) ficaram registrados como recursos **globais** em `App.axaml` (não locais a uma View), porque vão ser usados em várias telas diferentes — registrar local a cada tela seria repetir a mesma declaração de recurso em cada uma.

## 36. Ordem de atribuição de propriedades importa quando alguém está observando uma delas

**Onde:** `ViewModels/ShellViewModel.cs` (todos os métodos `IrPara*`) — Fase 5 (pdv-ui), Task 46

Cada método de navegação do Shell muda duas propriedades em sequência: `TelaAtual` (um enum, "qual tela é essa") e `CurrentViewModel` (o ViewModel de verdade que a `ContentControl` vai mostrar). A ordem original era `TelaAtual` primeiro, `CurrentViewModel` depois — parecia irrelevante, já que as duas linhas rodam uma logo após a outra, síncronas, no mesmo método.

Só que **"síncrono" não quer dizer "atômico"**: assim que `TelaAtual = Tela.Dashboard` executa, o `RaiseAndSetIfChanged` dispara o `PropertyChanged` na hora — e qualquer código que esteja esperando especificamente por essa mudança (no caso, um teste com `this.WhenAnyValue(s => s.TelaAtual).FirstAsync().ToTask()`) recebe o sinal **naquele exato instante**, antes da linha seguinte (`CurrentViewModel = viewModel`) sequer rodar. O `await` do lado de quem observa "acorda" no meio da execução do método que ainda estava mudando outras coisas — por isso os primeiros testes de ponta a ponta (login→abrir caixa→Dashboard→Pdv) falhavam com `CurrentViewModel` sendo ainda o da tela **anterior**, mesmo já tendo confirmado que `TelaAtual` tinha mudado.

A correção foi só inverter a ordem: `CurrentViewModel` sempre por último em cada `IrPara*`, garantindo que `TelaAtual` (a propriedade que sinaliza "cheguei") só muda depois que todo o resto do estado da navegação já está pronto — regra prática: **a última propriedade a mudar deveria ser a que representa "terminei"**, não uma escolhida arbitrariamente por ordem de leitura do código.

## 37. Revisão de código sobre trabalho recém-escrito — três achados reais, mesmo já tendo testado tudo

**Onde:** `ViewModels/PdvViewModel.cs`, `ViewModels/ShellViewModel.cs` — revisão de código pós-Task 46 (2026-09-18)

Rodei a skill `code-review-and-quality` sobre as Tasks 37-46 (todo o fluxo "venda ponta a ponta") mesmo já tendo 153 testes verdes na hora — e achei três problemas reais que nenhum teste tinha pego, porque nenhum teste tinha sido escrito pensando neles:

1. **`AdicionarPagamento` com valor inválido criava um pagamento de R$ 0,00 silencioso.** Os testes existentes sempre passavam um `ValorPagamentoAdicionar` válido antes de chamar `AdicionarPagamento` — nunca testaram o caminho "usuário esqueceu de preencher". Comparar com `AdicionarItem` (que tem um fallback sensato, quantidade 1) expôs a inconsistência: dinheiro não tem "valor default razoável" nenhum, então o certo era rejeitar a ação, não inventar um valor.
2. **`FinalizarVendaAsync` não validava pagamento contra o total.** `PodeFinalizarVenda` só olhava "tem item no carrinho?" — nunca "os pagamentos cobrem o total?". Os testes que exercitavam `FinalizarVendaCommand` sempre adicionavam um pagamento com o valor certinho antes, então o caminho "esqueceu de pagar" nunca apareceu.
3. **Exceção em código fire-and-forget desaparecia silenciosa.** `ShellViewModel` dispara vários `_ = algumaCoisa.IniciarAsync()`/`_ = AposLoginAsync(...)` sem esperar o resultado (de propósito, pra não travar a UI — ver #29). Mas nenhum desses tinha `try/catch`: uma falha ali vira uma "task nunca observada" que o .NET moderno engole sem crashar e sem avisar nada — a tela simplesmente para de reagir, sem pista nenhuma do que aconteceu. Testes normais não pegam isso porque testam o caminho de sucesso; só um teste desenhado especificamente pra injetar uma falha nesse ponto exato (usando um serviço com `contextFactory` quebrado de propósito) revelou o problema.

A lição maior: **testes verdes provam que o código faz o que os testes descrevem, não que os testes descrevem tudo que importa**. Revisão de código com foco em "o que mais eu escreveria um teste pra pegar, olhando de fora?" acha uma classe diferente de bug do que TDD acha sozinho — os dois se completam, nenhum substitui o outro.

## 38. Teste que passa sozinho mas falha na suíte inteira é bug de teste, não "azar"

**Onde:** `SistemaPDV.Tests/DashboardViewModelTests.cs` (`NovaVendaCommandEmiteQuandoExecutado`) — Fase 5 (pdv-ui), Task 47

Ao rodar a suíte completa depois da Task 47, um teste antigo (`NovaVendaCommandEmiteQuandoExecutado`, da Task 46) falhou — sem eu ter mexido em nada relacionado a ele — e passou de primeira quando rodado sozinho. Esse padrão ("verde isolado, vermelho na suíte") é a assinatura clássica de um teste que depende de **timing**: o xUnit roda classes de teste em paralelo, então a suíte inteira mexe com a ordem em que threads e schedulers correm, e um teste que só acerta "por sorte" numa execução calma começa a errar quando tem mais concorrência.

A causa: o teste assinava o comando com `Subscribe(_ => disparou = true)` e logo depois de `await Execute()` conferia `Assert.True(disparou)`. Isso pressupõe que o callback do `Subscribe` já rodou quando o `await` volta — mas quem entrega essa notificação é o scheduler do ReactiveUI, e nada garante que ela chegue *antes* da linha seguinte. É a mesma família de bug do #31 (esperar a entrada em vez do sinal de saída), só que agora num teste que eu mesmo tinha escrito e considerado "simples demais pra falhar".

A correção foi o padrão já estabelecido: assinar **antes** de disparar, com `FirstAsync().ToTask()`, e `await` esse sinal explicitamente. Rodei a suíte 3 vezes seguidas pra confirmar que ficou estável. A regra prática: se um teste falha só de vez em quando, ou só junto com outros, trate como bug real de sincronização — não rode de novo "pra ver se passa".

## 39. Reaproveitar um serviço de leitura em lote, em vez de consultar item por item

**Onde:** `Services/Sales/VendaLocalService.cs` — Fase 5 (pdv-ui), Task 47

A lista de pedidos precisa, pra cada venda, do nome do cliente, do operador e dos nomes das formas de pagamento — dados que moram em outras tabelas. O jeito ingênuo (dentro de um laço sobre as vendas, buscar o cliente daquela venda, depois as formas de pagamento daquela venda) faz 1 consulta pra listar + N consultas extras: o mesmo N+1 que já corrigimos em `CatalogSyncService` (#22). Aqui a mesma lição foi aplicada *já na primeira versão*, sem esperar uma revisão de código apontar: primeiro carrega todas as vendas de uma vez (`Include` de itens e pagamentos), junta os ids de cliente/forma de pagamento que aparecem, e busca cada tabela **uma vez só** com `Where(x => ids.Contains(x.Id))`, montando dicionários pra resolver o nome em memória. Total: 4 consultas fixas, não importa se a lista tem 5 ou 500 vendas.

## 40. Navegação livre entre telas exige decidir o que acontece com o estado de quem sai

**Onde:** `ViewModels/ShellViewModel.cs` (`IrParaDashboardCommand`/`IrParaPdvCommand`/`IrParaListaPedidosCommand`) — Fase 5 (pdv-ui), Task 47

Até a Task 46 o Shell só andava "pra frente" (login → caixa → dashboard → venda), sem como voltar. A Task 47 exigiu uma barra de navegação persistente (Painel / Nova Venda / Pedidos), habilitada só com caixa aberto — o `CanExecute` de cada comando é `WhenAnyValue(vm => vm.CaixaAberto).Select(c => c is not null)`, reativo, então os botões acendem sozinhos assim que o caixa abre.

Mas construir a navegação expôs uma pergunta que o fluxo linear escondia: **e o que acontece com a tela que a gente está deixando?** Hoje cada `IrPara*` cria um ViewModel novo — então sair da tela de venda no meio de uma venda e voltar **descarta o carrinho**. Registrei isso como critério não atendido em vez de marcar como feito, porque é uma decisão de produto (reaproveitar a mesma instância enquanto o caixa for o mesmo? bloquear a navegação com venda em andamento?) que não é minha de tomar sozinha.

**Resolução (2026-09-20):** o usuário escolheu bloquear a navegação enquanto há venda em andamento. `PdvViewModel` ganhou `TemVendaEmAndamento` (item **ou** pagamento já digitado — só pagamento também conta, senão sair descartaria dado do operador), o Shell espelha isso em `VendaEmAndamento` e o `CanExecute` dos três comandos de navegação virou `temCaixaAberto.CombineLatest(semVendaEmAndamento, (a, b) => a && b)`. Repare que "Nova Venda" também fica bloqueada estando na própria tela de venda: como `IrParaPdv` cria um `PdvViewModel` novo, clicar nele com o carrinho cheio apagaria o carrinho da mesma forma. O teste (`NavegacaoFicaBloqueadaEnquantoHaVendaEmAndamento`) assina o sinal de "bloqueou" **antes** de mexer no carrinho e o de "liberou" antes de cancelar — o mesmo padrão de esperar o sinal, não a ação (#31, #38).

## 41. Modelo de ameaça antes de código: "onde o dado de fora entra e o que sai de dentro"

**Onde:** `Services/Sync/CatalogSyncService.cs` (`SincronizarClienteNovoAsync`), skill `security-and-hardening` — Fase 5 (pdv-ui), Task 48

A Task 48 é a primeira que manda **dado pessoal** (nome, CPF/CNPJ) pra fora do dispositivo, junto com o token de acesso. Antes de escrever a linha de código, a skill pede um exercício de cinco minutos: listar as **fronteiras de confiança** (onde dado não confiável entra) e os **ativos** (o que vale proteger), e perguntar "como eu abusaria disso?". Aqui as fronteiras eram três: o cadastro digitado pelo operador, a **resposta da API** e o caminho de rede. Cada uma virou uma defesa concreta e um teste de "caso de abuso" (escrito como teste de primeira classe, não como extra):

- **Resposta da API é dado não confiável.** O `ErroApiExtractor` (compartilhado por caixa, venda e cliente) lançava exceção se `errors` tivesse um formato inesperado e, no fallback, devolvia o corpo **inteiro** — que ia parar no banco. Um wi-fi de loja com portal cativo responde `200` com uma página HTML; um erro de servidor pode despejar um stack trace. Agora ele nunca lança e trunca em 300 caracteres. Como o código era compartilhado, caixa e venda ganharam a proteção de graça.
- **Validar antes de mandar, não deixar a API descobrir.** Um CPF digitado errado voltaria como `422` a **cada ciclo de 30 segundos**, pra sempre (o cliente fica em `FalhaSync`, que é elegível pra reenvio). `DocumentoValidator` confere os dígitos verificadores localmente: documento inválido nem chega à rede. E a mensagem de erro gravada **nunca repete o documento** — o texto vai pro banco e pode ir pra tela ou pra log, e um CPF num log é vazamento.
- **Não mandar dado pessoal em texto puro.** O push só sai por HTTPS (exceto loopback, porque a API de desenvolvimento roda em `http://localhost:73`). A checagem retorna falha **sem marcar o cliente**: é problema de configuração, não do cliente — mesma distinção que `VendaSyncService` faz pra "dependência ainda não sincronizou".
- **O payload é uma allowlist.** `ClienteNovoRequestDto` declara exatamente os campos que saem. Se o código serializasse a entidade `Cliente` direto, qualquer campo interno novo (um `UltimoErroSync`, um dado sensível futuro) vazaria sem ninguém decidir isso. Com um DTO, **sair da máquina é uma decisão explícita**.
- **Nunca confiar num id devolvido.** `id` ausente, zero ou negativo não é um cliente — vira falha, em vez de gravar `IdExterno = 0` e quebrar as vendas daquele cliente depois.

## 42. `409 Conflict` nem sempre significa "já está sincronizado"

**Onde:** `CatalogSyncService.SincronizarClienteNovoAsync` — Fase 5 (pdv-ui), Task 48

Em `Venda` e `Caixa` tratamos `409` como sucesso (#4): o servidor já tem aquele registro, então a operação está feita. Copiar isso pro cliente seria um bug sutil. A diferença: no `409` da venda a gente já sabe o `guid` (nós o geramos); no do cliente, a resposta **não traz o id** do cadastro que já existe. Marcar `Sincronizado` deixaria `IdExterno` nulo — e o `VendaSyncService` recusa sincronizar qualquer venda cujo cliente não tenha `IdExterno`. Resultado: todas as vendas pra esse cliente travadas pra sempre, sem erro visível. Por isso o `409` de cliente vira `FalhaSync` com a mensagem da API, e a limitação (sem reconciliação por CPF depois) ficou registrada no `todo.md`. Regra: **antes de reaproveitar um tratamento de erro de outro lugar, confira se a premissa que o justificava ainda vale.**

## 43. Testar a decodificação, não o texto: o que o serializador escapa é JSON válido

**Onde:** `SistemaPDV.Tests/CatalogSyncServiceClienteNovoTests.cs` — Fase 5 (pdv-ui), Task 48

Um teste falhou procurando `"razao_social":"Padaria do Zé"` no corpo enviado: o `System.Text.Json` escreve `é` como `é`. Não era bug do produto — é JSON perfeitamente válido, e qualquer parser (a API inclusive) decodifica de volta pra `é`. O erro estava no teste, que comparava a **representação** em vez do **valor**. A correção foi fazer o teste parsear o JSON e comparar a propriedade já decodificada. Regra prática: quando o formato tem várias representações equivalentes (escape de unicode, espaços, ordem das chaves), a asserção deve olhar pro dado, não pro texto.

## 44. Acrescentar um valor a um enum é uma mudança em todo `if` que o consome

**Onde:** `ResultadoEnvioTipo.ConexaoInsegura` × `CaixaSyncService.SincronizarFechamentoAsync` — Fase 5 (pdv-ui), Task 48 (correções da revisão de segurança)

Para recusar `http://` em todos os endpoints, `SoftcomApiClient.EnviarAsync` passou a devolver um novo tipo, `ConexaoInsegura`. O compilador não reclamou de nada — e havia um buraco: o fechamento de caixa só tratava `Falha or TokenExpirado` como erro; **todo o resto** caía no ramo de sucesso. Uma URL insegura viraria `Sincronizado` sem que nada tivesse saído da máquina (o operador acharia que o fechamento foi confirmado). Um `if` que lista os casos ruins e trata "o resto" como bom é frágil justamente com enums que crescem. Duas defesas: (1) cada chamador ganhou uma checagem explícita de `ConexaoInsegura`, com teste próprio (e conferi que o teste do fechamento realmente **falha** quando a guarda é removida — um teste que nunca viu vermelho não prova nada); (2) fica a regra de revisar todo consumidor ao acrescentar um valor. O tratamento é o mesmo nos quatro serviços: devolver falha **sem marcar a entidade** e sem contar tentativa — é problema de configuração, não do caixa/venda/cliente.

## 45. Resposta `200` não garante corpo JSON — e o `Deserialize` lança

**Onde:** `SoftcomJson.TentarDesserializar<T>`, usado por `CaixaSyncService`, `VendaSyncService` e `CatalogSyncService`

Status HTTP de sucesso só diz que *alguém* respondeu, não *quem* nem *o quê*: portal cativo de wi-fi, proxy e gateway devolvem `200` com HTML. `JsonSerializer.Deserialize` nesse corpo lança `JsonException`, que passava sem tratamento na abertura de caixa (e propagava até quem chamasse). Foi extraído um helper que converte "não é o JSON esperado" em `null` — que os chamadores já tratavam como "a resposta não trouxe o id" — e a mensagem gravada agora passa por `ErroApiExtractor.Extrair` (truncada em 300), em vez de repetir o corpo inteiro no banco. Aproveitou-se também para trocar o `DominioSeguro` local do cliente (que aceitava qualquer esquema em loopback, ex.: `ftp://localhost`) pela regra única de `ConexaoSegura`: **uma regra de segurança duplicada em dois lugares vira duas regras diferentes**.

## 46. `Contains` não é busca: `LIKE` tem curingas, `instr` diferencia maiúsculas, e lista sem teto trava a tela

**Onde:** `CadastroLocalService.ListarClientesAsync/ListarProdutosAsync` — Fase 5 (pdv-ui), Task 49

Três armadilhas de uma busca "simples". (1) No SQLite, `string.Contains` do EF vira `instr(...)`, que **diferencia** maiúsculas — "maria" não acharia "Maria". `EF.Functions.Like` resolve (o `LIKE` do SQLite ignora caixa em ASCII; acentuação como "JOÃO" vs "joão" continua diferenciando — limitação conhecida). (2) Só que `LIKE` tem curingas: se o operador digitar `%` ou `_`, o padrão casa tudo. O texto digitado precisa ser **escapado** (`\%`, `\_`, `\`) e a chamada informar o caractere de escape — tem teste próprio. (3) Sem paginação, um catálogo real com milhares de produtos materializado numa `ListBox` congela a UI; a lista corta em 200 (`LimiteLista`) e a tela **avisa** que foi cortada, senão o operador acharia que aquilo é tudo. Escrever o escape no C# também pegou uma pegadinha do dia a dia: `"\%"` não compila (sequência de escape inválida) — o certo é `"\%"`.

## 47. Validar na entrada do formulário com a mesma regra do envio — e cuidado com "limpar" em silêncio

**Onde:** `CadastroLocalService.CriarClienteAsync` + `DocumentoValidator` — Fase 5 (pdv-ui), Task 49

O outbox (Task 48) já recusa CPF/CNPJ inválido antes de enviar, mas só marcaria o cliente como `FalhaSync` — o operador descobriria tarde e sem saber o motivo. Validar **no cadastro**, com a mesma função (`DocumentoValidator`, uma regra só), dá o erro na hora e não grava lixo; a mensagem também nunca repete o documento digitado. Um detalhe sutil: `SoDigitos` "limpa" o texto — então `"529.982.247-25x"` viraria um CPF válido e passaria em silêncio, e `"abc"` viraria vazio (tratado como "sem documento"). Antes de tirar caracteres, **verifique que só havia o que se esperava** (dígitos e a pontuação usual). Regra geral: normalizar é diferente de validar; quem normaliza sem checar aceita entrada que o usuário não pretendia. Também entrou uma checagem de documento duplicado, que evita o `409` da API mais adiante (que, como vimos em #42, não traz o id).

## 48. `DispatcherTimer` dispara na thread de UI — o trabalho, não

**Onde:** `SincronizacaoBackgroundService` — Fase 5 (pdv-ui), Task 50

Usar `DispatcherTimer` (decisão de 2026-09-18) resolve *quando* rodar sem `Dispatcher.Post` manual, mas o `Tick` executa **na thread de UI**. Se o handler chamasse a sincronização direto, o `await` não ajudaria: o provedor SQLite do EF Core é síncrono por baixo (a "async" dele não libera a thread), então gravar um catálogo com milhares de produtos congelaria a janela. Por isso o tick só faz `Task.Run(ciclo)`, e o resultado (estado de conexão) volta pra UI por um observable + `Dispatcher.UIThread.Post` (feito no `App`, o Shell nem conhece o serviço). Regra: **o timer decide o quando; nunca deixe o corpo do trabalho na thread de UI só porque o timer está nela.** Consequência: o serviço passa a rodar em paralelo com a UI, que também lê/grava o mesmo `pdv.db` — o SQLite serializa as escritas (espera até 30 s por padrão), mas fica como risco a observar; ligar o modo WAL seria o próximo passo se houver travadinhas ao vender durante uma sincronização grande.

## 49. Ciclos periódicos: testar o ciclo, não o timer — e três guardas que a rede exige

**Onde:** `SincronizacaoBackgroundServiceTests` (21 testes) — Task 50

Um timer real é ruim de testar (precisa de `Dispatcher`, espera de 30 s). A saída é separar a **cola** (`Iniciar/Parar`, dois `DispatcherTimer`) da **lógica** (`ExecutarCiclo*Async`, métodos públicos que os testes chamam direto com um `HttpClient` falso que responde por rota e registra as requisições). Três guardas nasceram de perguntar "o que acontece se…": (1) **ciclo lento** — se o anterior ainda roda quando o próximo tick chega, um semáforo com `WaitAsync(0)` faz o novo ser *ignorado* (não enfileirado, senão os ticks se acumulam), o que também impede push e pull de clientes ao mesmo tempo; (2) **ocioso não custa rede** — sem pendência, o ciclo de 30 s nem pede token (uma requisição a cada 30 s sem motivo); (3) **catch-up do catálogo** — logo depois de vincular o dispositivo, o catálogo é baixado no próximo tick de 30 s e não só no de 5 min, senão ninguém consegue logar (funcionários vêm do catálogo). Também: cada etapa do outbox tem o próprio try/catch — um cliente rejeitado não trava o caixa que o operador espera —, e um laço com teto (20) repete `SincronizarCaixaPendenteAsync`, que só trata **um** caixa por chamada.

## 50. Retentativa: contar e agendar no banco, não na memória — e "desistir" precisa de saída

**Onde:** `PoliticaRetentativa`, migration `AddRetentativaOutbox` — Fase 5 (pdv-ui), refinamento da Task 50

Um item com erro permanente (422) era reenviado a cada 30 s para sempre, com um token novo a cada vez. A política nova: espera que dobra (30 s → 10 min) e teto de 8 tentativas, depois o item para de ser reenviado sozinho. Duas decisões de desenho: (1) **o contador e o "próxima tentativa" ficam no banco** (`TentativasEnvio`, `ProximaTentativaEm`), não num dicionário em memória — senão fechar e reabrir o app "perdoaria" o erro e o ciclo recomeçaria do zero; (2) **desistir tem que ter saída**: sem o botão "Reenviar falhas" (que zera o contador), um item esgotado ficaria preso para sempre sem o operador saber — por isso a última mensagem gravada diz "parou de tentar… use Reenviar falhas". O filtro "pode tentar agora" é uma única expressão (`Elegivel<T>`) usada em todos os lotes **e** na checagem de "há pendência?" do serviço de background — se cada lugar reescrevesse a condição, um esqueceria a política e o app voltaria a autenticar a cada 30 s à toa. Envio explícito de UM item (`SincronizarClienteNovoAsync(id)`) ignora a espera de propósito; só o lote automático a respeita. O tempo entra por `TimeProvider` (parâmetro opcional, padrão do sistema) — os testes avançam um `RelogioFalso` em vez de dormir.

## 51. Atualização automática de telas: o Shell avisa a tela aberta, ninguém assina evento

**Onde:** `IAtualizavelPorSincronizacao`, `ShellViewModel.NotificarDadosSincronizados` — refinamento da Task 50

"A lista atualiza sozinha" parece pedir que cada ViewModel assine um evento do serviço de sincronização. Mas os ViewModels são **recriados a cada navegação**: cada assinatura esquecida vazaria e continuaria recarregando telas que ninguém vê. Em vez disso, o serviço publica um único `DadosAlterados`, o `App` leva o aviso à thread de UI e o Shell — que sabe qual é a tela atual (`CurrentViewModel`) — chama `AtualizarAposSincronizacaoAsync` só se ela implementa a interface. Duas sutilezas: a tela de venda **não** implementa (recarregar catálogo no meio de um carrinho seria pior que deixar desatualizado), e Cadastros recarrega com a **busca já aplicada** (a do último Enter), nunca com o que o operador está digitando agora — senão o filtro mudaria sozinho no meio da digitação. O serviço só avisa quando algo mudou (envio tentado, ou catálogo com itens); um ciclo ocioso não recarrega ninguém.

## 52. Substituir texto com `perl -pi` e `$`: o `$1`, o `$13` e o `$"` bitem

**Onde:** edições em `CadastrosView.axaml`, `CadastrosViewModel.cs`, `ListaPedidosViewModel.cs` — Task 50

Três vezes no mesmo dia uma substituição por regex quebrou o arquivo em silêncio, porque no lado do "substituto" o Perl interpreta `$`: `$13` (grupo 13, que não existe — comeu a tag `<TabControl …>` inteira), `$"` (o separador de listas — apagou o início de uma string interpolada do C# `$"{x} …"`) e um `\r?` que casou onde não devia. O compilador pegou os três, mas só depois. Regra: para trechos com `$` (C# interpolado, XAML com `{Binding}`), usar a ferramenta de edição de texto exato (`Edit`) em vez de regex; e depois de qualquer edição automática em massa, **compilar antes de seguir** — o erro aparece a 1 linha de distância em vez de 3 tarefas depois.

## 53. Cinco tarefas verdes e a sincronização nunca funcionou: o servidor falso era permissivo demais

**Onde:** `CatalogoApiRealTests`, `SoftcomRotas` — teste contra a API real (2026-09-20)

Ao rodar o app de verdade, "Chave inválida" levou a uma investigação (com autorização do usuário: o mesmo código do app numa **cópia** do banco, contra a API real) que mostrou: os 5 recursos do catálogo falhavam. Causas: (1) **5 das 7 rotas estavam sem o prefixo `softauth/`** (a API responde `500 {"error":""}` para rota inexistente — nada de 404); (2) formas de pagamento vêm **embrulhadas num array** (`[ { "data": [...] } ]`); (3) `bloqueado` e `vender` são booleanos de verdade, e não `"0"/"1"`; (4) `estoque` e preços vêm como **texto** decimal; (5) vários campos da empresa vêm `null` onde o DTO esperava inteiro. Nada disso aparecia nos testes porque o `FakeHttpMessageHandler` **respondia qualquer URL com o JSON que o teste mandasse** — os DTOs eram testados contra o formato que a gente supunha, nunca contra o que a API entrega. Correção de método: um teste de contrato com um servidor falso que **imita o real** (rota fora de `softauth/api/v2/` = 500 `{"error":""}`, respostas com as formas observadas, incluindo os `null`s e o array na raiz), e as rotas num lugar só (`SoftcomRotas`). Regra: **um servidor falso que aceita tudo confirma só o que você já acreditava**; pelo menos uma vez por integração, olhe uma resposta real (só os tipos, se houver dado pessoal) e transforme-a em teste.

Os endpoints de **escrita** (abrir/fechar caixa, venda, criar cliente) também estavam sem o prefixo e foram movidos para `softauth/api/v2/`, mas isso é inferência (GET nessas rotas sem o prefixo dá o mesmo `500` vazio, com o prefixo dá "Resource not found", ou seja, a rota existe): **não foi testado com POST real**, para não criar dado de verdade — conferir no primeiro caixa/venda de teste.

## 54. `pdv_key` da API é um hash bcrypt — guardar SHA-256 dele nunca casaria

**Onde:** `PdvKeyHasher`, `LoginOperadorService`, `CatalogSyncService.SincronizarFuncionariosAsync`

O app assumia que a API devolvia a `pdv_key` do operador em claro e guardava `SHA-256` dela; o login comparava `SHA-256(digitado)`. Na API real o campo tem **60 caracteres e prefixo `$2y$10$`**: é um hash **bcrypt** (13 dos 14 funcionários). `SHA-256(hash bcrypt)` jamais é igual a `SHA-256(chave)`, então mesmo com a sincronização consertada **nenhuma chave passaria**. Agora o hash bcrypt é gravado como veio (já é um verificador; não permite recuperar a chave) e o login confere a chave digitada contra cada hash com bcrypt (pacote `BCrypt.Net-Next` 4.0.3, sem vulnerabilidades conhecidas — o .NET não tem bcrypt embutido). Detalhes de desenho: (1) valor que **não** tem cara de bcrypt (vazio, ou uma chave em claro se a API mudar) é **ignorado**, nunca gravado; (2) `Verificar` nunca lança — hash em claro, `SHA-256` do formato antigo ou nulo simplesmente não confere; (3) como o login não pede usuário, tenta-se cada funcionário ativo, e bcrypt é lento de propósito (~dezenas de ms por hash, custo 10) — por isso roda em `Task.Run`, fora da thread de UI; (4) o prefixo `$2y$` (PHP/Laravel) é aceito além de `$2a$/$2b$`.

## 55. "A primeira da lista" não é "a do dispositivo": o critério estava no link de vínculo

**Onde:** `SoftcomAuthService.ExtrairEmpresaCnpj`, `CatalogSyncService.SincronizarEmpresaAsync`, `VendaSyncService`

A API devolve todas as empresas do cliente (4, no dispositivo real), mas o dispositivo pertence a uma só — e o app vendia pela `Empresas.FirstOrDefault()`, que só acertava por sorte (a do dispositivo era a de `empresa_id=1`). O critério estava o tempo todo no **link de vínculo** (`empresa_cnpj`), que já ficava gravado em `UrlApi`. Não precisou de tela de escolha nem de migração: extrai-se o CNPJ do link (`ExtrairEmpresaCnpj`) e (1) a sincronização grava **só** essa empresa — as outras trariam junto certificado digital e senha, dado sensível sem uso —, (2) a venda usa a empresa com esse CNPJ, e (3) sobras de versões antigas (as 4 gravadas) são removidas na próxima sincronização (nenhuma FK aponta para `Empresa`; é dado que se baixa de novo). Casos de borda tratados: sincronização **incremental** que devolve página vazia ou só outras empresas não é erro se a local já existe; link antigo sem CNPJ com uma única empresa usa essa, e com várias **não adivinha** (pede para vincular de novo). Regra: quando o ambiente devolve "todas as opções", o critério de escolha costuma estar num dado que você já tem — procure antes de perguntar ao usuário ou de escolher a primeira.

## 56. O corpo de POST tem que ser o que a API EXIGE, não o que o Swagger lista: descobrir pelo 422

**Onde:** `VendaRequestDto`, `Venda.NumeroPedido`, `Produto.ProdutoIdApi` — venda contra a API real (2026-09-20)

O primeiro POST de venda real voltou `422` com `{"errors":{"numero_documento":[…],"cancelada":[…],"bloqueada":[…],"produtos.0.produto_empresa_grade_id":[…]}}`. O Swagger tem ~100 campos, quase todos "nullable"; o que a API **realmente** exige só aparece quando ela recusa — e a mensagem do app, sem o nome do campo, dizia "É obrigatório." quatro vezes. Três lições: (1) **o extrator de erro precisa manter o nome do campo** (agora `numero_documento: É obrigatório.`); (2) **um produto pode ter vários ids** — na API real `id=206`, `produto_id=77`, `produto_empresa_id=154` (196 de 200 produtos têm `id ≠ produto_id`): a venda pede o `produto_id` **e** o `produto_empresa_grade_id`, e o app mandava o `id` como `produto_id`, ou seja, registraria OUTRO produto (o mapeamento adotado — grade = `id` da listagem, produto = `produto_id` — é hipótese a confirmar olhando o item da primeira venda no SoftcomShop); (3) **dado que passa a ser necessário depois** exige backfill: os 200 produtos já no banco não tinham `produto_id`, e a sincronização incremental não os traria de novo — enquanto algum produto local estiver sem ele, a sincronização de produtos busca tudo. Também nasceu o **número do pedido** (`NumeroPedido`): sequencial e único no dispositivo, gravado na criação da venda (offline), enviado como `numero_documento` (string) e mostrado na lista de pedidos no lugar do "—". A migração numera as vendas existentes pela ordem de criação antes de criar o índice único (senão todas ficariam com 0 e o índice falharia).

**Continuação do #56 — a venda real foi aceita (HTTP 200, id 333):** depois do 422 de validação, a API ainda devolveu três `500` **de gravação**, um por vez, cada um revelando o campo seguinte que o servidor lê: item sem `preco_compra` (`Column 'preco_compra' cannot be null`), financeiro sem `api_nome_pagamento` (`Undefined index`), parcela sem `valor_parcela`. As tentativas falhas **não deixaram venda pela metade** (a seguinte deu 500 de novo, não 409), o que permite iterar. Ficou no corpo: no item, `preco_compra`, `comissao`, `comissao_atendente`, `percentual_comissao_venda`, `composicao_automatica`, `promocao_aplicada`; no pagamento, `api_nome_pagamento`, `api_codigo_pagamento` (nome e código NFC-e da forma) e, à vista, uma parcela (`valor_parcela`, `valor_recebido`, `parcelas=1`, `numero_parcela="1"`). Regra: um **500 com texto** de uma API PHP ("Undefined index: x", "Column 'x' cannot be null") é o mapa do que falta — leia o texto inteiro antes de adivinhar. Armadilha do **reenvio depois do sucesso**: a venda foi criada no servidor mas o banco do app ainda a marcava com falha; reenviar o mesmo `guid` pode duplicar se a API não deduplicar. A venda foi reconciliada no banco local (`Sincronizado`, `VendaIdExterno=333`) antes de reabrir o app.

## 57. "Pendente sem erro" precisa dizer o que espera — e não exigir configuração que dá pra deduzir

**Onde:** `VendaSyncService.AguardarAsync`, detecção do Consumidor Final

A 2ª venda de teste ficou 🟡 e sem nenhuma mensagem. Causa: era venda **avulsa** (sem cliente) e o app exigia o "id do Consumidor Final" preenchido à mão em Configurações — vazio. A regra "espera por dependência não vira falha" está certa (não é culpa da venda e não deve gastar tentativa), mas era **totalmente silenciosa**: nada na tela, nada no log. Dois consertos: (1) **o app deduz o Consumidor Final**: é o cliente que a API marca com `indicador_finalidade = 1` (id 1, "CONSUMIDOR"); a configuração manual continua valendo e tem prioridade; (2) **toda espera grava o motivo** em `UltimoErroSync` (a lista já o mostra em vermelho abaixo da linha) sem mudar o status nem contar tentativa, e o motivo some sozinho quando a venda é enviada. Regra: um estado "aguardando" sem explicação é indistinguível de "travado" — o usuário só descobriria olhando o banco (foi o que aconteceu aqui); e uma configuração obrigatória que o sistema consegue deduzir dos dados que já sincronizou não devia ser obrigatória.

## 58. Exceção em comando do ReactiveUI derruba o app — e no v23 o handler se registra no builder

**Onde:** `TratamentoDeErros`, `Program.cs` (`WithExceptionHandler`) — achado na revisão de código antes do push

Nenhum dos ~20 `ReactiveCommand` do app tratava exceção (nem `ThrownExceptions`, nem handler global). No ReactiveUI, uma exceção dentro de um comando que ninguém escuta vai ao **handler global, que por padrão faz debugger break + `UnhandledErrorException` e derruba o processo** — já tinha derrubado o host de testes uma vez (`UnhandledErrorException`), e no app real significaria fechar no meio de uma venda por um banco travado. Os testes passavam porque os comandos nunca lançavam. Correção: um observador estático (`TratamentoDeErros.Observador`) registrado no builder (`.UseReactiveUI(b => b.WithExceptionHandler(...))` — no v23 `RxApp.DefaultExceptionHandler` não existe e `RxState.DefaultExceptionHandler` é só leitura) que repassa a exceção ao banner do Shell e **nunca lança**. O registro tem que ser feito **antes** de existir qualquer tela, por isso o destino é definido depois, pelo `App`. O teste de integração só prova algo se o inicializador de testes espelhar o `Program.cs` — e removê-lo derruba o processo de testes (verificado). Também nesta revisão: `ExtrairEmpresaCnpj` passou a exigir 11 ou 14 dígitos (um `empresa_cnpj=123` truncado viraria "a empresa do dispositivo" e a limpeza apagaria as empresas locais).

## 59. `NumberStyles.Number` com cultura invariante lê "10,50" como 1050 — dinheiro e quantidade precisavam de um leitor próprio

**Onde:** `ValorMonetario`, `AbrirCaixaViewModel`, `PdvViewModel` — achado ao escrever a tela de fechar caixa

O app lia troco, quantidade e valor de pagamento com `decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture)`. `NumberStyles.Number` inclui `AllowThousands`, e na cultura invariante o separador de milhar é a **vírgula**: um brasileiro digitando `10,50` tinha **1050** lido, `0,5` (kg de uma venda por peso) virava **5**, e um pagamento de `12,50` virava R$ 1.250,00. Os testes usavam `"19.80"` (ponto), então nunca pegaram. Correção: um leitor único (`ValorMonetario.TentarLer`) em que vírgula **ou** ponto valem como decimal, `1.234,56`/`1,234.56` leem o último separador como decimal, e o que é **ambíguo é recusado** (`1.234` em dinheiro: milhar ou fração? — pede pra digitar de novo, em vez de adivinhar). Dinheiro aceita 2 casas, quantidade 3 (venda por peso). A apuração do fechamento vem pré-preenchida no formato brasileiro (`35,00`). Regra: **parse de entrada de usuário precisa de teste com o formato do usuário** (vírgula, milhar, espaço), e "errado em silêncio" (1050) é pior que "recusado" (mensagem).

## 60. Fechar caixa: o que o fechamento resume tem que chegar antes — e id local não é id da API de novo

**Onde:** `FecharCaixaViewModel`, `CaixaSyncService`, `SincronizacaoBackgroundService` — Task 51

Faltava a **tela de fechar caixa**: o 1º critério de sucesso da spec ("… vender → fechar caixa") não era atendido e a tarefa não estava no plano (o back-end existia desde a Fase 3). Ao ligá-la apareceram dois defeitos que só o fluxo completo mostra: (1) o envio mandava `forma_pagamento_id` com o id **local** da forma (ex: 1) e não o da API (ex: 5) — a apuração cairia na forma errada (mesma classe do bug do produto, #56); (2) o ciclo do outbox enviava **abertura → fechamento → vendas**: o fechamento *resume* o caixa, e chegando antes das vendas a API fecharia um caixa "sem vendas". A ordem agora é **abrir → vendas → fechar** (teste que registra as requisições na ordem). Desenho da tela: a conferência vem **pré-preenchida** com o que as vendas somaram por forma (o operador só ajusta o que divergiu), o troco final é informado, e fechar só grava local (funciona offline; a API recebe pelo outbox). Depois de fechar, com `ExigirAberturaCaixa` volta a "Abrir caixa" (o operador continua logado); sem a exigência, cai no Dashboard. Fica de fora do v1: apuração de bandeiras de cartão (`digitacao_bandeiras`, vai vazia).

## 61. Carregar os dados ANTES de mostrar a tela — e um turno fechado não reabre

**Onde:** `ShellViewModel.IrPara*Async`, `CaixaService.TurnosUsadosAsync`, `AbrirCaixaViewModel.IniciarAsync` — depois de o usuário fechar o 1º caixa real

**1) O que o usuário viu.** Fechou o caixa (o fechamento **chegou à API**: o caixa 28 lá tem `data_fechamento`), voltou a "Abrir caixa" e a tela sugeriu "Turno 1" de novo → *"Já existe um caixa local para esse funcionário, data e turno."* A regra está certa (um caixa por operador+data+turno, igual à API; um turno fechado **não reabre**), mas a tela ia contra ela: sugeria o turno já usado e a mensagem não dizia o que fazer. Agora a tela sugere o **primeiro turno ainda livre** de hoje (`TurnosUsadosAsync`) e a mensagem diz "o turno N de hoje já foi usado… escolha outro turno".

**2) Carregar antes de mostrar.** As telas eram exibidas vazias e carregavam em segundo plano ("fire-and-forget"). Isso é ruim pro operador (a tela se enche sozinha; na de fechar caixa dava pra clicar em **Fechar caixa antes de a apuração chegar** e fechar sem conferir nada) e criava corrida: a carga em paralelo mexia no banco ao mesmo tempo que a ação seguinte — nos testes, com a conexão única do SQLite em memória, isso virava falha **intermitente** (`unable to delete/modify user-function due to active statements`, ~2 em 10 execuções). Agora todo `IrPara*` é `async`: cria o ViewModel, **`await IniciarAsync()`** e só então troca `CurrentViewModel`/`TelaAtual`; o turno sugerido já vem certo quando a tela aparece (sem piscar "Turno 1"). Verificado com 15 execuções seguidas da suíte (antes: falhava). Regra: **flakiness que aparece "de vez em quando" quase sempre é trabalho em segundo plano que ninguém aguarda** — em vez de esperar com `Delay` no teste, faça o código aguardar antes de expor o estado.

## 62. Regra de negócio importante vale nas três camadas: serviço, tela e envio

**Onde:** "só fecha o caixa sem venda pendente" — `CaixaService`, `FecharCaixaViewModel`, `CaixaSyncService`

Decisão do usuário: o fechamento **só pode acontecer sem venda pendente** (o fechamento resume o caixa na API; venda que ainda não chegou ficaria de fora). Aplicar a regra num lugar só deixa furos: (1) **o serviço** (`FecharCaixaLocalAsync`) é a fonte da verdade — recusa e devolve o motivo, então nenhuma tela futura consegue fechar "por engano"; (2) **a tela** só antecipa: avisa quantas vendas faltam, diz o que fazer ("Reenviar falhas") e bloqueia o botão, e se **atualiza sozinha** quando a sincronização em background termina (o operador não precisa sair e voltar) — mais um botão "Verificar de novo"; (3) **o envio do fechamento** também espera, cobrindo o caixa que já estava fechado antes da regra e a venda que voltou a falhar depois. A ordem do outbox (abrir → vendas → fechar) só garante a *sequência*, não que as vendas *deram certo*. Um teste por camada (o da tela chama `Execute()` mesmo com o botão desabilitado, pra provar que o serviço segura). Consequência que fica registrada: uma venda com erro **permanente** passa a travar o fechamento — falta uma forma auditável de descartá-la.


## 63. Descartar uma venda que nunca vai ser aceita: auditoria em vez de DELETE, e a autorização é de quem responde por ela

**Onde:** `VendaLocalService.DescartarVendaAsync`, `VendaFiltros`, `ListaPedidosViewModel`/`View` — consequência direta do aprendizado #62

Depois de exigir "fechar caixa só sem venda pendente" (#62), uma venda que a API **nunca** aceita passou a travar o caixa para sempre. A saída óbvia seria apagar a linha — mas apagar venda é destruir registro fiscal/financeiro sem rastro. Decisões (do usuário, com o porquê):

1. **Não apaga: muda de estado.** Novo `SyncStatus.Descartada` + quatro colunas de auditoria (`DescartadaEm`, `DescartadaPorId`, `SolicitadaPorId`, `MotivoDescarte`). A venda segue na lista como "⚫ Descartada por Fulano: motivo". Como o enum é gravado como texto, o valor novo não precisou de migração — só as colunas.
2. **Só supervisor autoriza, digitando a chave dele.** Quem *pede* é o operador logado (`SolicitadaPorId`); quem *autoriza* é o supervisor (`DescartadaPorId`), conferido por bcrypt fora da thread de UI. Chave errada, chave de não-supervisor e supervisor desativado dão **a mesma mensagem** — não dá pra descobrir quem é supervisor testando chaves. O campo de senha é limpo depois de toda tentativa; o motivo digitado fica (o operador não redigita).
3. **Só em falha (`FalhaSync`).** Uma venda `PendenteSync` ainda vai ser enviada — descartá-la seria perder dinheiro por engano.
4. **Trata como cancelada:** fora do "esperado" do fechamento, do faturamento e da contagem de pendentes. Em vez de repetir `SyncStatus != Sincronizado && != Descartada` em cada serviço (e esquecer um), os dois filtros moraram num só lugar, `VendaFiltros.NaoEnviada` e `VendaFiltros.Valida`. Adicionar outro estado terminal no futuro é mexer em um arquivo.

Lição geral: **toda regra que bloqueia precisa de uma saída auditável** — senão o operador acaba pedindo acesso ao banco pra "resolver". E o envio (`VendaSyncService`) recusa explicitamente uma `Descartada`, mesmo que o filtro do lote já a ignore: defesa em profundidade contra um dia alguém chamar o envio individual.


## 64. Número único por empresa com vários PDVs offline: prefixo por dispositivo, não contador central

**Onde:** `NumeroDocumento`, `ConfiguracaoSincronizacao.CodigoPdv`, `VendaSyncService`, tela de Configurações

A API exige `numero_documento` único **por empresa**, e cada PDV gera o número sozinho, offline. Com o `NumeroPedido` sequencial de cada dispositivo (1, 2, 3…), dois PDVs da mesma empresa colidiriam já na primeira venda. As três saídas (e por que ficou a primeira, escolha do usuário):

1. **Prefixo por dispositivo** (escolhida): `02-000045`. Funciona 100% offline, sem depender da API e sem coordenar nada em tempo de venda — a coordenação acontece **uma vez**, quando alguém dá um código a cada PDV.
2. Faixa numérica reservada por PDV: continua só dígitos, mas a faixa acaba e alguém precisa controlar quem tem qual.
3. Número dado pelo servidor: sem buracos, mas **não funciona offline** — quebra o princípio central do app.

Detalhes que valem lembrar: (a) o **`#N` da tela continua o sequencial simples** — o balcão fala "venda 45", não "02-000045"; o número composto só existe no envio, montado na hora (`Formatar`), então mudar o código não mexe no banco de vendas. (b) O **hífen é proibido no código**: é o separador, e assim dois pares (código, número) diferentes nunca produzem o mesmo texto. (c) Sem código configurado o número segue puro — uma empresa com um PDV só não precisa mudar nada, e as vendas já enviadas (`1`, `2`) não colidem com as novas (`01-000003`). (d) Limite honesto: **o app não tem como saber que outro PDV usa o mesmo código** — a unicidade entre dispositivos depende de quem configura, por isso a tela avisa. Código inválido não salva nada (nem o resto do formulário), para o operador não achar que valeu.


## 65. Antes de "provar" com a API, olhe o que o banco local já provou — e proteja o laço de paginação

**Onde:** `SoftcomApiClient.BuscarTudoAsync`, item "mais de 200 produtos" do backlog

Dúvida: se o catálogo passa de uma página (`per_page=200`), a sincronização pega tudo? Em vez de chamar a API real (que exigiria token e segredo), contei os produtos numa **cópia** do `pdv.db`: **201**. Como uma página tem 200, o 201º só chegou se a segunda página foi seguida — a paginação já funciona no mundo real, com custo zero e sem tocar em credencial. Lição: dado que o próprio sistema já gravou costuma ser prova mais barata (e mais segura) que uma nova chamada.

O que a evidência **não** cobre é o caso ruim: uma API que devolve a mesma `next_page_url` para sempre prenderia o ciclo de sincronização num laço infinito, baixando a mesma página. O laço agora guarda as URLs já visitadas e falha com mensagem clara ao repetir uma. Dois testes novos: cinco páginas encadeadas lidas na ordem, e o caso do laço (para em 2 chamadas).


## 66. Log: o que registrar, o que NUNCA registrar, e o log que não pode derrubar o app

**Onde:** `LogArquivo`, `Registro`, ganchos em `OutboxHelper`, `SincronizacaoBackgroundService`, `TratamentoDeErros`, `App`

O app engolia falhas de propósito (um cliente que falha não trava o lote; uma exceção de comando vira banner) e todos os comentários diziam "sem infraestrutura de log ainda". Em campo isso significa "deu erro" sem ninguém saber por quê. O log resolve isso — mas é também o lugar mais fácil de vazar segredo e de derrubar o app, então as regras vieram antes do código:

1. **Nunca lança.** Disco cheio, pasta sem permissão, arquivo aberto por outro programa: tudo é engolido dentro do próprio log. Um log que causa crash na hora da venda é pior que não ter log. Teste: uma pasta impossível de criar não lança.
2. **Nunca grava segredo nem dado pessoal.** As mensagens de erro da API trazem o corpo da resposta, que pode ter token, CPF, etc. Tudo passa por `Mascarar` antes de ir pro arquivo (token, `client_secret`, `pdv_key`, hash bcrypt, `Bearer …`, CPF e CNPJ). O teste de `Authorization: Bearer <token>` **pegou um vazamento real** na 1ª versão: a regra de chave/valor mascarava só a palavra "Bearer" e deixava o token — foi preciso aceitar o `Bearer ` opcional antes do valor. Lição: teste a máscara com o formato exato em que o segredo aparece, não só com o caso feliz.
3. **Tem limite.** 5 MB por dia (passou, avisa uma vez e para) e 14 dias de retenção: o log não pode encher o disco de um PDV.
4. **Registra mudança, não repetição.** Um PDV sem internet publica "Offline" a cada 30 s; uma linha por ciclo encheria o arquivo sem dizer nada de novo. O estado da conexão só vai ao log quando **muda** — o arquivo conta a história ("caiu às 14:02, voltou às 14:20").
5. **`Registro` estático, `LogArquivo` por instância.** Os serviços já eram construídos em dezenas de testes; passar um logger por construtor mudaria todas essas assinaturas. `Registro` (mesmo padrão de `TratamentoDeErros`) fica em no-op até o `App` definir o destino — então a suíte de testes nunca escreve arquivo por acidente, e o `LogArquivo` em si é testável isolado.

Onde ficam: `%LOCALAPPDATA%\SistemaPDV\logs\pdv-AAAAMMDD.log` (pasta do usuário: sempre gravável, mesmo com o app em Program Files). Limite conhecido: a máscara é por padrão (regex); um dado pessoal em formato inesperado (ex: nome do cliente dentro de uma mensagem da API) não seria reconhecido — por isso o log não grava corpo de requisição, só mensagens de erro.

**Revisão de código do log (achados corrigidos antes do push):** (a) a máscara `token` por palavra solta apagava o diagnóstico mais comum — "Access token expired." virava "Access token ***"; o separador agora **precisa** de `:` ou `=` (só a palavra na frase não é um segredo). Mascarar demais custa tanto quanto de menos: um log que esconde o motivo do erro não serve. (b) Mensagem vinda da API com `\n2026-… [INFO] …` **forjava uma entrada falsa** no arquivo (log injection): toda linha depois da primeira agora tem recuo, então só uma entrada legítima começa na coluna 0. (c) A mensagem de falha do envio dizia "a API recusou", mas o mesmo ponto também recebe token expirado e erro de rede — passou a dizer só "Falha ao enviar …". Nos três casos o teste foi escrito **antes** da correção e falhou primeiro.


## 67. Duas ações concorrentes na mesma linha: reproduza a corrida num teste antes de "consertar"

**Onde:** `VendaSyncService.MarcarFalhaAsync`, `DescartarVendaAsync` — achado na revisão de código do descarte (#63)

O envio retenta vendas em `FalhaSync` (até 8 vezes) e o supervisor pode descartar uma dessas vendas. Se o descarte cai no meio de um envio, os dois escrevem na mesma linha. Minha 1ª hipótese na revisão foi "a venda descartada ressuscita como falha". **Estava errada em parte** — só descobri porque escrevi o teste que faz o descarte *dentro* da requisição HTTP simulada (a janela exata entre ler a venda e gravar o resultado): o `SyncStatus` não é regravado (o EF só grava colunas que mudaram, e `FalhaSync` → `FalhaSync` não mudou), então a venda **continua Descartada**. O que realmente vazava era menor: a tentativa que falha depois do descarte ainda **contava tentativa e sobrescrevia o erro** numa venda já descartada. Lição: uma hipótese de corrida é só uma hipótese até um teste a reproduzir — sem ele eu teria descrito o defeito errado.

Regras adotadas (decisão do usuário: descartar qualquer venda em falha, sem esperar as 8 tentativas, e reconferir antes de gravar): (1) **falha depois do descarte não grava nada** — `MarcarFalhaAsync` reconfere no banco (`AsNoTracking`) e devolve "descartada durante o envio"; (2) **sucesso depois do descarte: a verdade da API vence** — a venda já está lá, então fica `Sincronizado` (senão sairia do "esperado" do caixa por engano), a auditoria do pedido de descarte é preservada e um aviso vai pro log. A janela que sobra (entre a reconferência e o `SaveChanges`) é de milissegundos e o pior efeito é benigno.

**Achado da revisão da numeração por PDV (#64):** o código está correto, mas o **formato `01-000005` ainda não tinha sido aceito pela API real** — as vendas reais 3 e 4 saíram com o número puro. **CONFIRMADO em seguida (usuário, 2026-09-20):** com o código `01` configurado pelo botão de Configurações, a venda #5 sincronizou normalmente (id 337 na API; conferido no banco local). Como confirmar: configurar um código e fazer uma venda; se a API recusar, o plano B é um prefixo só numérico (`<código numérico><sequencial de 6 dígitos>`).


## 68. Uma tela que ninguém consegue abrir não existe: campo novo precisa de caminho até ele

**Onde:** `ShellViewModel.IrParaConfiguracoesCommand`, `ConfiguracoesViewModel` (porta de entrada com chave), `SupervisorAutenticador`

Ao criar o campo "Código deste PDV" (#64) eu o coloquei na tela de Configurações — que só abria sozinha no **1º uso**, com o dispositivo ainda sem vínculo (`IrParaConfiguracoes` era privado). Depois de vinculado, **não havia botão**: o código do PDV, "Exigir abertura de caixa" e o Consumidor Final ficaram inalcançáveis. Só apareceu quando o usuário perguntou "como faço para ir nas configurações?". Lição: ao entregar um campo/tela, percorra o caminho do usuário até ele (qual botão? em que estado do app fica visível?) — testes de ViewModel passam mesmo quando a tela é inacessível.

Decisões do usuário (2026-09-20): botão **⚙ Configurações** na barra, em qualquer tela logada (sem exigir caixa aberto: o código do PDV precisa ser definido *antes* de vender; só bloqueia com venda em andamento) e **só supervisor**, digitando a chave. Como foi feito:
- **A chave é pedida na própria tela**, que abre com o formulário escondido (`Bloqueada`); na 1ª vinculação não pede nada (ainda não há login nem supervisor local). Chave errada, de operador comum ou de supervisor desativado dão **a mesma mensagem**, e o campo é limpo depois de toda tentativa.
- **A regra vive no ViewModel, não só no botão escondido**: `SalvarAsync` e `VincularAsync` recusam com a tela bloqueada (`Execute()` ignora `CanExecute` — mesma lição do fechamento de caixa, #62). Teste: salvar bloqueado não grava nada e vincular bloqueado não chama a rede.
- **`SupervisorAutenticador`** (novo, estático): a conferência da chave estava privada em `VendaLocalService` (descarte); com o 2º uso virou um lugar só, para a regra "supervisor ativo + bcrypt" não divergir.
- **Re-vincular a partir do menu derruba o login e o caixa** (`OperadorLogado`/`CaixaAberto` = null, volta ao Login): um novo vínculo pode ser de **outra empresa**. (A revisão de código mostrou que isso não basta — ver o fim desta entrada.)
- **`IrParaTelaInicialAsync`**: a decisão "Dashboard ou Abrir caixa" estava duplicada no login e no fechamento; agora é um método usado também pelo "Voltar" das Configurações.
- Tudo auditado no log: quem liberou (supervisor) e as tentativas recusadas.

**Revisão de código do acesso às Configurações (achados corrigidos):** (a) **Re-vincular a outra empresa misturava dados.** Ao dar acesso ao formulário de vínculo, ele passou a ser alcançável com o banco em uso: vendas/caixas pendentes da empresa antiga seriam enviadas com as credenciais da nova, e o catálogo novo se sobreporia ao antigo (o CNPJ do link até decide o que a limpeza de empresas locais apaga). Agora `ConfiguracaoService` recusa, **antes de qualquer chamada à API**, vincular a um CNPJ diferente quando já há caixas/vendas locais. Só recusa quando dá para *provar* a diferença: mesma empresa (ex: novo segredo), link sem CNPJ (vínculo antigo) e 1ª vinculação seguem livres. Derrubar o login (feito na 1ª versão) resolve a sessão, não os dados. (b) **Carregar antes de mostrar (#61) também vale aqui:** a tela abria com o formulário vazio e carregava em segundo plano — um "Salvar" precoce gravaria os valores padrão por cima da configuração. Agora `IrParaConfiguracoesAsync` aguarda o carregamento antes de exibir. (c) FYI: se um dispositivo vinculado não tiver nenhum supervisor com chave sincronizado, as Configurações ficam inacessíveis pelo menu (no banco real há 5 supervisores com chave, então não é o caso hoje); a chave também não tem limite de tentativas, apenas o custo do bcrypt — o item "atraso progressivo após erros de login" do backlog cobre os dois fluxos.


## 69. Desligar uma regra sem apagá-la: um interruptor central, injetado, com os dois modos testados

**Onde:** `PoliticaSupervisor`, `VendaLocalService`, `ConfiguracaoService`, `AppServices`

Decisão do usuário (2026-09-20): os dois pontos que pedem a chave de um supervisor — **descartar venda** (#63) e **abrir as Configurações** (#68) — ficam **abertos por enquanto**, porque ainda não se sabe como obter a chave de supervisor no SoftcomShop; quando souber, pede para religar. Apagar o código seria o caminho mais curto e o pior: a lógica (e os testes) de #63/#68 é exatamente o que se quer de volta depois.

Como foi feito: **um único `const bool PoliticaSupervisor.ExigirChave = false`**, repassado *por construtor* pela composição do app (`AppServices`) aos dois serviços. Os serviços têm **padrão `true`** (a regra completa) — desligar é decisão da composição, não deles — então toda a suíte anterior continua exigindo a chave sem mudar uma linha, e o modo aberto ganhou testes próprios. Três cuidados:
- **Sem inventar autoria:** com a chave desligada, `DescartadaPorId` fica vazio (ninguém autorizou) e a lista mostra "Descartada: motivo", não um nome; `SolicitadaPorId` (quem pediu) continua gravado e o log diz "sem chave de supervisor (exigência desligada)".
- **A tela acompanha:** o campo de chave some e o texto de ajuda não fala em supervisor; o botão Descartar só exige o motivo. **Enter no campo de motivo não descarta** (só o botão): descartar é irreversível e um Enter acidental não pode disparar.
- **Um teste garante a fiação:** sem ele, esquecer de repassar o interruptor a um serviço faria religar virar "trocar a constante e torcer". `ComposicaoDoAppUsaOInterruptorUnico` constrói o `AppServices` de verdade e confere os dois serviços.

**Para religar:** trocar `ExigirChave` para `true` em `Services/PoliticaSupervisor.cs` — e, se quiser o aviso no botão da barra, voltar o `ToolTip.Tip="Exige a chave de um supervisor"` do "⚙ Configurações" em `ShellView.axaml` (retirado porque ficaria falso). O que ainda falta é a fonte da chave: hoje ela vem do `pdv_key` (hash bcrypt) que a API manda por funcionário com `supervisor = true`; se o SoftcomShop tiver outro mecanismo de autorização de supervisor, é isso que precisa ser descoberto.


## 70. Os dois últimos itens do backlog de robustez: atraso progressivo no login e retry do número do pedido

**Onde:** `LimitadorDeTentativas`, `LoginOperadorService`, `LoginViewModel`; `VendaService.RegistrarVendaLocalAsync`

**Atraso progressivo no login.** O bcrypt torna cada tentativa lenta, mas nada impedia alguém de ficar testando chaves sem parar num PDV exposto. Regra: as **3 primeiras falhas seguidas são livres** (erro de digitação é normal) e a partir da 4ª o app exige uma espera que dobra — 5 s, 10, 20, 40… até o **teto de 5 min**. Decisões e porquês:
- **Durante a espera nem a chave certa é conferida.** Não gasta bcrypt e não dá pista de acerto a quem tenta adivinhar (se a certa "passasse" durante a espera, o atacante saberia que acertou). O operador legítimo só espera.
- **Tentativa feita na espera não conta nem estende a espera.** Senão um operador que aperta Enter várias vezes se puniria sozinho, e o teto viraria "5 min para sempre".
- **Acertar zera tudo**, inclusive as 3 livres.
- **A mensagem é honesta sem ser pista:** "Chave inválida" continua genérica; a partir da 4ª acrescenta "aguarde N s". Arredonda **para cima** (dizer "0 s" com a espera valendo confunde) e 60 s vira "1 min".
- **Estado em memória:** fechar e reabrir o app zera o contador. Limite assumido: é proteção contra tentativa na frente do balcão, não contra quem já tem acesso ao computador (esse abre o banco direto). O `LimitadorDeTentativas` é genérico e testado isolado — quando a chave de supervisor for religada (#69), o mesmo limitador se aplica a ela.
- Testado com relógio falso (`RelogioFalso`), sem `Thread.Sleep`.

**Retry do número do pedido.** `NumeroPedido = MAX + 1` só colide com **dois processos no mesmo banco**; o índice único recusa o segundo e, antes, isso derrubava o registro da venda que o operador **acabou de finalizar**. Agora tenta o próximo número (até 5 vezes). Cuidados: (1) **só repete se foi mesmo colisão** — depois da falha confere num contexto novo se o número já existe; qualquer outra falha (chave estrangeira, disco, banco travado) sobe na hora, porque repetir não a resolveria (teste: violação de FK = 1 tentativa só); (2) **uma venda nova por tentativa**, porque depois de um `SaveChanges` que falha o contexto e o que ele rastreava não servem mais; (3) **teto de 5**: o 5º erro seguido já não é azar de corrida, sobe para o log/operador em vez de insistir para sempre. A corrida é simulada gravando a venda "do outro processo" dentro do evento `SavingChanges` do primeiro contexto — e **conferi que o teste prova o retry**: com o teto trocado para 1, dois testes falham.


## 71. Cartões e bandeiras: quando o Swagger erra o caminho, olhe onde a API JÁ funciona

**Onde:** `SoftcomRotas.Cartoes`, `SoftcomApiClient.BuscarPaginasAsync`, `CatalogSyncService.Cartoes`, `Cartao`, `PagamentoVenda.Bandeira`, `PdvViewModel`, `FecharCaixaViewModel`

**Achar a rota.** O Swagger dá `GET /api/financeiros/cartoes/page/{page}`, mas sob `softauth/api/v2/…` (onde todas as outras rotas vivem) 14 variações de nome deram `500 "Resource not found."`. A dica veio do usuário: *"usa um caminho parecido com o que pega o token"* — o token é `softauth/authentication/token`, **sem `api/v2`**. Com `softauth/api/financeiros/cartoes/page/1` (sem o `v2`) veio o 200. Lição: quando a documentação e a API real divergem, compare com o caminho que **já funciona** (aqui o do token) antes de sair chutando nomes. Um controle (a mesma sonda numa rota conhecida, `forma-pagamento`) descartou problema de token nas tentativas que falharam.

**O formato é OUTRO** (mais uma vez o Swagger mentiu): envelope `{code, message, human, data[], meta:{page:{current,prev,next,count}}, date_sync}` — sem `current_page`/`next_page_url` —, **tudo como texto** (`bandeira_id` = `"02"`, com zero à esquerda; `taxa_administrativa` = `"0.01"`) e página fora do intervalo devolve **HTTP 500** `"Invalid pagination interval."` em vez de lista vazia. Consequências no código: (1) um leitor próprio (`BuscarPaginasAsync`) que pagina **por número** e monta cada URL sozinho — nunca segue uma URL devolvida pelo servidor, então não há como ser levado a outro domínio com o token; (2) `bandeira_id` fica **texto** (`TextoFlexivelConverter`: aceita texto ou número, preserva o "02"); (3) o 500 de intervalo na **1ª página** é "empresa sem cartões", não falha; (4) proteção de laço (`next` que não avança) como na paginação das outras rotas.

**Sincronizar por substituição, com cuidado.** A API não avisa de cartão removido, e a lista é pequena: cada ciclo baixa tudo e o conjunto local vira exatamente o da API (cria, atualiza e apaga o que sumiu) — mas **só depois de a busca inteira dar certo** (teste: falha na 2ª página não apaga nem a metade que chegou). Apagar é seguro porque o pagamento guarda só o **nome** da bandeira. E `Quantidade` = registros que **mudaram**, não os que vieram: senão o ciclo de 5 min contaria a lista toda e recarregaria as telas à toa.

**A bandeira é atributo do cartão, não um catálogo à parte** (`REDE / CREDITO / MASTERCARD`): a lista do app é o conjunto dos nomes distintos. Decisões do fluxo (o usuário disse "você que decide"): na venda, a bandeira é **obrigatória só para forma de cartão e só se há bandeiras sincronizadas** — sem cartões não há o que escolher e travar a venda por causa de um cadastro que talvez nem exista seria pior que o problema; a escolha vale para **um** pagamento (volta a nula), porque o próximo cartão pode ser de outra bandeira; no fechamento, uma linha por bandeira **que aparece nas vendas do caixa**, pré-preenchida com o esperado (descartadas e pagamentos sem bandeira ficam fora). Não guardei o CNPJ da credenciadora: o app não usa, é um dado a menos para vazar.

**Compatibilidade sem mexer em 100 testes:** `RegistrarVendaLocalAsync` ganhou um overload com a bandeira e a assinatura antiga delega a ele; os testes que contavam "5 GETs por rodada do catálogo" passaram a usar uma constante (`RecursosDoCatalogo`), assim o próximo recurso muda **um** número. `CatalogSyncService` virou `partial` com o recurso em arquivo próprio: já era o maior arquivo do projeto.

**O que NÃO foi provado contra a API real:** o fechamento com `digitacao_bandeiras: [{bandeira:"MASTERCARD", valor:…}]` — o formato (nome, não id) veio do Swagger. Se a API recusar, o fechamento aparece como falha com a mensagem dela (política de retentativa) e dá para ajustar o campo. A venda **não** manda a bandeira à API (contrato do POST de venda para isso é desconhecido). Confirmar com um fechamento real é o próximo passo, com autorização do usuário.

**Revisão de código da Fase 6 (achados):** (a) **Verificado de ponta a ponta contra a API real** (só GET, numa cópia do banco): o `SincronizarCartoesAsync` de verdade trouxe o cartão real (MASTERCARD, código `"02"` preservado) e o 2º ciclo contou 0 mudanças — os testes sozinhos só provam o formato que EU simulei. (b) **`EhCartao` usava igualdade com `"CARTAO"`**: uma empresa com outra grafia (`CARTAO_CREDITO`) ficaria sem escolha de bandeira em silêncio; passou a `StartsWith("CARTAO")` — e **não** `Contains("CART")`, que pegaria `CARTEIRA_DIGITAL`. (c) **A tela de venda não via cartões que chegavam depois de aberta** (1º uso, ou cartão novo no SoftcomShop): um pagamento em cartão sairia sem bandeira e ficaria fora da apuração. `PdvViewModel` agora recarrega **só as bandeiras** quando a sincronização mexe no banco (não o catálogo nem o carrinho — a razão original de a tela de venda não ser recarregável). (d) **Risco que fica, sem correção de código:** o formato de `digitacao_bandeiras` nunca foi aceito pela API real e um fechamento que a API recusar **não pode ser corrigido pelo operador** (o caixa já está fechado localmente, a apuração fica gravada). Por isso o 1º fechamento real com bandeira deveria ser feito de propósito, num caixa de teste. **RESOLVIDO (usuário, 2026-09-20; conferido no banco): o fechamento com `digitacao_bandeiras: [{bandeira:"MASTERCARD", valor:40}]` foi aceito** (caixa 2 = id 30 na API, `Sincronizado`, sem erro).


## 72. Redesenhar uma tela por um print: renderize de verdade, e conheça as pegadinhas do Avalonia

**Onde:** `Themes/PdvStyles.axaml`, `Views/PdvView.axaml`, `Views/Controls/PagamentoPainel`, `Views/ShellView.axaml`, `PdvViewModel` (Fase 7)

**Como conferir o visual sem abrir o app real.** O app de verdade sincroniza com a API ao abrir (e escreve no banco do usuário), então não serve para "olhar a tela". Montei um renderizador **headless** (Avalonia.Headless + Skia, fora do repositório, numa pasta de trabalho): sobe o `ShellViewModel` com um banco em memória e dados de exemplo, navega até a tela, renderiza um PNG e eu o abro para comparar com o print do protótipo. Trocou "acho que ficou parecido" por comparação real — e achou coisas que só o resultado mostra: 3 colunas de cards em vez de 4 (faltavam 4 px por card), valor do desconto desalinhado, aba desabilitada com fundo cinza do tema, barra do topo cortando "Configurações". Também permite testar larguras (1860/1366/1024) e estados (cupom vazio, busca sem resultado, painel de pagamento aberto) — o que a skill de UI pede e que teste de ViewModel nunca cobre. Armadilha do harness: **esperar tarefa async bloqueando a thread da interface trava** (deadlock); é preciso "bombear" a fila (`RunJobs`) enquanto espera.

**Pegadinhas do Avalonia que custaram uma volta cada:**
- **`StaticResource` em estilos compartilhados** falha ("resource not found") se o estilo é resolvido antes do dicionário do tema; **`DynamicResource`** resolve tarde e é o certo para um arquivo de estilos.
- **Estilo declarado em `UserControl.Styles` só alcança os DESCENDENTES** do controle. Um seletor `UserControl.compacto …` nunca casa (o próprio controle não é descendente dele): a classe tem de ficar num filho (o `DockPanel` raiz). Não há media query em XAML, então a largura da janela vira uma classe via code-behind (só apresentação).
- **Seletor de estilo precisa de tipo** quando há `Setter` (`Border.somenteAmplo`, não `.somenteAmplo`) — senão o compilador de bindings reclama de "parent Style Selector".
- **Tema Fluent pinta o estado desabilitado do botão** (fundo cinza): para uma aba de navegação esmaecida é preciso sobrescrever `:disabled` — e manter a aba da tela ATUAL bem visível mesmo desabilitada (o operador precisa saber onde está).

**Decisões de produto/UX registradas:** (1) o **pagamento virou um painel modal** no Finalizar/F10, como no protótipo (que não mostra pagamento na tela): o cupom fica limpo, o valor a lançar já vem preenchido com o que falta (pagamento misto), "Falta pagar" e "Troco" aparecem como **texto** (não só cor), o foco vai para o campo de valor e o corpo fica desabilitado atrás do modal (o Tab não escapa). (2) **F10 em duas batidas** (abre; com o valor pago, confirma) e **Esc fecha o painel antes de cancelar a venda** — sem isso um Esc a mais apagaria o cupom. (3) **Enter com código de barras exato adiciona o produto** (o leitor digita e manda Enter); com vários resultados **não adiciona às cegas**, avisa. (4) A quantidade (que aceita fração, provada com a API real) **fica na barra da busca**: o protótipo não a mostra, mas a venda por peso precisa dela — a única diferença de propósito. (5) O protótipo mostra a categoria no card ("• Mercearia"); a API só manda `grupo_id`, então o card mostra a **unidade**. (6) "Sync: N pendentes" conta caixas, vendas e clientes novos sem id — recontada ao entrar, ao finalizar venda e quando a sincronização mexe no banco. (7) Estados vazios em tudo (catálogo sem produtos, busca sem resultado, cupom vazio), como pede a skill; ícones com `AutomationProperties.Name`/dica no modo compacto.

**Painel de pagamento no estilo do protótipo, com pagamento misto e troco (Fase 7b).** Regras que valem lembrar:
- **Só o dinheiro passa do que falta.** O cliente entrega R$ 50 numa compra que ainda deve R$ 14,49: lança-se 14,49 e a diferença (35,51) é troco, mostrada em tempo real enquanto se digita. PIX e cartão que passam do que falta são **recusados** com mensagem ("Só o dinheiro dá troco") — antes qualquer forma aceitava pagar a mais. Um teste antigo ("pagar a mais habilita finalizar") usava PIX; o que ele queria provar era "dinheiro com troco", então passou a usar dinheiro, e ganhou o par que prova a recusa nas outras formas.
- **"Dinheiro" se reconhece pelo código da NFC-e `01`, não pelo `Tipo`.** Na API real o "PIX OFF" (código 20) também vem com `Tipo = ESPECIE`; olhar o `Tipo` daria troco em PIX. Sem código, cai no `Tipo` (formas cadastradas sem código).
- **A venda grava o que foi APLICADO, não o que foi entregue** (`PagamentoAlocado.Valor` × `ValorRecebido`): o total pago bate com o total da venda. O troco vive só na tela por enquanto — enviar `valor_recebido` real à API e persistir isso fica pendente (precisa migration e confirmar com a API real que aceita `valor_recebido` ≠ `valor_pagamento`).
- **Uma forma só = um clique.** Escolher a forma já habilita o Confirmar, que **lança e conclui** (validando valor/bandeira como qualquer lançamento); o "＋ Adicionar" só é necessário para dividir em mais de uma forma. Se o lançamento é recusado (ex: cartão sem bandeira), o painel fica aberto com o motivo — não fecha nem grava meia venda.
- **Guarda contra pagamento de R$ 0,00:** com a venda já toda paga, outro lançamento em dinheiro criaria um pagamento zerado (`min(valor, 0)`); passou a ser recusado ("A venda já está totalmente paga").
- **Renderizar de novo mostrou o resto:** a tela ganhou rolagem no corpo (o rodapé Voltar/Confirmar fica fixo) porque em 884 px de altura o conteúdo com a área de lançamento não cabe.

**Revisão de código das Fases 7/7b (achados corrigidos):**
1. **Pagamento acima do total depois de tirar um item.** A regra "só o dinheiro passa do que falta" só valia na hora de LANÇAR; o total pode cair depois (paga o PIX de R$ 48,98, volta ao cupom, tira um item → total R$ 24,49, PIX ainda R$ 48,98) e a venda saía gravada com um PIX maior que o total. `PodeFinalizarVenda` agora também exige que os pagamentos **não passem do total** (`PagamentosPassamDoTotal`, tolerância de 1 centavo), e o painel mostra um aviso vermelho dizendo o que fazer ("remova ou ajuste um pagamento") em vez de só desabilitar o Confirmar em silêncio. Lição: uma regra de negócio que depende do total tem de ser conferida onde o total pode mudar, não só onde o valor é digitado.
2. **Total fracionado (peso × preço).** 0,333 kg × R$ 9,99 = 3,32667 — a tela mostra R$ 3,33, mas o app comparava o pagamento com 3,32667: um PIX de "3,33" seria recusado ("passa do que falta") e um dinheiro gravaria 5 casas decimais. O que falta agora é medido **em centavos** (`FaltaEmCentavos`, mesmo valor que a tela mostra e que o operador digita). O `Total` do cupom em si continua sem arredondar (limitação anterior, anotada).
3. **Fechar o painel deixava a forma escolhida (e a mensagem de erro).** Reabrir mostrava a forma antiga marcada. Fechar agora limpa forma, bandeira e mensagem — e o teste que dizia isso na frase do nome mas não verificava foi corrigido para verificar.
4. **Foco de teclado perdido ao abrir o painel.** O corpo da venda fica desabilitado atrás do modal; se o foco estava nele, sumia, e F10/Esc (que vivem no `PdvView`) deixavam de chegar — só o mouse resolvia. Agora o foco vai para a 1ª forma ao abrir e para o campo de valor ao escolher uma. **Só apareceu ao verificar o foco no renderizador** (nenhum teste de ViewModel enxerga isso), e a 1ª tentativa falhou de um jeito instrutivo: os cartões só existem depois do 1º layout, então a ação de foco precisa **tentar de novo** até eles existirem; e um painel que já nasce visível não dispara "ficou visível", por isso o teste do renderizador abre o painel **depois** de mostrar a janela, como no app.
5. **Código morto:** `AdicionarPagamentoCommand` (o clique direto na forma) não é mais usado desde o painel novo — removido. Comentário desatualizado ("troco não é modelado") corrigido.
**FYI (sem mudança):** (a) `ReactiveCommand.Execute()` ignora o `CanExecute`, então `ConfirmarVenda`/`F10` chamam `FinalizarVenda.Execute()` sem proteção contra reentrada; hoje é seguro porque o acesso ao SQLite completa de forma síncrona na thread da interface (não há ponto de interrupção entre ler o cupom e limpá-lo), mas um `await` real ali abriria a porta para venda duplicada. (b) A grade de produtos não é virtualizada (`WrapPanel`): confortável com centenas de produtos, pesada com milhares. (c) `PdvViewModel` passou de ~550 linhas: o painel de pagamento é o candidato natural a virar um ViewModel próprio se crescer mais.

## 73. Atalhos de teclado que só funcionam "depois de clicar num item": KeyBinding depende de foco

**Onde:** `Views/PdvView.axaml.cs`, `Views/PdvView.axaml` (Fase 7c)

**O relato:** lançar um item e apertar F2 (Nova venda) não fazia nada; clicando num item do cupom, F2 funcionava. Idem F10 e F4. Num PDV isso é grave: o operador não pode ter de "acordar" a tela com o mouse.

**Causa.** Os `KeyBindings` de um `UserControl` só disparam quando o evento de tecla **sobe (bubble) de um controle focado dentro dele**. Sem nenhum controle da tela com o foco — logo ao navegar para a tela, ao clicar numa área vazia, ou quando o botão que tinha o foco some (ex: painel de pagamento fecha) — a tecla nasce na janela e nunca passa pelo `PdvView`. Reproduzi com o renderizador headless simulando teclas: foco em lugar nenhum → F2/F10 mortos; foco na busca ou num card → funcionam. O F4 era pior: tratado no code-behind com o mesmo `AddHandler` no `PdvView`, mesma dependência.

**Correção (em duas camadas).**
1. **Foco inicial na busca** (e volta para ela quando o painel de pagamento fecha): o cursor já está pronto, como no protótipo, e o leitor de código de barras digita direto.
2. **Rede de segurança na janela:** enquanto o `PdvView` está na árvore visual, um handler de `KeyDown` no `TopLevel` (em bubble, só age se o evento ainda não foi tratado) **reaproveita a lista de `KeyBindings` do próprio XAML** (F2/F10/Esc) — sem duplicar as teclas — e trata o F4 (foco de UI puro, sem Command). É removido ao sair da tela, então F2 não vale nas outras telas. Bubble e não Tunnel de propósito: quem tem o foco e já tratou a tecla (Esc fechando a lista de um ComboBox) continua mandando, e o Esc não cancela a venda por baixo.

**Verificação:** renderizador headless, tirando o foco de verdade (foco na própria janela) e apertando F10/F2, F4 com foco num card, F10 com foco na busca — todos respondem. Teste de ViewModel não enxerga isso (o comando funciona; o que falhava era a tecla chegar nele).

**Lição:** atalho global de tela não pode depender de onde está o foco; declare no XAML o que é comando, mas garanta que a tecla chega ao controle mesmo com o foco fora dele.

## 74. Avisos temporários (toasts): quem avisa, quando avisa e o que NÃO pode fazer

**Onde:** `ViewModels/Toast.cs`, `ToastCentral.cs`, `ShellViewModel`, `PdvViewModel.ItemLancado`, `Views/ShellView.axaml` (Fase 8, Task 68)

- **Um dono só.** O Shell tem a `ToastCentral` e a tela só a exibe (um `ItemsControl` no canto, sobre tudo). Quem tem algo a avisar NÃO conhece o canto da tela: o `PdvViewModel` só expõe `ItemLancado` (um observable com o nome do produto) e o Shell traduz isso em aviso. Assim o ViewModel do PDV continua testável sem a central.
- **Só a MUDANÇA avisa.** O ciclo de 30 s republica "offline" a cada volta; o aviso só sai na troca de estado. Abrir o app já offline avisa; abrir online não ("restaurada" seria mentira). "Online com falhas" conta como conectado (a API respondeu).
- **"Fila vazia" precisa de recontagem própria.** Depois de reconectar, o aviso "nenhuma pendência" só é verdadeiro se a fila realmente está vazia. Esperar o próximo ciclo mexer no banco não serve: se não havia nada a enviar, nenhum ciclo mexe e o aviso nunca vinha. Por isso reconectar dispara uma recontagem imediata e um sinalizador (`avisarFilaVazia`) mantém o aviso pendente até a contagem chegar a zero; com pendências, o segundo aviso NÃO aparece (teste dedicado).
- **Mesma família substitui, não empilha.** Quem bipa 10 produtos seguidos veria 10 avisos; o aviso do item tem `Chave = "item"` e cada novo troca o anterior (e o prazo do novo recomeça — o timer do antigo vira no-op porque ele já saiu da lista). Máximo de 4 visíveis.
- **Não pode tampar nem roubar o mouse/foco:** `IsHitTestVisible="False"` e `Focusable="False"` — um aviso caindo em cima do botão Finalizar não pode impedir o clique.
- **Tempo testável.** A fila aceita um `IScheduler` (nos testes um `HistoricalScheduler`, que "avança o relógio" sem esperar). Em produção os prazos correm num relógio de fundo e a retirada da lista volta à thread da interface (`RxSchedulers.MainThreadScheduler` — no ReactiveUI 23 o `RxApp` antigo não existe mais). Em testes que trocam de estado de conexão, a recontagem em segundo plano é exposta (`RecontagemAposReconexao`) para o teste aguardá-la antes de soltar o banco em memória.
- **Visual:** a faixa colorida lateral (verde/vermelha) é um `Border` próprio dentro do card, com o card recortando os cantos (`ClipToBounds`) — uma `BorderBrush` colorida na borda toda pintava o aviso inteiro de vermelho, diferente do protótipo. O texto ficou num `Grid` com coluna `*` (num `StackPanel` horizontal o texto não quebrava e era cortado). Cor nunca é a única pista: todo aviso tem ícone e texto.

## 75. "Estou online?" não se descobre esperando ter o que enviar: detecção de queda em ~15 s

**Onde:** `Services/Sync/VerificadorDeConexao.cs`, `SincronizacaoBackgroundService` (Fase 8, depois dos toasts)

**A lacuna (achada por uma pergunta do usuário):** o estado de conexão só mudava quando um ciclo de sincronização falava com a API — o de 30 s só se há algo pendente, o de 5 min sempre. Com a fila vazia, desligar a internet levava **até 5 minutos** para virar "Offline" (e o aviso "Internet desconectada" chegava igualmente atrasado). E o `HttpClient` sem prazo espera 100 s numa rede pendurada, e como os ciclos não se sobrepõem, um envio pendurado atrasava também o aviso.

**Correção — duas fontes de sinal, independentes dos ciclos:**
1. **`NetworkChange.NetworkAvailabilityChanged`** (o Windows avisa na hora quando a placa perde/ganha rede): sem placa ativa = Offline imediato. É de graça, mas só diz "há uma interface ligada" — não "há internet".
2. **Verificação leve a cada 15 s** (`VerificadorDeConexao`): um `HEAD` na raiz do servidor da API, com prazo de 5 s. **Qualquer resposta HTTP (até 404/500) prova que o servidor está lá**; só a falta de resposta (DNS, recusa, prazo estourado) é "inalcançável". Cobre o "rede ligada, sem internet" que o evento não vê. Não usa o semáforo dos ciclos, por isso um envio pendurado não a atrasa.

**Regras que evitam efeitos colaterais:**
- **Só a MUDANÇA age.** Caiu → publica Offline (uma vez). Voltou (depois de ter ficado inalcançável) → roda o **catálogo** (que autentica e publica Online — o ciclo de envio sozinho não publicaria Online se não houvesse pendência) e em seguida o envio. Repetir "alcançável" não faz nada: um Offline por **credencial errada** com o servidor no ar não pode virar uma autenticação a cada 15 s (teste dedicado: 3 verificações, 0 pedidos de token).
- **Não mascara problema de configuração:** URL inválida ou sem HTTPS devolve "alcançável" — quem diz "a URL da API não usa HTTPS" continua sendo o ciclo, e a verificação não o troca por um enganoso "sem internet".
- **Cancelar não é queda:** fechar o app durante a verificação propaga o cancelamento; só o prazo estourado conta como inalcançável (dois `catch` distintos, testados).
- **Publicação com trava:** agora três origens publicam o estado (ciclos, verificação, evento de rede, em threads diferentes) e um `Subject` do Rx não aceita `OnNext` concorrente — a publicação (e o registro no log) passaram a ser serializadas por um `lock`.
- **O verificador é opcional no construtor** (`null` = não verifica): os testes antigos dos ciclos seguem iguais e a verificação é testada à parte, com um "servidor" fake que se liga e desliga (`RedeFake`) e um handler que nunca responde (para o prazo).

**Não testado automaticamente:** o evento `NetworkChange` e os timers exigem o Windows real e um Dispatcher (mesma limitação dos timers dos ciclos). Conferência manual: desligar o Wi-Fi → aviso em segundos (evento); "Wi-Fi ligado sem internet" → aviso em até ~15 s (verificação); religar → "restaurada" e, com a fila vazia, "nenhuma pendência".

**Revisão de código (toasts + detecção de queda) — achado corrigido.** A volta da internet rodava o catálogo UMA vez e marcava "voltou". Se um ciclo que já estava em andamento (ex: um envio que ficou pendurado durante a queda) segurava o semáforo, o catálogo da volta era ignorado em silêncio; e esse ciclo antigo, ao terminar falhando, publicava Offline POR CIMA do Online recém-publicado — o indicador ficava vermelho com a internet de volta até o ciclo de 5 min. Agora, se depois da tentativa o estado continua Offline, a próxima verificação (15 s) tenta de novo, **no máximo 3 vezes** (com credencial recusada e o servidor no ar, sem o limite seria um login a cada 15 s; teste dedicado: 6 verificações, 3 pedidos de token). Conferido no renderizador com o dispatcher real: os avisos somem sozinhos em 6 s sem exceção (a retirada da lista volta à thread da interface). **Sem achados:** segurança (o HEAD vai só ao host já configurado, sem credencial), nomes/estrutura, tamanho dos arquivos.

## 76. Painel Principal no visual do protótipo — peças reutilizáveis e o que o protótipo mostra que o app ainda não tinha

**Onde:** `Views/DashboardView.axaml`, `DashboardViewModel`, `Views/Controls/CartaoIndicador`, `SeloSincronia`, `Views/AtalhosDeTela.cs` (Fase 8, Task 69)

- **Peças criadas já pensando nas próximas telas.** `CartaoIndicador` (faixa colorida + título + valor grande + frase) serve também à Listagem de Pedidos (3 cartões); `SeloSincronia` (pílula "✓ Nuvem SoftcomShop" / "⏳ Pendente de envio" / "✕ Falha no envio") serve à Listagem e ao modal de Detalhes. As cores do selo vêm de classes de estilo ligadas ao `SyncStatus` por `ObjectConverters.Equal` no XAML (sem code-behind); o texto, de um conversor. Sempre ícone + texto, nunca só cor.
- **O que mudou de significado.** O cartão "Estoque local" somava as UNIDADES (945); o protótipo mostra "12 itens cadastrados" (quantidade de produtos). Agora o número grande é a contagem de produtos e a soma das unidades virou a frase de apoio ("945 un em estoque local"). "Vendas emitidas hoje" conta só vendas válidas (a descartada é tratada como cancelada, mas continua na tabela como trilha de auditoria).
- **Hora local, não UTC.** A "última sincronização" era mostrada como estava gravada (UTC); o painel agora converte para a hora local e, se não é de hoje, mostra a data ("dd/MM HH:mm") — uma hora sozinha, de anteontem, enganaria.
- **A conexão vem do Shell.** O ViewModel do painel não conhece o serviço de sincronização; o Shell a repassa (`DefinirConexao`) ao abrir o painel e a cada mudança. Teste garante que o painel já abre certo (Offline) sem esperar a próxima mudança.
- **Botão "Detalhes" da tabela** leva à Listagem de Pedidos com aquela venda já selecionada (é onde o detalhe e o descarte moram hoje); quando o modal de Detalhes (Task 71) existir, é ele que abre. **Adaptado do protótipo de propósito:** o aviso "Arquitetura Offline-First" ficou sem o botão "Testar queda de internet" e o texto não manda clicar num botão "Online/Offline" que não existe (o app detecta a conexão de verdade).
- **O mesmo defeito de atalho existia no Painel** (F2 "Abrir Caixa PDV" só funcionava com algum controle focado — ver #73). Em vez de copiar a solução do PDV, ela virou um comportamento reutilizável: `views:AtalhosDeTela.Ativos="True"` num UserControl liga, enquanto a tela está aberta, o handler na janela que reaproveita os `KeyBindings` do XAML (mais o `ITelaComAtalhosExtras` para o que não é Command, como o F4 do PDV). Verificado no renderizador: sem foco, F2 no Painel abre o PDV, e o handler do Painel é removido ao sair (F2 no PDV continua sendo "Novo"). O `PdvView` perdeu o handler próprio.
- **Armadilha do Avalonia:** um comportamento anexado (`AttachedProperty`) não pode usar uma classe `static` como tipo dono no `RegisterAttached<TOwner,…>` — a classe é `sealed : AvaloniaObject` com construtor privado.
- **Armadilha de ferramenta (achada aqui):** editar arquivos com um script PowerShell 5.1 que contém texto acentuado literal corrompe os acentos (o script é lido em ANSI) — três comentários (e um já commitado, no estilo dos avisos) viraram "sÃ³". Corrigidos e conferidos por busca no repositório inteiro; para editar código com acentos, usar a ferramenta de edição de arquivo, não scripts PowerShell com texto literal.

**Ajustes depois do Painel (pedido do usuário + revisão de código).** (1) O bloco "Arquitetura Offline-First" foi **removido**: era só do protótipo. (2) **Achado da revisão:** o painel agora mostra o selo de sincronia das vendas e a fila outbox, mas só carregava ao abrir — o selo ficava "⏳ Pendente" até o operador sair e voltar, mesmo com a venda já enviada. Passou a implementar `IAtualizavelPorSincronizacao` (o Shell o chama quando um ciclo mexe no banco), como a Listagem de Pedidos; teste dedicado. Lição: quando uma tela passa a exibir um estado que muda sozinho, ela precisa entrar no mecanismo de atualização — não basta o dado estar certo na abertura.

## 77. Listagem de Pedidos no visual do protótipo — filtros que sobrevivem ao recarregamento e uma barra do topo que vazava

**Onde:** `Views/ListaPedidosView.axaml`, `ListaPedidosViewModel`, `VendaLocalService`/`VendaResumo`, `ShellViewModel`, `Views/ShellView.axaml(.cs)` (Fase 8, Task 70)

- **Indicadores × tabela.** Os 4 cartões (total de pedidos, volume faturado, sincronizados, pendentes) contam TODAS as vendas do caixa; a busca e os filtros só mexem na tabela — senão o "total" mudaria ao digitar na busca. A venda descartada continua na tabela (trilha de auditoria), mas não entra em nenhum indicador (é tratada como cancelada, como no painel). "Pendentes" = pendentes + em falha, e o detalhe do cartão diz quantas falharam.
- **Filtro que a atualização automática não pode apagar.** A lista recarrega a cada ciclo de sincronização; se a lista de opções do filtro de forma de pagamento fosse recriada a cada recarga, o ComboBox perderia a escolha do operador (e ainda manda `null` por um instante ao trocar de lista). Duas defesas: a lista de opções só é **trocada quando o conteúdo muda**, e o setter **ignora `null`** (não é uma escolha do operador). Se a forma escolhida desaparece de vez (a única venda dela foi descartada), volta a "Todas Formas" em vez de deixar um filtro fantasma que esconde tudo. Testes cobrem os três casos.
- **Busca:** por número ("1003" ou "#1003") ou por nome do cliente, **sem acento e sem diferença de maiúscula** ("joao" acha "João"); só espaços não filtra. Vazio por falta de pedidos ("Nenhum pedido registrado…") é diferente de vazio por causa dos filtros ("…com esses filtros" + botão Limpar) — a mensagem diz qual dos dois.
- **Resumo dos itens** ("2 itens: Arroz 1kg") vem do serviço (que já resolvia cliente/operador/formas em lote; agora também os nomes dos produtos, num lote só — sem N+1); a dica do mouse mostra a lista inteira ("3 × Arroz 1kg", quantidade fracionada com vírgula).
- **"Sincronizar Fila Outbox"** não conhece o serviço de fundo: o ViewModel só emite, o Shell mostra o aviso e sinaliza (`SincronizacaoSolicitada`), e o App liga isso ao `SolicitarAgora()`. O aviso sai ANTES do sinal (o operador vê a resposta ao clique; e o teste não depende de ordem). O mesmo sinal serve ao painel da Fila Outbox (Task 72).
- **Detalhes** por enquanto só seleciona a linha (é onde mora o descarte de uma venda em falha, que continua igual); o modal com itens e requisição é a Task 71. **Nova Venda (F2)** vale sem foco (`AtalhosDeTela`, como no Painel e no PDV).
- **Tabela e largura:** colunas fixas + cliente flexível, dentro de uma rolagem horizontal (largura mínima) — em janela estreita a tabela rola em vez de espremer. A lista não é virtualizada (a página rola inteira): confortável com centenas de vendas de um caixa; se um dia um caixa tiver milhares, é o ponto a rever.
- **Bug antigo achado ao renderizar em 1500 px:** a barra do topo (Task 67) só entrava no modo compacto abaixo de 1500 px, mas a barra completa precisa de ~1900 — entre os dois o operador e o Configurações ficavam cortados (já dava para ver nos avisos, em 1600 px). Agora são dois degraus: abaixo de 1900 some a pílula do sistema; abaixo de 1650 somem também os rótulos (ficam os ícones). Conferido em 1700 e 1500 px. Lição: verificar a barra do topo em **várias larguras** sempre que uma tela nova é renderizada, não só nas duas extremas.

**Revisão de código da Listagem de Pedidos.** (1) **Achado corrigido:** o botão "Sincronizar Fila Outbox" sempre avisava "Sincronizando…", mesmo com a internet fora ou a fila vazia — o operador ficaria esperando algo que não vai acontecer (e uma venda em espera crescente também não sai por esse botão; para ela existe "Reenviar falhas"). Agora o aviso diz a verdade: "Sem conexão: o que está na fila sai assim que a internet voltar." / "Nenhuma pendência na fila local." / "Sincronizando a fila outbox…"; o pedido ao serviço continua sendo feito nos três casos (ele confere rede e fila por conta própria). (2) Tentei definir as colunas da tabela uma vez só, num recurso, para o cabeçalho e as linhas nunca saírem de alinhamento — **não dá**: um recurso `string` não converte para `ColumnDefinitions`, e um `ColumnDefinitions` não pode ser compartilhado entre grades. Ficou duplicado, com um comentário nos dois lugares dizendo que precisam ser iguais. (3) **FYI (sem mudança):** `VendaResumo` guarda as formas de pagamento numa frase ("Dinheiro, Pix") e o filtro a separa por ", " — um nome de forma com vírgula quebraria o filtro; não vira lista de propósito, porque a igualdade por valor do `record` é o que mantém a linha selecionada depois de cada recarga automática (verificado no renderizador: a venda em falha continua selecionada após a atualização). **Sem achados** em segurança nem em desempenho (nomes dos produtos resolvidos num lote só).

## 78. Modal "Detalhes do pedido" — mostrar a requisição sem poder mentir sobre ela

**Onde:** `Services/Sales/MontadorDeRequisicaoDeVenda.cs`, `VendaLocalService.ObterDetalheAsync`, `Views/Controls/DetalhePedidoPainel`, `ListaPedidosViewModel` (Fase 8, Task 71)

- **O risco de um modal que "monta o seu JSON".** Se a tela montasse a requisição por conta própria, um dia ela mostraria uma coisa e a API receberia outra — e o modal existe justamente para servir de evidência do que foi enviado. Por isso o código que monta a requisição (funcionário, empresa, cliente/Consumidor Final, ids do produto, formas de pagamento, numero_documento…) saiu de dentro do `VendaSyncService` para o `MontadorDeRequisicaoDeVenda`, usado pelo envio E pelo modal. A refatoração foi feita movendo o código sem mudar a lógica (os testes de envio existentes seguiram verdes) e há um teste de contrato: **envia a venda de verdade (servidor de mentira), captura o corpo e compara, campo a campo (`JsonNode.DeepEquals`), com o JSON exibido**.
- **Decisão do usuário:** mostrar o POST REAL de vendas da API, não o `/pedidos/sync` inventado do protótipo. O texto mostra método + URL real, `Api-Version`, `Authorization: Bearer ••••••••` e o corpo. **O token nunca passa pelo modal** (o montador nem o recebe; teste garante que um token de verdade não aparece).
- **Limitação assumida (e a alternativa):** a requisição é **reconstruída** a partir da venda gravada, não é uma cópia do que foi transmitido. Para uma venda antiga, um dado que mudou depois (ex: o preço de compra do produto, que vai no corpo) poderia sair diferente do que a API recebeu na época. A tela diz isso ("Reconstruída a partir da venda gravada"). Se um dia for preciso a cópia exata, a saída é gravar o corpo enviado na própria venda (nova coluna + migration) — ficou como opção, não feita.
- **Quando não dá para montar:** falta sincronizar algo (empresa, produto…) ou a venda foi descartada → o modal mostra o **motivo** no lugar do JSON (em vez de um JSON incompleto que pareceria "o que foi enviado"). A nota "Enviada e aceita / A API recusou / Ainda não enviada" some junto (não há o que comentar).
- **Só para ler:** o JSON exibido usa quebra de linha e acentos como acentos ("ESPÉCIE"); o envio real usa o serializador padrão (que escapa `É` como `É`). O conteúdo é o mesmo — o teste compara o JSON parseado, não o texto.
- **UX:** o modal reaproveita o padrão do painel de pagamento (fundo escurecido, página de trás desabilitada, foco no botão Fechar, Esc fecha — e Esc com o modal fechado não faz nada, porque o comando Fechar só fica disponível com ele aberto). Vindo do "Detalhes" do painel principal a listagem já abre com o modal aberto: o foco também é pedido quando o painel **nasce já visível** (antes só ao "ficar visível" — mesma pegadinha do painel de pagamento, achada de novo no renderizador). Com o modal aberto, um ciclo de sincronização recarrega o detalhe (o selo e a nota acompanham). O texto da requisição é selecionável (`SelectableTextBlock`) para colar num chamado de suporte.

**Teste instável achado ao fechar a Task 71:** `FinalizarUmaVendaSobeAContagemDePendentes` falhou uma vez em três execuções da suíte cheia (passava isolado). Ele lia o "antes" logo depois do login, mas o login recontava a fila em segundo plano: lido cedo demais valia 0 e o resultado dependia de quem chegava primeiro. Agora espera o ponto de partida (o caixa aberto ainda sem subir = 1 pendente) antes de lê-lo; 5 execuções seguidas verdes. Lição: em teste, **nunca ler um valor que uma tarefa de segundo plano vai mudar sem antes esperar essa tarefa** — falhar "de vez em quando" é o sinal.

**Revisão de código do modal de Detalhes.** (1) **Achado corrigido:** com a URL da API vazia ou malformada (dispositivo sem vínculo válido), o montador da requisição lança `UriFormatException` (`new Uri("")`), que o modal não capturava — o "Detalhes" viraria o banner de erro inesperado e nem abriria. Agora o modal abre, mostra os dados da venda e explica no lugar do JSON ("Não foi possível montar a requisição: …"); teste dedicado. Vale só para a exibição: no envio de verdade a mesma exceção já era tratada pelo laço do lote. (2) **FYI, sem mudança:** (a) ao fechar o modal o foco de teclado não volta a nenhum controle (a página estava desabilitada) — F2 e Esc seguem funcionando (`AtalhosDeTela`), só o Tab recomeça do topo; (b) para uma venda pendente por dependência faltando, o motivo aparece duas vezes (em vermelho no cabeçalho e no lugar do JSON) — repetição consciente, cada um responde a uma pergunta ("por que está pendente?" / "por que não há requisição?"); (c) `ListaPedidosViewModel` chegou a ~420 linhas: se ganhar mais responsabilidade, o modal (estado `Detalhe` + textos) é o candidato natural a virar um ViewModel próprio. **Sem achados** em segurança (token mascarado e nunca recebido pelo montador; o corpo não leva CPF/CNPJ do cliente, só ids) nem em desempenho.

## 79. Painel lateral da Fila Outbox — um log escrito para o operador, sem vazar o que é técnico

**Onde:** `Services/Sync/LogDeSincronizacao.cs`, `SincronizacaoBackgroundService`, `VendaSyncService`, `ShellViewModel`, `Views/Controls/PainelFilaOutbox` (Fase 8, Task 72)

- **Dois logs, dois públicos.** O arquivo de log (`Registro`) é técnico (exceção, pilha) e serve a quem dá suporte; o painel é para o operador ("Pedido #1003 sincronizado com sucesso!"). Por isso é um log **separado**, curado, em memória (últimas 100 linhas) — em vez de mostrar na tela o que vai para o arquivo. Regra: **nunca** token, senha nem mensagem de exceção na tela (o motivo de uma recusa da API aparece como a API o devolveu, o mesmo texto que a lista de pedidos já mostra) (uma exceção inesperada vira "erro inesperado (detalhes no log do aplicativo)"; o detalhe vai só para o arquivo). Teste garante que o token não aparece.
- **O que entra:** conexão que caiu ("Conexão perdida. Modo contingência ativado…") e voltou; "Disparando sincronização de N item(ns)…" (só quando há algo a enviar — o ciclo ocioso de 30 s NÃO escreve nada, senão o painel viraria uma parede de ruído); uma linha por pedido enviado ou recusado (com o motivo); abertura/fechamento de caixa e clientes novos; catálogo só quando trouxe algo ou quando uma falha NOVA aparece (a mesma falha repetida a cada 5 min não reaparece). A linha "Sincronização solicitada pelo operador" nasce no Shell.
- **Para ter uma linha por pedido** o lote de vendas passou a devolver `Detalhes` (número do pedido + resultado) no `ResultadoSincronizacaoRecurso` — antes só devolvia a contagem e o motivo das recusas se perdia. As etapas de caixa (que se repetem até esvaziar) agora somam o total e devolvem a primeira falha, em vez de engolir o resultado. A checagem "há pendência?" virou uma **contagem** (o mesmo critério de elegibilidade), que também alimenta o "N item(ns)" da linha.
- **Thread:** o log é escrito de threads de fundo; a lista da tela só pode ser mexida na de interface. O serviço entrega pelo `Novas` (observable, com `OnNext` serializado por trava) e o App usa o mesmo padrão dos outros eventos (`Dispatcher.UIThread.Post`).
- **Conflito de nome achado na compilação:** já existia um `NivelLog` (do log de arquivo); o do painel chama-se `NivelAtividade`.
- **UX do painel lateral:** abre pela pílula "Sync" (que virou botão, com foco visível e dica); o fundo escurecido é um **botão** transparente que fecha (alcançável por teclado e leitor de tela, fora do Tab porque ✕ e Esc já fecham); foco vai para "Disparar Sincronização Agora". **Esc com o painel aberto sobre a tela de venda fecha o painel e NÃO cancela a venda** (verificado no renderizador, inclusive sem foco): o painel tem o próprio atalho na janela e a tela de baixo fica desabilitada — e `AtalhosDeTela` passou a ignorar telas desabilitadas (`IsEffectivelyEnabled`), para nenhum atalho de uma tela coberta disparar por baixo de um modal.

**Revisão de código do painel da Fila Outbox.** (1) **Achado corrigido — texto enganoso:** "Offline" no app também significa credencial recusada ou URL sem HTTPS, e o log dizia "Conexão perdida" em todos os casos (mandaria o operador olhar o cabo em vez do problema). Agora só as perdas de rede de verdade (sem placa ativa / API inalcançável) dizem "Conexão perdida"; os demais dizem "Não foi possível falar com a API (autenticação ou comunicação)", **sem** o corpo bruto da resposta (que ficaria na tela; ele segue na dica da pílula e no arquivo de log). Teste dedicado. O mesmo texto genérico do aviso "Internet desconectada" (Task 68) continua para qualquer Offline — FYI, não mexi. (2) **Achado corrigido — foco perdido (também vale para o modal de Detalhes, anotado antes como FYI):** ao abrir um painel modal a tela de trás é desabilitada e o controle focado perde o foco; ao fechar ninguém o recebia de volta, e num PDV o leitor de código de barras passava a digitar no vazio até clicar na busca. Novo `GuardaDeFoco`: anota o último controle FORA do painel que recebeu o foco (não dá para olhar "na hora de abrir": a mesma variável já desabilitou a tela e o foco já se foi) e o devolve ao fechar. Verificado no renderizador: fecha o painel, a caixa de busca volta a ter o foco, a venda continua intacta. (3) **Corrigido o exagero dos comentários/doc:** o log não é "sem dado pessoal" por garantia — mostra o motivo da API como ela devolve. (4) **FYI, sem mudança:** o log chama `OnNext` dentro da própria trava (mantém a ordem); só o App assina e ele apenas posta na thread de interface, então não há risco de bloqueio — um futuro assinante que bloqueie precisaria sair da trava. **Sem achados** em segurança (token nunca no log; teste) nem em desempenho.

## 80. Cadastros no visual do protótipo — a categoria vinha de uma rota que só se descobre olhando a API

**Onde:** `Models/Grupo`, `CatalogSyncService.Grupos.cs`, `CadastroLocalService`, `CadastrosViewModel`, `Views/CadastrosView.axaml`, migration `AddGrupos` (Fase 8, Task 73)

- **A categoria do produto (decisão do usuário: mostrar o NOME)** exigiu achar de onde vem: o produto só traz `grupo_id`. Investigado na API real **só com leitura (GET), numa cópia do banco, imprimindo apenas contagens e nomes de campos**: `GET softauth/api/v2/produtos/grupos` (com `v2` — ao contrário dos cartões — e **sem** `/page/N`, que dá 500; a rota sem `v2` também responde, mas com o outro envelope). Conferido que **os 390 produtos da 1ª página têm `grupo_id` e todos resolvem** para um dos 50 grupos. Sincronização por substituição, como os cartões (a API não avisa de um grupo removido; falha no meio nunca esvazia a lista local; a quantidade devolvida é o que MUDOU, para os ciclos de 5 min não recarregarem as telas à toa).
- **Mostrar a categoria sem misturar dado de tela na tabela:** `Produto.GrupoNome` é uma propriedade **não mapeada** (ignorada no EF), preenchida na leitura por UMA consulta de grupos (não um join por produto). Grupo que ainda não sincronizou (ou que sumiu) deixa o nome nulo e o card do PDV cai na unidade, como era antes — o app funciona igual com o catálogo de grupos vazio. O card do PDV passou a mostrar "Cód: 789100010 • Mercearia" como no protótipo; a busca de Cadastros também acha pelo nome da categoria.
- **Migration feita numa cópia do projeto** (o `dotnet ef` num diretório de trabalho limpo) e copiada de volta. A primeira geração saiu com `SyncStatus` como número: faltava a configuração da entidade (`GrupoConfiguration`, com o enum como texto como nas demais) — sem ela a coluna divergiria das outras tabelas. Novo teste **compara as migrations com o modelo** (`HasPendingModelChanges` depois de `Migrate()` num banco vazio): se um dia o modelo ganhar uma coluna sem migration, falha nos testes em vez de quebrar o app no computador do operador.
- **Números das abas ("Produtos ( 12 )") são o TOTAL** (contagem própria), não o tamanho da lista filtrada nem o corte de 200 linhas.
- **Operadores (aba nova):** vêm dos funcionários sincronizados. O que o protótipo mostra e o app não tem: "Caixa padrão" virou "Caixa aberto" (o caixa que o operador tem aberto agora neste terminal; fechado não conta); "Perfil" vem da flag de supervisor; "Ativo" ganhou dois irmãos honestos — **Desativado** e **Sem chave do PDV** (existe, mas não consegue entrar, porque o login é pela chave). O CPF do funcionário e a chave ficam FORA da projeção (teste garante que o resumo nem tem esses campos).
- **Bug pego pelo próprio teste da formatação de telefone:** a primeira versão colocava o DDD por cima de um número que já o continha ("(83) (83) 3221-4589"). Regra final: 10–11 dígitos = DDD já incluído; 8–9 = DDD à parte; qualquer outro tamanho aparece como veio (não se inventa máscara).
- **"Novo Produto" fica desabilitado** (decisão do usuário: o catálogo é do SoftcomShop), com a dica dizendo isso. "Novo Cliente" (topo e na aba) abre o formulário, que fica recolhido até pedirem (a tela abre limpa). As mensagens de vazio deixaram de mandar "cadastrar o primeiro" onde não há como (só o cliente se cadastra aqui).
- **FYI, sem mudança:** uma falha ao baixar os grupos entra em "falhas do catálogo" como os cartões (o indicador fica "Online (com falhas)"); se algum dia um cliente da API não tiver essa rota, a pílula ficaria laranja para sempre por algo cosmético — é o ponto a rever nesse caso (ex: tratar Grupos como opcional).

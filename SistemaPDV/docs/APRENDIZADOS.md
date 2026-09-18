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

## 13. Hash vs. criptografia reversível pra dados sensíveis

**Onde:** `specs/SPEC-caixa.md` (`PdvKeyHash`) vs. o `client_secret` protegido por DPAPI no projeto de referência

Duas situações parecidas, tratamentos diferentes, porque o uso é diferente:
- `pdv_key` (login do operador): só precisa ser **comparado** ("o que o operador digitou bate com o que está salvo?"). Hash (SHA-256) resolve — nunca precisa recuperar o valor original, só comparar hash com hash.
- `client_secret` (credencial OAuth): precisa ser **reenviado** pra API a cada renovação de token — o app precisa conseguir recuperar o valor original. Hash não serve aqui (é uma via de mão única); precisa de criptografia reversível (DPAPI), que permite proteger em repouso e ainda assim descriptografar quando necessário.

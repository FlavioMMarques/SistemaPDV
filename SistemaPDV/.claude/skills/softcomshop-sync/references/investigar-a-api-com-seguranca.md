# Investigar a API real sem estragar nada

Quando uma rota, um formato ou um campo obrigatório é desconhecido, **olhe a API de verdade** — mas do jeito seguro. Toda a descoberta deste projeto (rotas sem `softauth/`, formas embrulhadas em array, `pdv_key` bcrypt, rota dos cartões e dos grupos) foi feita assim.

## Regras

1. **Só leitura (GET)** por padrão. POST/PUT/DELETE em produção só com autorização explícita de quem é dono dos dados — e diga o que vai enviar.
2. **Numa CÓPIA do banco.** Copie `pdv.db`, `pdv.db-wal` e `pdv.db-shm` (o modo WAL guarda dados no `-wal`). Nunca aponte o programa de investigação para o banco que o app usa.
3. **Imprima só estrutura:** status HTTP, **nomes** de campos, tipos (`String`, `Number`, `Null`, `Array`) e contagens. **Nunca** valores de dados pessoais (nome, CPF/CNPJ, telefone) nem o token.
4. **Nunca imprima nem cole** token, `client_secret` ou chave do PDV — nem "só para conferir".
5. Poucas requisições e sem laço: é um serviço de produção de outra empresa.

## Receita (programa de console descartável, fora do repositório)

1. Um projeto console que referencia a DLL do app (`dotnet build … -p:OutDir=<pasta>` e `Reference` com `HintPath`).
2. Abra o banco **copiado**, leia a `ConfiguracaoSincronizacao`, obtenha o token com o próprio `SoftcomAuthService.ObterTokenAsync` (assim o segredo protegido pelo Windows é usado sem você tocar nele) e monte `Api-Version: v2` + `Authorization: Bearer`.
3. Faça `GET` nas rotas candidatas e resuma a resposta:

```csharp
static string Forma(JsonElement e, int nivel = 0) => e.ValueKind switch
{
    JsonValueKind.Object => "{" + string.Join(", ", e.EnumerateObject()
        .Select(p => p.Name + ":" + (nivel < 1 ? Forma(p.Value, nivel + 1) : p.Value.ValueKind.ToString()))) + "}",
    JsonValueKind.Array => "[" + (e.GetArrayLength() > 0 ? Forma(e[0], nivel) : "vazio") + "] x" + e.GetArrayLength(),
    _ => e.ValueKind.ToString(),
};
```

4. Para conferir uma **relação** (ex: todo `grupo_id` de produto existe na lista de grupos?), imprima **contagens**: "390 produtos; 390 com grupo_id; 390 resolvem".
5. Para conferir o **serviço real** do app (ex: `SincronizarGruposAsync`) sem escrever na API, rode-o contra a cópia do banco: a API só recebe GET e a escrita vai para o banco copiado.

## Achando uma rota que "não existe"

- Rota errada responde **500 `{"error":""}`**, não 404 — então "500" não prova que a rota não existe.
- Teste as variações: com e sem `softauth/api/v2/`, com e sem `/page/N`, plural/singular. Olhe onde a API **já funciona** (o caminho do token não tem `api/v2`; a rota dos cartões também não).
- O Swagger é auto-gerado e às vezes descreve a **resposta** onde deveria descrever a requisição, ou o caminho errado.
- Compare o envelope da resposta: a mesma rota com e sem `v2` pode devolver formatos diferentes.

## Descobrindo os campos obrigatórios de um POST (com autorização)

1. Envie o mínimo. O `422` lista os campos faltando (`{"errors":{"campo":["mensagem"]}}`) — **preserve o nome do campo** na mensagem (dizer "É obrigatório" quatro vezes não ajuda).
2. Depois do 422 podem vir `500` de **gravação**, um por vez, cada um revelando o próximo campo que o servidor lê (`Column 'x' cannot be null`, `Undefined index: y`). As tentativas falhas não deixaram registro pela metade.
3. Registre cada campo descoberto em `references/api-real.md` e cubra com um teste de contrato.
4. Não repita o envio "só para ver": cada POST que passa cria um registro **de verdade** na empresa do cliente.

## Antes de chamar a API, olhe o que o banco já provou

Dúvida "a paginação passa de 200 produtos?" ⇒ conte os produtos na cópia do banco: se há 201, a segunda página foi seguida. Custo zero e sem tocar em credencial. Dados que o próprio sistema já gravou costumam ser prova mais barata e mais segura que uma nova chamada.

# Spec: caixa

## Objective

Login local do operador no terminal PDV (sem rede, usando o `pdv_key` já sincronizado por `catalog-sync`) e o ciclo de vida do caixa: abrir no início do turno e fechar no final (com a conferência de valores por forma de pagamento). Assim como a venda, abertura e fechamento de caixa são **offline-first** — entram no mesmo padrão de outbox.

Isso é viável porque a venda não depende do `id` numérico que o servidor gera na abertura: o payload de `POST /api/v2/vendas` trata `caixa_funcoes_id` como opcional, e junto com ele manda `caixa_data`+`caixa_turno` — a mesma chave natural (data/operador/turno) que a própria API usa pra detectar caixa duplicado (`409` na abertura). Ou seja: o PDV local sempre sabe `caixa_data`/`caixa_turno`/`funcionario_id` sem precisar de rede, e é isso que vai em toda venda; o `caixa_funcoes_id` é só um extra, preenchido quando (e se) a abertura já sincronizou.

Sucesso = um funcionário sincronizado consegue logar digitando o `pdv_key`; abrir caixa funciona offline (fila local) e sincroniza quando há rede, sem bloquear o operador; vendas do turno referenciam o caixa pela chave natural, não pelo id remoto; fechar caixa registra a conferência e marca o caixa como fechado, também via outbox.

## Tech Stack

- Depende de `data-layer` (entidades `Funcionario`, `Caixa`) e `catalog-sync` (funcionários sincronizados, cliente HTTP autenticado)

## Commands

```
dotnet build
dotnet test --filter Caixa
```

## Project Structure

```
SistemaPDV/
  Services/
    Caixa/
      LoginOperadorService.cs      # valida pdv_key contra Funcionario local
      CaixaService.cs              # abrir/fechar, local + chamada à API
```

## Code Style

```csharp
public async Task<Funcionario?> AutenticarAsync(string pdvKeyDigitado)
{
    await using var context = contextFactory();
    var hash = HashPdvKey(pdvKeyDigitado);
    return await context.Funcionarios
        .FirstOrDefaultAsync(f => f.PdvKeyHash == hash && !f.Desativado);
}
```

`pdv_key` chega em texto puro da API, mas é gravado localmente como hash (SHA-256 é suficiente aqui — não é senha de usuário final, é uma chave curta de operação local; o objetivo é só não deixar a chave em claro se o arquivo SQLite vazar). O login compara hash contra hash, nunca compara texto puro salvo.

## Testing Strategy

- Teste de login: funcionário sincronizado com `pdv_key` conhecida autentica; `pdv_key` errada ou funcionário `desativado` não autentica.
- Teste de abertura de caixa: sucesso salva `IdExterno` local igual ao `id` retornado pela API; resposta 409 (já existe caixa aberto pra data/operador/turno) é tratada sem duplicar localmente.
- Teste de fechamento: sucesso marca `Status = Fechado`; 422 (já fechado, ou campo faltando) devolve a mensagem de erro pro chamador sem quebrar o estado local.

## Boundaries

- **Sempre:** abrir caixa localmente grava `DataCaixa`+`Turno`+`FuncionarioId` imediatamente (é isso que a venda referencia, não o id remoto); sincronizar a abertura/fechamento com a API assim que houver rede, do mesmo jeito que `sales` sincroniza vendas pendentes.
- **Perguntar antes:** permitir dois caixas locais "abertos" ao mesmo tempo pro mesmo operador/turno antes de sincronizar (risco de gerar o mesmo conflito 409 que a API já valida do lado dela) — por ora um único caixa local ativo por vez evita isso.
- **Nunca:** guardar `pdv_key` em texto puro no banco local.

## Success Criteria

- [ ] Login local funciona sem chamada de rede, usando dados já sincronizados
- [ ] Abrir caixa funciona offline: grava local com `SyncStatus.PendenteSync`; venda pode ser criada imediatamente depois, sem esperar sync
- [ ] Quando a abertura sincroniza, o `id` retornado é salvo como `IdExterno` do caixa local (fica disponível pra enriquecer vendas já enviadas ou pendentes, mas não é bloqueante)
- [ ] Fechar caixa registra a digitação por forma de pagamento e por bandeira, também via outbox
- [ ] Erros da API (409/422) na sincronização da abertura/fechamento chegam de forma rastreável (mesmo `SyncStatus.FalhaSync` + mensagem usado em `sales`), sem travar o operador

## Open Questions

```
ASSUMPTIONS I'M MAKING:
1. `operador_id` enviado nos payloads de caixa/venda é o `Funcionario.IdExterno`
   (não o `usuario.id` aninhado) — não confirmado explicitamente, ajusto se a API
   rejeitar.
2. Cada terminal/dispositivo opera um caixa por vez (não há troca de operador no meio
   de um caixa aberto sem fechar antes) — igual ao modelo "Caixa 01 = Carlos Silva"
   do protótipo mockado.
3. Se a abertura sincronizar e vier 409 (já existe caixa aberto pra essa data/operador/
   turno no servidor — ex: foi aberto por outro dispositivo), trato como "já existe,
   adota": tenta buscar o caixa remoto existente e usa o id dele, em vez de marcar como
   falha. Ainda não confirmei se a API tem um GET pra buscar caixa por data/operador/
   turno — se não tiver, esse caso vira falha manual (operador avisado, ajusta na mão).
→ Corrija agora ou sigo com essas assunções.
```

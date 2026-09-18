# Spec: sales

## Objective

Registrar uma venda localmente (sempre funciona, mesmo offline) e enviá-la pra API SoftcomShop sabendo se deu certo ou não — padrão outbox: a venda nunca fica bloqueada esperando rede, mas o PDV sempre sabe (e mostra) se cada venda já foi confirmada pelo servidor.

Sucesso = fechar uma venda offline salva local com status pendente e não trava a UI; ao reconectar, a venda é enviada automaticamente; sucesso ou falha do envio fica visível (mesmo os indicadores 🟢/🟡 do protótipo mockado).

## Tech Stack

- Depende de `data-layer` (`Venda`, `ItemVenda`, `PagamentoVenda`), `catalog-sync` (produtos/clientes/formas de pagamento locais pra montar a venda), `caixa` (caixa aberto no turno atual)

## Commands

```
dotnet build
dotnet test --filter Sales
```

## Project Structure

```
SistemaPDV/
  Services/
    ErroApiExtractor.cs      # extrai mensagem de { "errors": {...} } — já duplicado
                              # em CaixaSyncService; terceiro uso (aqui) é o motivo
                              # de virar compartilhado
    Sales/
      VendaService.cs        # cria venda local (sempre offline-safe)
      VendaSyncService.cs    # outbox: tenta enviar pendentes, atualiza status
      Dtos/
        VendaApiDto.cs       # request (subconjunto essencial) + response
```

`ConfiguracaoSincronizacao` ganha `ClienteConsumidorFinalIdExterno` (int?) — em vez de cravar o id `1` (evidência forte, mas não 100% confirmada — ver Open Questions) como número mágico no código, fica configurável. Se a suposição estiver errada, é um valor pra corrigir, não uma busca pelo código.

## Code Style

```csharp
public async Task<Venda> RegistrarVendaLocalAsync(NovaVenda dados)
{
    var venda = new Venda
    {
        Guid = Guid.NewGuid(),
        DataHora = DateTime.Now,
        CaixaId = dados.CaixaId,
        ClienteId = dados.ClienteId,
        Itens = dados.Itens.Select(MapearItem).ToList(),
        Pagamentos = dados.Pagamentos.Select(MapearPagamento).ToList(),
        SyncStatus = SyncStatus.PendenteSync,
    };

    await using var context = contextFactory();
    context.Vendas.Add(venda);
    await context.SaveChangesAsync();

    _ = vendaSyncService.TentarEnviarAsync(venda.Guid); // dispara em background, não bloqueia o fechamento da venda
    return venda;
}
```

Envio pra API:

```csharp
public async Task<ResultadoEnvio> TentarEnviarAsync(Guid vendaGuid)
{
    var venda = await CarregarComItensEPagamentosAsync(vendaGuid);
    var payload = MontarPayloadApi(venda);

    var resposta = await apiClient.PostAsync("/api/v2/vendas", payload);

    return resposta.StatusCode switch
    {
        HttpStatusCode.OK => MarcarComoEnviada(venda, resposta.Body.Data.Id),
        HttpStatusCode.Conflict => MarcarComoEnviada(venda, idExterno: null), // 409 = já recebida, guid já existe
        _ => MarcarComoFalha(venda, resposta.MensagemErro),                  // 422 e demais: fica pendente, guarda o erro
    };
}

private VendaApiPayload MontarPayloadApi(Venda venda) => new()
{
    Guid = venda.Guid.ToString(),
    DataHora = ((DateTimeOffset)venda.DataHora).ToUnixTimeSeconds(), // inteiro Unix, não string ISO
    EmpresaId = empresaId,
    UsuarioId = usuarioId,
    FuncionarioId = venda.Caixa.FuncionarioIdExterno,
    ClienteId = venda.ClienteId is null ? clienteConsumidorFinalId : venda.Cliente!.IdExterno,
    CaixaData = venda.Caixa.DataCaixa,
    CaixaTurno = venda.Caixa.Turno,
    CaixaFuncoesId = venda.Caixa.IdExterno, // opcional: só preenchido se a abertura já sincronizou
    Produtos = venda.Itens.Select(MapearItemApi).ToList(),
    Pagamentos = venda.Pagamentos.Select(MapearPagamentoApi).ToList(),
};
```

## Testing Strategy

- Teste: registrar venda offline (mock do envio falhando) — venda salva local com `SyncStatus.PendenteSync`, sem exceção propagada pra UI.
- Teste: envio com `200` marca `Sincronizado` e salva o `VendaIdExterno`.
- Teste: envio com `409` marca `Sincronizado` sem duplicar (idempotência via `guid`).
- Teste: envio com `422`/erro de rede marca `FalhaSync`, guarda a mensagem, e permanece elegível pra nova tentativa.
- Teste: sincronizar uma leva com vendas pendentes reenvia só as que ainda não foram confirmadas.

## Boundaries

- **Sempre:** gerar o `Guid` da venda no momento da criação local (nunca depois) — é a chave de idempotência; nunca bloquear o fechamento da venda esperando resposta da API.
- **Perguntar antes:** mudar a política de retry (quantas tentativas, backoff) — por ora é reenvio manual/no próximo sync, sem retry automático agendado.
- **Nunca:** marcar uma venda como `FalhaSync` permanente sem guardar a mensagem de erro pro operador conseguir agir (reenviar, ou levar pro suporte).

## Success Criteria

- [x] Fechar uma venda funciona sem rede e não bloqueia a UI
- [x] Venda pendente é reenviada automaticamente quando a sincronização roda com rede disponível
- [x] Status da venda (`Pendente`/`Sincronizado`/`Falha`) é consultável pela UI (pra alimentar os indicadores 🟢/🟡 do protótipo)
- [x] Reenviar uma venda já aceita (409) não duplica nem gera erro visível pro operador

## Contrato confirmado (schema completo de `POST /api/v2/vendas`)

Campos SEM `nullable: true` no schema (candidatos a obrigatórios): `guid`, `data_hora` (**inteiro Unix, não string/ISO**), `empresa_id`, `usuario_id`, `funcionario_id`, `cliente_id`, `cliente_nome`, `codigo_status`, `cancelada`, `bloqueada`, `percentual_comissao_venda`, `relancar_pagamento`, `xml`, `numero_nfe`, `numero_documento`, além de `pagamentos[]`/`produtos[]`.

**Ressalva importante:** vários desses (`xml`, `numero_nfe`, `numero_documento`, `codigo_status`, `cliente_nome`) parecem campos GERADOS PELO SERVIDOR (XML de NFC-e, numeração fiscal) que aparecem no mesmo schema de "Venda" usado tanto pra request quanto pra response — é comum em documentação Swagger gerada automaticamente de um único modelo Laravel. Não dá pra confiar cegamente que "sem nullable = obrigatório no POST". A validação real só vem testando o endpoint (ou pedindo pra quem mantém a API confirmar quais são de fato obrigatórios na criação).

**Confirmado por serem nullable (opcionais):** `caixa_funcoes_id`, `caixa_data`, `caixa_turno` — o que habilita caixa 100% offline (ver `SPEC-caixa`): venda referencia o caixa por `caixa_data`+`caixa_turno`+`funcionario_id`, não pelo id remoto.

## Open Questions

```
ASSUMPTIONS I'M MAKING:
1. Payload de venda enviado inclui: guid, data_hora (Unix timestamp int), empresa_id,
   usuario_id, funcionario_id, cliente_id, caixa_data, caixa_turno (+ caixa_funcoes_id
   quando já conhecido), produtos[], pagamentos[]. Os campos claramente
   servidor-gerados (xml, numero_nfe, numero_documento, codigo_status) vão omitidos
   ou com valor vazio/default na primeira tentativa; ajusto conforme a resposta 422
   real informar o que falta. CONFIRMADO (2026-09-18 pelo usuário): usuario_id e
   funcionario_id recebem o mesmo valor (Funcionario.IdExterno), por enquanto.
2. cliente_id É obrigatório (não nullable) — "venda avulsa" (Consumidor Final do
   protótipo) usa um id fixo/reservado de cliente genérico, não null.
   CONFIRMADO (2026-09-18 pelo usuário): é `id = 1` — configurável via
   `ConfiguracaoSincronizacao.ClienteConsumidorFinalIdExterno`, não mais uma suposição.
3. "Enviar automaticamente ao reconectar" significa: o app detecta rede voltando (ou
   um sync manual/periódico) e tenta as vendas pendentes — não é um serviço de
   background contínuo rodando fora do processo do PDV.
4. Uma venda com pagamento em mais de uma forma (pagamento misto) é suportada desde
   o início (lista de pagamentos), não uma limitação "por enquanto".
→ Corrija agora ou sigo com essas assunções.
```

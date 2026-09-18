# Spec: catalog-sync

## Objective

Puxar (pull) da API SoftcomShop os dados de referência que o PDV precisa pra operar offline: produtos, clientes, formas de pagamento, empresa (config fiscal/dispositivo) e funcionários (incluindo `pdv_key`, usada pelo módulo `caixa` pro login local). Autenticação via OAuth2 `client_credentials`, sincronização incremental via `ultima_sincronizacao`/`date_sync`.

Sucesso = rodar a sincronização uma vez, do zero, popula as tabelas locais correspondentes; rodar de novo sem mudanças no servidor não duplica nada e é rápido (usa o filtro incremental).

## Tech Stack

- `System.Net.Http` (HttpClient nomeado via `IHttpClientFactory`)
- `System.Text.Json` para (de)serialização, com `JsonNumberHandling.AllowReadingFromString` nos DTOs (a API às vezes devolve número como string)
- Depende de `data-layer`

## Commands

```
dotnet build
dotnet test --filter CatalogSync
```

## Project Structure

```
SistemaPDV/
  Models/
    ConfiguracaoSincronizacao.cs   # link/URL da API, client_id, client_secret protegido,
                                    # nome do dispositivo, última sincronização por recurso
                                    # (extensão pontual do data-layer — não fazia sentido
                                    # existir antes de catalog-sync precisar dela)
  Data/Configurations/
    ConfiguracaoSincronizacaoConfiguration.cs
  Services/
    Sync/
      SegredoProtector.cs          # DPAPI: protege/desprotege client_secret (e, futuramente,
                                    # o certificado da empresa) em repouso
      SoftcomAuthService.cs        # troca client_id/client_secret por access_token
      CatalogSyncService.cs        # orquestra: autentica, busca cada recurso, faz upsert
      SoftcomApiClient.cs          # GET genérico paginado (segue next_page_url)
      Dtos/
        PaginaApiDto.cs            # envelope { current_page, data[], next_page_url, ..., date_sync }
        ProdutoApiDto.cs
        ClienteApiDto.cs
        FormaPagamentoApiDto.cs
        EmpresaApiDto.cs
        FuncionarioApiDto.cs
```

## Code Style

Um cliente genérico de página, reaproveitado pelos quatro recursos paginados (mesmo padrão do projeto de referência `SistemaAvalonia`, `Services/ConfiguracaoSincronizacaoService.BuscarPaginadoAsync<T>`):

```csharp
public async Task<IReadOnlyList<T>> BuscarTudoAsync<T>(string caminho, DateTimeOffset? ultimaSincronizacao, CancellationToken ct)
{
    var itens = new List<T>();
    string? url = MontarUrlInicial(caminho, ultimaSincronizacao);

    while (url is not null)
    {
        var pagina = await GetPaginaAsync<T>(url, ct);
        itens.AddRange(pagina.Data);
        url = pagina.NextPageUrl;
    }

    return itens;
}
```

DTOs mapeiam o **schema completo** que a API retorna (não só os campos que o PDV usa hoje) — evita re-sincronizar tudo de novo quando `sales` ou `pdv-ui` precisarem de um campo que `catalog-sync` já recebia mas descartava.

## Testing Strategy

- Testes de unidade para o parsing dos DTOs (JSON de exemplo → objeto), incluindo o caso de número vindo como string.
- Testes de unidade para a lógica de upsert (casa por `IdExterno`; cria se não existe; atualiza se existe) usando SQLite in-memory (ver `data-layer`).
- `SoftcomApiClient` testado com `HttpMessageHandler` fake (sem chamada de rede real) — inclui um teste que garante que `next_page_url` fora do domínio configurado é rejeitado (mesma proteção que o projeto de referência já tem, pra não vazar o Bearer token pra outro host).

## Boundaries

- **Sempre:** enviar o header `Api-Version` (obrigatório em todo endpoint desta API); validar que `next_page_url` pertence ao mesmo domínio antes de seguir; usar `IdExterno` (nunca o `Id` local) para casar registro local com o remoto.
- **Perguntar antes:** mudar o intervalo/gatilho de sincronização automática (ex: rodar em background a cada N minutos) — por enquanto é só sob demanda (botão), igual ao protótipo mockado.
- **Nunca:** logar o token de acesso, o `client_secret`, ou os campos `empresa_certificado`/`empresa_certificado_senha`.

## Success Criteria

- [ ] `SoftcomAuthService` obtém token válido a partir de `client_id`/`client_secret` salvos
- [ ] Sincronizar produtos, clientes, formas de pagamento, empresa e funcionários popula as tabelas locais correspondentes
- [ ] Rodar duas vezes seguidas não duplica registros (casamento por `IdExterno`)
- [ ] `ultima_sincronizacao`/`date_sync` é persistido e usado na sincronização seguinte

## Open Questions

```
ASSUMPTIONS I'M MAKING:
1. O link de cadastro do dispositivo (client_id + empresa_name + empresa_cnpj na query
   string) é colado uma vez na tela de configuração, igual ao projeto de referência —
   SistemaPDV não gera esse link, só consome.
2. O access_token é obtido a cada sincronização (não fica em cache entre execuções do
   app) — não vi confirmação de tempo de expiração do token, então não vou assumir
   que dá pra reutilizar entre sessões.
3. Funcionários: sincronizar TODOS os retornados pra empresa autenticada (o filtro por
   empresa já vem do lado do servidor, associado ao token/dispositivo) — não filtra
   nada a mais localmente.
4. Empresa: sincroniza um registro (a empresa do dispositivo), não uma lista — mesmo
   o endpoint sendo `/empresas/{page}` paginado, na prática o dispositivo só enxerga
   a própria empresa. Se isso não for verdade, ajusto pra tratar como lista.
→ Corrija agora ou sigo com essas assunções.
```

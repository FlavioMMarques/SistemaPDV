# Arquitetura da sincronização (o que funcionou)

## Modelo de dados

- Toda entidade sincronizável tem `IdExterno` (o id na API, nulo até sincronizar) e `SyncStatus` (`PendenteSync`, `Sincronizado`, `FalhaSync`, e `Descartada` só para venda).
- **Chave gerada no cliente:** a venda nasce com um `Guid` — é a **chave de idempotência** enviada à API. Gerá-la só na hora de enviar reintroduz duplicatas (a resposta pode se perder no caminho).
- **Um campo não guarda dois estados independentes.** O caixa tem duas coisas a confirmar (abertura e fechamento); por isso existe `AberturaSincronizada` separado de `SyncStatus`. Um `SyncStatus` só fazia o fechamento nunca ser detectado como pendente.
- Entidades do outbox guardam `UltimoErroSync`, `TentativasEnvio` e `ProximaTentativaEm`.
- Enums no SQLite ficam como **texto** (configure na entidade, senão a coluna diverge das demais).

## Leitura (catálogo)

- Cada recurso tem seu marcador `UltimaSincronizacao*` (o `date_sync` devolvido) e pede só o que mudou (`ultima_sincronizacao=`).
- **Upsert por `IdExterno`:** uma consulta dos existentes (não uma por item — N+1), e um dicionário dos já processados na mesma leva (a API pode repetir o registro em páginas diferentes; sem isso o índice único quebra o `SaveChanges`).
- **Substituição** (cartões, grupos): lista pequena, sem aviso de remoção ⇒ baixa tudo a cada ciclo e o conjunto local passa a ser o da API. Duas guardas obrigatórias: (1) só aplica depois de a busca **inteira** dar certo; (2) resposta **vazia** de quem já tinha dados é suspeita ⇒ mantém o que existe. A quantidade devolvida é a de registros que **mudaram** (não a de baixados), senão as telas recarregam à toa a cada ciclo.
- Falha de um recurso **não impede** os outros; o resultado agregado diz qual falhou (vira dica no indicador de conexão).

## Escrita (outbox)

**Ordem de envio no ciclo:** clientes novos → abertura de caixa → vendas → fechamento de caixa.
- A venda referencia cliente e caixa; o **fechamento resume o caixa** (chegando antes das vendas a API fecha "caixa vazio").

**Classificação da resposta** (`SoftcomApiClient.EnviarAsync`): sucesso, `409` conflito, `401` token expirado, outras falhas, e `ConexaoInsegura` (URL sem HTTPS — problema de **configuração**: nada saiu daqui, então o item não é marcado como falha nem gasta tentativa).

**`409` não significa sempre "já sincronizado":**
- Venda: sim — o app já sabe o `guid`; marca `Sincronizado`.
- Abertura de caixa: sim (marca `AberturaSincronizada`; só vira `Sincronizado` se o caixa ainda está aberto).
- **Cliente: NÃO.** A resposta não traz o id. Marcar `Sincronizado` deixa `IdExterno` nulo e toda venda para esse cliente fica travada para sempre em silêncio. Vira `FalhaSync` com a mensagem da API.
- Regra geral: antes de reaproveitar um tratamento de erro, confira se a premissa que o justificava ainda vale.

**Espera por dependência ≠ falha.** Funcionário, empresa, cliente, produto ou forma de pagamento sem `IdExterno` ⇒ o item continua `PendenteSync`, **sem** gastar tentativa, e o motivo é gravado em `UltimoErroSync` (a lista mostra). O motivo some sozinho quando o item é enviado. "Pendente sem explicação" é indistinguível de "travado".

**Lote isolado:** uma venda que lança exceção não aborta as seguintes (`try/catch` por item, cancelamento passa direto).

## Retentativa (`PoliticaRetentativa`)

- Falhou ⇒ espera **30 s, 1 min, 2, 4, 8, 10 min** (teto 10 min) e **desiste depois de 8 tentativas** (~35 min de insistência).
- Contar e agendar **no banco**, não na memória (sobrevive a reabrir o app).
- **Desistir precisa de saída:** botão "Reenviar falhas" zera o contador; dar certo também zera.
- O filtro `Elegivel<T>` é uma expressão que o EF traduz para SQL (não uma interface).
- Sem isso, um erro permanente (422) era reenviado a cada 30 s para sempre, com token novo a cada vez.

## Ciclos em segundo plano (`SincronizacaoBackgroundService`)

| Ciclo | Ritmo | Fala com a rede? |
|---|---|---|
| Outbox | 30 s | **Só se houver algo elegível a enviar** (senão nem autentica) |
| Catálogo | 5 min | Sempre (autentica e baixa) |
| Verificação de conexão | 15 s + evento do Windows | HEAD leve na raiz do servidor, prazo 5 s |

- Um **semáforo** garante um ciclo por vez (sem fila): o ciclo novo é ignorado se o anterior ainda roda.
- O timer dispara na thread de interface, mas o **trabalho vai para `Task.Run`** (o provedor SQLite é síncrono por baixo: gravar catálogo na thread de UI congelaria a tela).
- **Testar o ciclo (método público), não o timer.**
- Exceção nunca escapa do ciclo (só cancelamento): vira `Offline` + mensagem.
- Uma tela aberta que lista dados sincronizados **implementa `IAtualizavelPorSincronizacao`**: o Shell a avisa quando um ciclo mexeu no banco (ninguém assina evento; as telas são recriadas a cada navegação).

## "Estou online?"

- Os ciclos só falam com a rede quando têm o que enviar; com a fila vazia, uma queda levava **até 5 min** para aparecer. Por isso existe o `VerificadorDeConexao` (HEAD, qualquer resposta HTTP = alcançável) **e** o `NetworkChange` do Windows.
- Só a **mudança** de estado age: voltou ⇒ roda o catálogo e o envio na hora; repetir "alcançável" não faz nada (senão uma credencial errada viraria um login a cada 15 s). A recuperação tenta no máximo 3 vezes.
- `Offline` **também** é credencial recusada e URL sem HTTPS — o texto para o operador não pode dizer "internet caiu" em todos os casos.
- Publicar estado de várias threads exige **trava** (um `Subject` não aceita `OnNext` concorrente).

## Log para o operador × log técnico

- Arquivo de log (`Registro`): técnico, com exceção. Painel da Fila Outbox (`LogDeSincronizacao`): curado, em memória (100 linhas), em linguagem de operador.
- **Nunca** token, senha ou corpo bruto de resposta de autenticação na tela; exceção inesperada vira "erro inesperado (detalhes no log do aplicativo)".
- O ciclo ocioso **não escreve** nada (senão vira parede de ruído).

## Uma fonte só para o corpo enviado

O corpo de cada requisição é montado **num só lugar** (`MontadorDeRequisicaoDeVenda`), usado pelo envio e pelo modal "Detalhes do pedido". Se cada um montasse o seu, um dia a tela mostraria uma coisa e a API receberia outra. Há um teste de contrato: envia a venda a um servidor de mentira, captura o corpo e compara campo a campo com o exibido. O token nunca passa pelo montador.

## Regras de negócio que a sincronização protege

- Venda **descartada** nunca mais é enviada; a descartada é tratada como cancelada nos totais, mas continua visível como trilha de auditoria (nunca `DELETE`).
- O caixa não fecha com venda ainda não enviada.
- Item de venda com preço ≤ 0 é recusado no serviço (a tela já pede o preço).

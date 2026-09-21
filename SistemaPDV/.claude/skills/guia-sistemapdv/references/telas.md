# As telas que precisam ser feitas

O que cada tela deve mostrar e fazer, de onde vêm os dados e as regras que não podem faltar. Serve de **checklist do que construir** e de "definição de pronto" de cada tela. O visual segue o **protótipo** (os prints são fornecidos por quem coordena o projeto): tema escuro, amarelo `#FED400` como cor da marca, cartões arredondados. As cores e as fontes ficam como **tokens** em `Themes/PdvTheme.axaml`, e os estilos reutilizáveis em `Themes/PdvStyles.axaml`; nunca cores soltas no XAML.

## Regras que valem para TODA tela
- **Nenhuma tela espera a rede.** Gravar local e responder na hora; o envio é da fila.
- **Estado nunca só por cor:** cada sincronização aparece com **ícone + texto** (🟢 Sincronizado, 🟡 Pendente, 🔴 Falha).
- **Sempre os três estados:** vazio (com uma frase que diz o que fazer), carregando e erro. Nunca uma área em branco.
- **Carregue os dados antes de mostrar a tela** (senão ela abre vazia e o operador age em cima de nada).
- **Sem lógica no code-behind**, salvo foco de teclado e apresentação (documente a exceção).
- **Teclado:** todo botão só-ícone tem nome acessível; atalhos funcionam sem depender de foco.
- **Uma regra importante vale nas três camadas** (tela avisa, serviço recusa, envio confere).

## A casca (Shell) — sempre visível
- **Barra do topo:** logo, abas de navegação (Painel, Vendas, Pedidos, Cadastros, Fechar caixa, Configurações), chip do operador com o caixa (`Carlos • Caixa 01`) e o botão de sair, pílula de **conexão** (online, offline, "online com falhas") e pílula **"Sync: N pendentes"** (abre o painel lateral da fila).
- **Navegação bloqueada** sem caixa aberto (exceto Configurações) e **enquanto há venda em andamento** (senão o carrinho se perderia).
- **Fluxo de entrada:** dispositivo não vinculado → Configurações; senão → Login → (se a regra exigir) Abrir caixa → Painel.
- **Avisos temporários (toasts)** no canto inferior direito: conexão caiu (vermelho), conexão voltou e fila vazia (verde), "produto adicionado ao cupom". Somem sozinhos.
- **Banner de erro inesperado:** exceção em comando vira uma faixa, nunca derruba o app.
- **Painel lateral da Fila:** abre pela pílula "Sync"; mostra as pendências, o botão "Disparar sincronização agora" e um **log em linguagem de operador** (últimas 100 atividades, sem token nem detalhe técnico).

## 1. Configurações
- **Vincular o dispositivo:** colar o link de cadastro + nome do dispositivo → obtém e guarda o `client_secret` protegido.
- **Regras do PDV:** exigir abertura de caixa antes de vender (sim/não), id do cliente "Consumidor Final" (opcional; deduzido pelo indicador), **código deste PDV** (prefixo do número da venda, ver `softcomshop-sync`).
- **Consulta "Caixas no SoftcomShop"** (modal só de leitura): filtro por data (dd/mm/aaaa), "somente fechados", paginação de 15.
- **Regra:** trocar para OUTRA empresa com dados locais é recusado (misturaria os dados).

## 2. Login
- **Escolher o operador numa lista** (só os ativos com chave) e digitar a chave dele; a chave é conferida só contra o hash **daquele** operador. Motivo: a API não garante chave única.
- **Espera crescente** depois de 3 erros seguidos (o botão avisa quanto falta). Lista vazia (catálogo ainda não chegou): aviso "Nenhum operador disponível", que se preenche sozinho depois da sincronização.
- Mensagem de erro **genérica** ("Chave inválida"), sem dizer se o operador existe ou está desativado.

## 3. Abrir caixa
- **Troco inicial** (aceita vírgula ou ponto) e **turno** (1 a 6, os do SoftcomShop). Sugere o primeiro turno de hoje ainda livre para aquele operador.
- Grava local na hora; a abertura sobe depois.

## 4. Painel Principal
- **Quatro cartões:** faturamento de hoje (do caixa aberto), produtos cadastrados, fila de envio (pendentes), conexão + última sincronização.
- **Últimas 5 vendas** com o selo de sincronia e o botão Detalhes; aviso de modo offline-first. Atalho **F2** = nova venda.

## 5. Vendas (o PDV)
- **Busca grande** por nome, código ou código de barras: **Enter** com um código de barras já adiciona o produto (leitor). Botão Limpar.
- **Grade de cartões de produto:** código • categoria, nome, preço, estoque. Produto **sem preço (R$ 0,00)** mostra "Informar preço" e abre um painel que exige um valor > 0 (vale só para aquele item).
- **Cupom à direita:** itens numerados, quantidade (fracionada até 3 casas, para venda por peso), cliente (padrão "Consumidor Final"), subtotal, **desconto na venda toda** (campo em R$ ou %, com Enter/Aplicar; repartido entre os itens porque a API só aceita desconto por item), **total**, botão amarelo **Finalizar (F10)**.
- **Painel de pagamento:** formas em cartões (2 colunas), **pagamento misto** (adiciona uma forma de cada vez), no dinheiro o campo "valor recebido" e o **troco** em tempo real (só o dinheiro pode passar do que falta), e a **bandeira** do cartão (combo, obrigatória se houver bandeiras sincronizadas).
- **Atalhos:** F2 novo, F4 buscar, F10 pagar, Esc cancelar/voltar. Finalizar **não faz chamada de rede**: grava local e volta.

## 6. Listagem de Pedidos
- **Indicadores** (quatro) + **busca** (número/cliente, sem acento) + filtros de **status** e de **forma de pagamento**.
- **Tabela:** nº, data/hora, cliente, pagamento, valor, selo de sincronia (com o **motivo** quando espera ou falha), ações.
- **Detalhes do pedido** (modal): cabeçalho, itens, pagamento, total líquido e a **requisição real** (POST /vendas, token mascarado) montada pelo mesmo código do envio.
- **Ações:** "Sincronizar fila", "Reenviar falhas", **descartar venda** com motivo (só vendas em falha; registra quem pediu e quem autorizou; nunca `DELETE`).

## 7. Cadastros
- **Três abas:** Produtos, Clientes, Operadores de Caixa, cada uma com a contagem total; **uma busca só** (Enter ou 🔍).
- **Produtos:** SKU/código, descrição, categoria (o **nome** do grupo, não o id), preço, estoque local, status da nuvem. **Novo Produto** (modal): nome, SKU/código, categoria (combo), preço, estoque inicial.
- **Clientes:** id, nome/razão, CPF/CNPJ, telefone, cidade/UF, status da nuvem (a **mensagem de erro** do envio fica visível na linha). **Novo Cliente** (modal): nome e CPF/CNPJ obrigatórios; telefone, e-mail e endereço opcionais, com **busca por CEP** que preenche o endereço e guarda o código da cidade.
- **Operadores:** só leitura (perfil, caixa aberto, situação); nunca mostra CPF nem chave.
- **"Reenviar falhas"** devolve à fila o que falhou ou desistiu.

## 8. Fechar caixa
- **Conferência:** total vendido, **apuração por forma de pagamento** (já preenchida com o esperado; o operador ajusta), **apuração por bandeira** de cartão, troco final.
- **Só fecha sem venda pendente** (a tela avisa, bloqueia o botão e se atualiza quando a sincronização termina).
- Grava local; a fila envia (as vendas sobem **antes** do fechamento, que as resume).

## Peças reutilizáveis (não copie XAML, extraia)
Selo de sincronia, cartão indicador (título, valor grande, frase), painel modal (fundo escurecido, tela de trás desabilitada, Esc fecha), estilos globais de campo de texto, combo e botões. Modais precisam de **caminho de foco** (devolver o foco ao fechar).

## Ordem sugerida para construir
Casca e navegação → Configurações → Login → Abrir caixa → Painel → Vendas → Pedidos → Cadastros (só leitura e cliente simples) → Fechar caixa → (depois) apuração por bandeira, painel de pagamento misto, modais de cadastro, painel lateral da fila e toasts. **Cada tela só está pronta quando alguém a abriu, clicou e viu funcionar** — compilar não prova.

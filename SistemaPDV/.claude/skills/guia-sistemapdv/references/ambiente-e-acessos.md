# Ambiente e acessos

O que a pessoa precisa ter **antes** da primeira tarefa, e os comandos que ela vai usar o tempo todo.

## Ambiente
- **Windows 10 ou 11.** O app protege segredos com DPAPI (só Windows) e é feito para rodar lá.
- **.NET SDK 8.**
- **Git.** E, para abrir PRs pelo terminal, o GitHub CLI (`gh`): `winget install GitHub.cli` e depois `gh auth login`. Opcional: dá para abrir o PR pelo navegador.
- **Uma IDE:** Visual Studio, Rider ou VS Code com o C# Dev Kit.
- **A ferramenta de migrations:** `dotnet tool install --global dotnet-ef`.

## Estrutura da solução
```
SistemaPDV.slnx          a solução
SistemaPDV/              o aplicativo (Models, Data, Services, ViewModels, Views, Themes)
SistemaPDV.Tests/        os testes (xUnit + SQLite em memória)
SistemaPDV/docs/         RESUMO-ARQUITETURA.md e APRENDIZADOS.md
SistemaPDV/specs/        uma spec por módulo
SistemaPDV/tasks/        plan.md e todo.md
SistemaPDV/.claude/skills/   as skills do projeto (esta, softcomshop-sync, fila-outbox)
```

## Comandos do dia a dia
Na pasta da solução:
```
dotnet build                                    # deve terminar com 0 avisos
dotnet test                                     # a suíte inteira, sempre
dotnet test --filter "FullyQualifiedName~Nome"  # só os testes de um assunto
dotnet run --project SistemaPDV                 # abrir o app
dotnet ef migrations add NomeDaMudanca --project SistemaPDV   # nova migration
```
Depois de gerar uma migration, **abra o arquivo** e confira que o `Up()` só faz o que você mudou (uma coluna nova deve ser só uma coluna nova).

## Onde ficam os dados de execução
- **Banco local:** `pdv.db` (com `pdv.db-wal` e `pdv.db-shm` ao lado), na pasta do executável. Quando rodado por `dotnet run`, fica em `bin/Debug/net8.0/`.
- **Log do app:** `%LOCALAPPDATA%\SistemaPDV\logs\pdv-AAAAMMDD.log` (um por dia; mascara segredos e CPF/CNPJ; corta a resposta da API em ~300 caracteres).
- **Para ler o banco sem risco:** copie os **três** arquivos (`.db`, `-wal`, `-shm`) para outra pasta e abra a cópia, nunca o original.

## Acessos que a pessoa precisa receber de quem coordena o projeto
Isto **não está no repositório e nunca deve ir para ele:**
1. **O ambiente de treinamento do SoftcomShop** (um servidor de testes, não o de produção).
2. **O link de cadastro do dispositivo** (usado na tela de Configurações para vincular o app; dele sai o `client_id` e o `client_secret`).
3. **Um operador com chave (`pdv_key`)** para fazer login no app.
4. **O Swagger** das rotas usadas (rotas, campos, respostas). Lembre: ele é referência, não verdade (regra 4 do guia).
5. **Os prints do protótipo** das telas, para o visual.

Onde guardar: fora da pasta do projeto (um arquivo local, ou o gerenciador de senhas). O app guarda o segredo já protegido pelo DPAPI na conta do Windows.

## Como conferir o que se fez
- **Testes:** `dotnet test` verde.
- **Tela:** rode o app e clique. Para conferir o visual sem abrir o app real (que sincronizaria com a API), a técnica usada foi montar a tela num renderizador **headless** (Avalonia.Headless + Skia) com dados de exemplo e salvar uma imagem — veja o APRENDIZADOS #72.
- **API:** só leitura, na cópia do banco, seguindo a skill `softcomshop-sync` (arquivo `references/investigar-a-api-com-seguranca.md`).

## O que pedir a quem coordena, se faltar
- Um banco de exemplo já sincronizado, para começar sem depender da rede.
- A lista do que está **em espera** (hoje: preço e estoque do produto novo, o bairro na tela do SoftcomShop, o valor recebido e o troco, a chave de supervisor).

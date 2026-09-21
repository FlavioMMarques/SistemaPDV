using SistemaPDV.Models;

namespace SistemaPDV.Services;

// Linhas de leitura da tela de Cadastros — projeção enxuta em vez da entidade
// inteira (Cliente tem ~40 campos), mesmo padrão de VendaResumo.

// IdExterno = o id do cliente na API (o "#1" da tabela); nulo = ainda não subiu (cliente criado aqui, pendente de envio).
public record ClienteResumo(
    int Id,
    string Nome,
    string? Documento,
    SyncStatus SyncStatus,
    string? UltimoErroSync,
    int? IdExterno = null,
    string? Telefone = null,
    string? CidadeUf = null);

// Codigo = o que a tabela mostra como "SKU / código" (código de barras, senão SKU, senão o id da API — o mesmo do card do PDV);
// Categoria = o nome do grupo (nulo se o grupo ainda não sincronizou).
public record ProdutoResumo(
    int Id,
    string Nome,
    string? CodigoBarras,
    decimal PrecoVenda,
    int EstoqueAtual,
    SyncStatus SyncStatus,
    string? Codigo = null,
    string? Categoria = null,
    string? Unidade = null,
    string? UltimoErroSync = null);

public enum SituacaoOperador
{
    Ativo,

    // Desativado no SoftcomShop: não entra mais.
    Desativado,

    // Sincronizado sem chave do PDV: existe, mas não consegue entrar no app (o login é pela chave).
    SemChaveDoPdv,
}

// Operador de caixa (Funcionario). "Perfil" vem da flag de supervisor; "CaixaAberto" é o caixa que ele tem aberto agora neste
// terminal (nulo = nenhum). O CPF do funcionário fica de fora de propósito: a tela não precisa dele.
public record OperadorResumo(int Id, string Codigo, string Nome, string Perfil, bool Supervisor, string? CaixaAberto, SituacaoOperador Situacao)
{
    public string RotuloSituacao => Situacao switch
    {
        SituacaoOperador.Ativo => "Ativo",
        SituacaoOperador.Desativado => "Desativado",
        _ => "Sem chave do PDV",
    };

    public bool Ativo => Situacao == SituacaoOperador.Ativo;
    public bool Desativado => Situacao == SituacaoOperador.Desativado;
    public bool SemChave => Situacao == SituacaoOperador.SemChaveDoPdv;
}

// Quantos cadastros existem NO TOTAL (os números das abas): independem da busca e do corte de LimiteLista da lista.
public record ContagemCadastros(int Produtos, int Clientes, int Operadores);

public class ResultadoCriacaoCliente
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public int? ClienteId { get; private init; }

    public static ResultadoCriacaoCliente ComSucesso(int clienteId) => new() { Sucesso = true, ClienteId = clienteId };
    public static ResultadoCriacaoCliente ComFalha(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

// O que o modal "Cadastrar Cliente" preenche. Só Nome e CPF/CNPJ são obrigatórios; o resto é opcional.
// Telefone: "(83) 99999-8888" (DDD + número); CidadeUf: "João Pessoa - PB".
public record NovoClienteDados(string? Nome, string? CpfCnpj, string? Telefone = null, string? Email = null, string? CidadeUf = null);

// O que o modal "Cadastrar Produto" preenche. Preço e estoque chegam como texto digitado ("12,50" / "50"); a categoria é o
// IdExterno do Grupo sincronizado (a API exige um grupo que já exista lá).
public record NovoProdutoDados(string? Nome, string? Codigo, int? GrupoId, string? Preco, string? Estoque = null);

// Uma categoria do combo do modal: só grupos que já têm par na API (IdExterno) — sem isso o produto não seria aceito lá.
public record CategoriaResumo(int GrupoId, string Nome);

public class ResultadoCriacaoProduto
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public int? ProdutoId { get; private init; }

    public static ResultadoCriacaoProduto ComSucesso(int produtoId) => new() { Sucesso = true, ProdutoId = produtoId };
    public static ResultadoCriacaoProduto ComFalha(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

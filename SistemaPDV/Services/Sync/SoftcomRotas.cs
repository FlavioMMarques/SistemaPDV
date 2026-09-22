namespace SistemaPDV.Services.Sync;

// Todas as rotas da API SoftcomShop, num lugar só. Descoberto ao testar contra a API real
// (2026-09-20): TODAS ficam sob "softauth/api/v2/" — sem o "softauth/" o servidor responde
// 500 com {"error":""} (rota inexistente), inclusive nas de caixa e venda. Antes o prefixo
// era escrito à mão em cada serviço, e três dos cinco caminhos do catálogo estavam sem ele
// (a sincronização nunca funcionou de verdade — os testes usavam um servidor falso).
public static class SoftcomRotas
{
    public const string Prefixo = "softauth/api/v2";

    // Leitura (paginadas). Sem barra inicial: SoftcomApiClient.BuscarTudoAsync já junta com o domínio.
    public const string FormasPagamento = Prefixo + "/financeiro/forma-pagamento/page/1";
    public const string Clientes = Prefixo + "/clientes/clientes";
    public const string Produtos = Prefixo + "/produtos/produtos";
    public const string Funcionarios = Prefixo + "/funcionarios";
    public const string Empresa = Prefixo + "/empresa/empresas/1";

    // EXCEÇÃO: a rota de cartões fica em "softauth/api/" SEM o "v2" (como o token, "softauth/authentication/token") —
    // sob "softauth/api/v2/…" ela responde 500 "Resource not found." (conferido na API real em 2026-09-20). O
    // envelope também é outro (PaginaMetaApiDto) e o número da página vai no fim do caminho.
    public const string Cartoes = "softauth/api/financeiros/cartoes/page";

    // Grupos (categorias) de produto: sob "v2" e com o envelope padrão de página (conferido na API real em 2026-09-21). Sem
    // "/page/N" — com ele a rota responde 500; a paginação vem por next_page_url. A rota SEM v2 também responde, mas com o
    // outro envelope (o dos cartões) e tudo em texto.
    public const string Grupos = Prefixo + "/produtos/grupos";

    // Escrita (URL completa: EnviarAsync recebe a URL inteira).
    public static string CaixaAbrir(string dominio) => $"{dominio}/{Prefixo}/financeiro/caixa-funcoes/abrir";
    public static string CaixaFechar(string dominio) => $"{dominio}/{Prefixo}/financeiro/caixa-funcoes/fechar";
    // GET: lista os caixas da empresa (últimos 7 dias, ou o intervalo informado) — só leitura, tela "Caixas no SoftcomShop".
    public static string CaixasListar(string dominio) => $"{dominio}/{Prefixo}/financeiro/caixa-funcoes";
    public static string Vendas(string dominio) => $"{dominio}/{Prefixo}/vendas";
    public static string ClientesCriar(string dominio) => $"{dominio}/{Prefixo}/clientes/clientes";
    public static string ProdutosCriar(string dominio) => $"{dominio}/{Prefixo}/produtos/produtos";

    // EXPERIMENTAL (2026-09-22): trocando o cadastro de produto pelo endpoint de importação v1 — sem "/v2/", como os
    // cartões —, na hipótese de que ele grava o preço no registro da EMPRESA (produto_empresa_grade), o que o lote v2 não
    // faz (chega com preco_venda 0 lá; ver APRENDIZADOS #86/#94). Ainda não confirmado com produto real.
    public static string ProdutosImportar(string dominio) => $"{dominio}/softauth/api/produtos/importacao/produto";
}

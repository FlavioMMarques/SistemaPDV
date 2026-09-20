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

    // Escrita (URL completa: EnviarAsync recebe a URL inteira).
    public static string CaixaAbrir(string dominio) => $"{dominio}/{Prefixo}/financeiro/caixa-funcoes/abrir";
    public static string CaixaFechar(string dominio) => $"{dominio}/{Prefixo}/financeiro/caixa-funcoes/fechar";
    public static string Vendas(string dominio) => $"{dominio}/{Prefixo}/vendas";
    public static string ClientesCriar(string dominio) => $"{dominio}/{Prefixo}/clientes/clientes";
}

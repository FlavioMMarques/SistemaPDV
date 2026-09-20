namespace SistemaPDV.Services.Sync;

public enum ResultadoEnvioTipo
{
    Sucesso,
    Conflito,

    // 401 é diferenciado de Falha de propósito: um token expirado no meio de uma
    // sincronização longa (ex: CatalogSyncService.SincronizarTudoAsync, que reusa o
    // mesmo token pra 5 chamadas em sequência) é uma causa BEM diferente de um erro
    // de validação — mas hoje os chamadores ainda tratam TokenExpirado igual a
    // Falha (não há renovação automática de token ainda). Achado numa revisão de
    // código (docs/APRENDIZADOS.md) e deixado registrado aqui pra quando fizer
    // sentido implementar o retry com token renovado.
    TokenExpirado,

    // A URL configurada não é HTTPS (nem loopback) — nada foi enviado. É problema de
    // CONFIGURAÇÃO, não da entidade: quem chama devolve falha SEM marcar o
    // caixa/venda/cliente como FalhaSync (não é culpa dele, e ele não deve acumular
    // tentativas por isso). Ver ConexaoSegura.
    ConexaoInsegura,

    Falha,
}

public class ResultadoEnvio
{
    public ResultadoEnvioTipo Tipo { get; private init; }
    public string Conteudo { get; private init; } = string.Empty;

    public static ResultadoEnvio Criar(ResultadoEnvioTipo tipo, string conteudo) =>
        new() { Tipo = tipo, Conteudo = conteudo };
}

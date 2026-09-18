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

    Falha,
}

public class ResultadoEnvio
{
    public ResultadoEnvioTipo Tipo { get; private init; }
    public string Conteudo { get; private init; } = string.Empty;

    public static ResultadoEnvio Criar(ResultadoEnvioTipo tipo, string conteudo) =>
        new() { Tipo = tipo, Conteudo = conteudo };
}

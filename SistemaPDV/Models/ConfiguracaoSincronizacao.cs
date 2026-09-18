using System;

namespace SistemaPDV.Models;

// Config local de sincronização — não é ISincronizavel: é dado do próprio dispositivo,
// nunca sobe/desce da API. Cada recurso tem seu próprio marcador de "última sincronização"
// porque cada um devolve um date_sync independente (não faria sentido um único marcador
// global quando os recursos podem sincronizar em momentos diferentes).
public class ConfiguracaoSincronizacao
{
    public int Id { get; set; }

    public string? UrlApi { get; set; }
    public string? ApiClienteId { get; set; }
    public string? ApiClienteSecretProtegido { get; set; }
    public string? NomeDispositivo { get; set; }

    public DateTimeOffset? UltimaSincronizacaoProdutos { get; set; }
    public DateTimeOffset? UltimaSincronizacaoClientes { get; set; }
    public DateTimeOffset? UltimaSincronizacaoFormasPagamento { get; set; }
    public DateTimeOffset? UltimaSincronizacaoEmpresa { get; set; }
    public DateTimeOffset? UltimaSincronizacaoFuncionarios { get; set; }

    // Id externo do cliente "Consumidor Final", usado em venda avulsa (sem cliente
    // selecionado) — configurável em vez de um número cravado no código, porque a
    // evidência de que é id=1 é forte mas não 100% confirmada (ver SPEC-sales.md).
    public int? ClienteConsumidorFinalIdExterno { get; set; }

    // Regra de negócio configurável (não fixa no código), decidida com o usuário
    // (2026-09-18): quando true, o pdv-ui exige caixa aberto antes de liberar
    // Dashboard/PDV pós-login; quando false, abrir caixa vira uma ação opcional
    // pelo menu. Default true porque é o comportamento mais seguro pra um PDV real
    // (evita vender sem controle de caixa por engano).
    public bool ExigirAberturaCaixa { get; set; } = true;
}

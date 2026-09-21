using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync.Dtos;

namespace SistemaPDV.Services.Sync;

// Cartões (credenciadora × bandeira × tipo × parcelas): de onde vem a lista de bandeiras do app. Em arquivo próprio
// (partial) porque CatalogSyncService já é o maior arquivo do projeto e este recurso tem particularidades (rota sem
// "v2", outro envelope de paginação, sincronização por SUBSTITUIÇÃO).
public partial class CatalogSyncService
{
    // A lista é pequena (uma linha por cartão cadastrado) e a API não tem como avisar de um cartão REMOVIDO, então cada
    // ciclo baixa tudo e o conjunto local passa a ser exatamente o da API: atualiza os que mudaram, cria os novos e
    // apaga os que sumiram. Só depois de a busca INTEIRA dar certo — uma falha no meio nunca esvazia a lista local.
    // Apagar é seguro: PagamentoVenda guarda só o NOME da bandeira (texto), sem chave estrangeira pra cá.
    //
    // Quantidade devolvida = quantos registros MUDARAM (não quantos vieram): o ciclo de 5 min sempre baixa a lista
    // toda, e contar todos faria as telas se recarregarem à toa a cada ciclo (ver TrouxeAlgo).
    public async Task<ResultadoSincronizacaoRecurso> SincronizarCartoesAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);

        var resultado = await apiClient.BuscarPaginasAsync<CartaoApiDto>(dominio, SoftcomRotas.Cartoes, accessToken, ct);
        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar cartões.");

        // Se o mesmo id vier em mais de uma página, vale o último.
        var daApi = resultado.Itens.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.Last());
        var locais = await context.Cartoes.ToListAsync(ct);
        var porIdExterno = new Dictionary<int, Cartao>();
        foreach (var local in locais)
        {
            if (local.IdExterno is { } id && daApi.ContainsKey(id))
                porIdExterno[id] = local;
            else
                context.Cartoes.Remove(local);   // sumiu da API (ou nunca teve id)
        }

        foreach (var (id, dto) in daApi)
        {
            if (!porIdExterno.TryGetValue(id, out var cartao))
            {
                cartao = new Cartao { IdExterno = id };
                context.Cartoes.Add(cartao);
            }

            cartao.Credenciadora = dto.Credenciadora?.Trim() ?? string.Empty;
            cartao.Nome = dto.Nome?.Trim() ?? string.Empty;
            cartao.BandeiraId = dto.BandeiraId?.Trim() ?? string.Empty;
            cartao.BandeiraNome = dto.BandeiraNome?.Trim() ?? string.Empty;
            cartao.Tipo = dto.Tipo?.Trim() ?? string.Empty;
            cartao.AliasCartao = dto.AliasCartao?.Trim() ?? string.Empty;
            cartao.Dia = dto.Dia;
            cartao.Parcelas = dto.Parcelas;
            cartao.TaxaAdministrativa = dto.TaxaAdministrativa;
            cartao.SyncStatus = SyncStatus.Sincronizado;
        }

        var alterados = await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(alterados);
    }
}

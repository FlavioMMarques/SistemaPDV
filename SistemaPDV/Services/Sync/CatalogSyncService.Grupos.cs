using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync.Dtos;

namespace SistemaPDV.Services.Sync;

// Grupos (categorias) de produto. Em arquivo próprio (partial), como os cartões, porque CatalogSyncService já é o maior
// arquivo do projeto.
public partial class CatalogSyncService
{
    // A lista é pequena (dezenas de grupos) e a API não avisa de um grupo REMOVIDO, então cada ciclo baixa tudo e o conjunto
    // local passa a ser exatamente o da API: atualiza os que mudaram, cria os novos e apaga os que sumiram — só depois de a
    // busca INTEIRA dar certo (uma falha no meio nunca esvazia a lista local). Apagar é seguro: Produto.GrupoId é só um número,
    // sem chave estrangeira; o produto de um grupo que sumiu apenas volta a mostrar a unidade no lugar da categoria.
    //
    // Quantidade devolvida = quantos registros MUDARAM (não quantos vieram), como nos cartões: baixar tudo a cada ciclo não
    // pode fazer as telas se recarregarem à toa.
    public async Task<ResultadoSincronizacaoRecurso> SincronizarGruposAsync(string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);

        // ultimaSincronizacao nulo = sempre a lista completa (a incremental esconderia o que foi removido).
        var resultado = await apiClient.BuscarTudoAsync<GrupoApiDto>(dominio, SoftcomRotas.Grupos, null, accessToken, ct);
        if (!resultado.Sucesso)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Mensagem ?? "Falha desconhecida ao sincronizar grupos.");

        // Se o mesmo id vier em mais de uma página, vale o último.
        var daApi = resultado.Itens.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.Last());
        var locais = await context.Grupos.ToListAsync(ct);
        var porIdExterno = new Dictionary<int, Grupo>();
        foreach (var local in locais)
        {
            if (local.IdExterno is { } id && daApi.ContainsKey(id))
                porIdExterno[id] = local;
            else
                context.Grupos.Remove(local);   // sumiu da API (ou nunca teve id)
        }

        foreach (var (id, dto) in daApi)
        {
            if (!porIdExterno.TryGetValue(id, out var grupo))
            {
                grupo = new Grupo { IdExterno = id };
                context.Grupos.Add(grupo);
            }

            grupo.Nome = dto.Nome?.Trim() ?? string.Empty;
            grupo.SyncStatus = SyncStatus.Sincronizado;
        }

        var alterados = await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(alterados);
    }
}

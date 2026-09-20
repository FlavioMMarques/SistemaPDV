using System;
using System.Threading;
using System.Threading.Tasks;
using SistemaPDV.Data;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Services;

// Compartilhado por CaixaSyncService e VendaSyncService: os dois tinham um
// MarcarFalhaAsync privado quase idêntico (só define propriedades numa entidade já
// carregada e salva — sem consulta nenhuma, então sem o risco de tradução de LINQ
// que fez o catalog-sync NÃO compartilhar sua lógica de upsert). `aplicarErro` deixa
// cada chamador decidir quais campos exatos mexer (ex: Venda também incrementa
// TentativasEnvio, Caixa não).
public static class OutboxHelper
{
    public static async Task<ResultadoSincronizacaoRecurso> MarcarFalhaAsync<T>(
        AppDbContext context, T entidade, string mensagem, Action<T, string> aplicarErro, CancellationToken ct)
    {
        aplicarErro(entidade, mensagem);
        await context.SaveChangesAsync(ct);
        Registro.Aviso("Envio", $"{typeof(T).Name} não foi aceita pela API: {mensagem}");
        return ResultadoSincronizacaoRecurso.ComFalha(mensagem);
    }
}

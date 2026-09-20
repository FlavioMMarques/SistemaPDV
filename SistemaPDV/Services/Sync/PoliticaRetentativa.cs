using System;
using System.Linq.Expressions;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Sync;

// Espera crescente + teto de tentativas dos outboxes. Antes, um item com erro permanente
// (ex: 422 de um cadastro inválido) era reenviado a cada 30 s pra sempre, com um token
// novo a cada vez — barulho pra API e nenhuma chance de resolver sozinho.
//
// Falhou -> espera 30 s, 1 min, 2 min, 4 min, 8 min, 10 min (teto da espera);
// passou de MaximoTentativas -> desiste ("parou de tentar") até alguém pedir pra reenviar
// (botão "Reenviar falhas", que zera o contador) — ou até dar certo, que também zera.
//
// Vale só pro envio automático em lote. Enviar UM item explicitamente
// (SincronizarClienteNovoAsync(id)) ignora a espera, mas conta a tentativa.
public static class PoliticaRetentativa
{
    // 8 tentativas ≈ 30 s + 1 + 2 + 4 + 8 + 10 + 10 min ≈ 35 min de insistência antes de
    // desistir — tempo pra uma queda de rede/instabilidade passar sem virar loop eterno.
    public const int MaximoTentativas = 8;

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(10);

    public static TimeSpan Espera(int tentativas)
    {
        var expoente = Math.Min(Math.Max(tentativas - 1, 0), 10);   // limita antes de potenciar
        var espera = TimeSpan.FromTicks(EsperaInicial.Ticks * (1L << expoente));
        return espera > EsperaMaxima ? EsperaMaxima : espera;
    }

    public static bool Esgotou(IOutboxRetentavel entidade) => entidade.TentativasEnvio >= MaximoTentativas;

    // Registra a falha na entidade e devolve a mensagem a gravar em UltimoErroSync: a
    // mesma que veio, ou — na última tentativa — com o aviso de que parou e como retomar.
    public static string RegistrarFalha(IOutboxRetentavel entidade, DateTime agoraUtc, string mensagem)
    {
        entidade.TentativasEnvio += 1;

        if (Esgotou(entidade))
        {
            entidade.ProximaTentativaEm = null;
            return $"{mensagem} (parou de tentar após {MaximoTentativas} tentativas — use \"Reenviar falhas\" para tentar de novo)";
        }

        entidade.ProximaTentativaEm = agoraUtc + Espera(entidade.TentativasEnvio);
        return mensagem;
    }

    public static void Zerar(IOutboxRetentavel entidade)
    {
        entidade.TentativasEnvio = 0;
        entidade.ProximaTentativaEm = null;
    }

    // Filtro pro banco: ainda não esgotou E (nunca falhou OU a espera já venceu). Montado
    // como expressão sobre o tipo concreto (não via interface) pra o EF traduzir pra SQL.
    public static Expression<Func<T, bool>> Elegivel<T>(DateTime agoraUtc) where T : class, IOutboxRetentavel
    {
        var entidade = Expression.Parameter(typeof(T), "e");
        var tentativas = Expression.Property(entidade, nameof(IOutboxRetentavel.TentativasEnvio));
        var proxima = Expression.Property(entidade, nameof(IOutboxRetentavel.ProximaTentativaEm));

        var naoEsgotou = Expression.LessThan(tentativas, Expression.Constant(MaximoTentativas));
        var semEspera = Expression.Equal(proxima, Expression.Constant(null, typeof(DateTime?)));
        var esperaVenceu = Expression.LessThanOrEqual(proxima, Expression.Constant(agoraUtc, typeof(DateTime?)));

        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(naoEsgotou, Expression.OrElse(semEspera, esperaVenceu)), entidade);
    }
}

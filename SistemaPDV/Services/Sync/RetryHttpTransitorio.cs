using System;
using System.Net.Http;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

namespace SistemaPDV.Services.Sync;

// Retry curto (3 tentativas, 200ms/400ms/800ms) só pra falha de rede transitória —
// timeout, DNS, conexão recusada — coisa que passa sozinha em segundos. Nunca decide
// por status HTTP (401/409/422/500 continuam sendo classificados como sempre pelos
// chamadores, sem retry aqui). Compartilhado entre SoftcomApiClient e
// SoftcomAuthService pra não duplicar essa política (e o ajuste fino dela) em dois
// lugares — mesmo raciocínio de PdvKeyHasher, extraído por não poder divergir em
// silêncio entre dois usos.
//
// Isso é diferente da PoliticaRetentativa (espera de 30s a 10min entre CICLOS do
// outbox, persistida no banco, sobrevive a reinício do app): uma resolve o blip
// dentro de UMA chamada, a outra decide se vale tentar de novo minutos depois.
public static class RetryHttpTransitorio
{
    private const int Tentativas = 3;

    public static readonly AsyncRetryPolicy Politica = Policy
        .Handle<HttpRequestException>()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(Tentativas, tentativa => TimeSpan.FromMilliseconds(200 * Math.Pow(2, tentativa - 1)));
}

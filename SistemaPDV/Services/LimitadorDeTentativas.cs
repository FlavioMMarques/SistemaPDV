using System;

namespace SistemaPDV.Services;

// Atraso progressivo depois de erros seguidos de chave. O bcrypt já deixa cada tentativa lenta, mas nada impedia alguém
// de ficar testando chaves sem parar num PDV exposto. Regra:
//   - as primeiras TentativasLivres falhas seguidas passam sem atraso (erro de digitação acontece);
//   - a partir da seguinte, o app exige uma espera antes de aceitar outra tentativa: 5 s, 10 s, 20 s… até 5 min;
//   - acertar zera tudo; tentativas feitas DURANTE a espera são ignoradas (não contam, não estendem a espera).
//
// O estado fica em memória (uma instância por serviço): fechar e reabrir o app zera o contador. É uma proteção contra
// tentativa de adivinhar chave na frente do balcão, não contra quem tem acesso ao computador (esse já abre o banco).
public class LimitadorDeTentativas
{
    public const int TentativasLivres = 3;

    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(5);

    private readonly TimeProvider tempo;
    private readonly object trava = new();
    private int falhasSeguidas;
    private DateTimeOffset bloqueadoAte;

    public LimitadorDeTentativas(TimeProvider? tempo = null)
    {
        this.tempo = tempo ?? TimeProvider.System;
    }

    // Quanto falta até poder tentar de novo (zero = pode tentar agora).
    public TimeSpan EsperaRestante
    {
        get
        {
            lock (trava)
            {
                var restante = bloqueadoAte - tempo.GetUtcNow();
                return restante > TimeSpan.Zero ? restante : TimeSpan.Zero;
            }
        }
    }

    // Registra uma tentativa errada e devolve a espera que ela passou a exigir (zero enquanto ainda está nas livres).
    public TimeSpan RegistrarFalha()
    {
        lock (trava)
        {
            falhasSeguidas++;
            var espera = EsperaPara(falhasSeguidas);
            bloqueadoAte = tempo.GetUtcNow() + espera;
            return espera;
        }
    }

    public void Zerar()
    {
        lock (trava)
        {
            falhasSeguidas = 0;
            bloqueadoAte = default;
        }
    }

    public static TimeSpan EsperaPara(int falhasSeguidas)
    {
        if (falhasSeguidas <= TentativasLivres)
            return TimeSpan.Zero;

        var expoente = Math.Min(falhasSeguidas - TentativasLivres - 1, 10);   // limita antes de potenciar
        var espera = TimeSpan.FromTicks(EsperaInicial.Ticks * (1L << expoente));
        return espera > EsperaMaxima ? EsperaMaxima : espera;
    }

    // "5 s", "40 s", "3 min" — sempre arredondando pra cima (dizer "0 s" com a espera ainda valendo confunde).
    public static string Descrever(TimeSpan espera)
    {
        var segundos = (int)Math.Ceiling(espera.TotalSeconds);
        return segundos < 60 ? $"{segundos} s" : $"{(int)Math.Ceiling(segundos / 60.0)} min";
    }
}

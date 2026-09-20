namespace SistemaPDV.Tests;

// TimeProvider controlável: o teste decide quando "passa o tempo" (espera crescente de
// retentativa) sem Thread.Sleep.
public class RelogioFalso : TimeProvider
{
    private DateTimeOffset agora = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => agora;

    public DateTime AgoraUtc => agora.UtcDateTime;

    public void Avancar(TimeSpan tempo) => agora += tempo;
}

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;

/// <summary>
/// Orologio fermo a un istante: i dati del seed hanno date relative a un giorno fisso, e ciò che dipende dal tempo trascorso
/// (la scadenza sul gate) deve dare lo stesso esito in qualunque giorno si eseguano i test.
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

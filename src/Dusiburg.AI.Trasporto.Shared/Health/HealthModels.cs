using System.Diagnostics;

namespace Dusiburg.AI.Trasporto.Shared.Health;

/// <summary>
/// Esito di un controllo. Nomi diversi da <c>HealthStatus</c>/<c>HealthCheckResult</c> di ASP.NET, con cui convivono nell'API.
/// </summary>
public enum ProbeStatus
{
    Ok,

    /// <summary>Solo informativo, non incide sull'esito (es. posizionamento dei modelli).</summary>
    Info,

    /// <summary>Da sistemare ma non bloccante (es. database non ancora creato).</summary>
    Warning,

    Error
}

/// <param name="Number">Numero del controllo nella tabella di <c>docs/architettura.md</c>: ordina l'output.</param>
public sealed record ProbeResult(int Number, string Name, ProbeStatus Status, string Detail, TimeSpan Duration)
{
    /// <summary>Controllo non eseguito perché ne è fallito uno da cui dipende.</summary>
    public static ProbeResult Skipped(int number, string name, string reason) =>
        new(number, name, ProbeStatus.Error, $"non eseguito: {reason}", TimeSpan.Zero);

    /// <summary>Esegue un passo misurandone la durata; un'eccezione diventa un esito <see cref="ProbeStatus.Error"/> con il messaggio.</summary>
    public static async Task<ProbeResult> MeasureAsync(int number, string name, Func<Task<(ProbeStatus Status, string Detail)>> step)
    {
        long start = Stopwatch.GetTimestamp();

        try
        {
            (ProbeStatus status, string detail) = await step();

            return new ProbeResult(number, name, status, detail, Stopwatch.GetElapsedTime(start));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ProbeResult(number, name, ProbeStatus.Error, exception.Message, Stopwatch.GetElapsedTime(start));
        }
    }
}

/// <summary>Gruppo di controlli su una dipendenza esterna (SQL, runtime dei modelli).</summary>
public interface IHealthProbe
{
    Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken);
}

public sealed record ProbeReport(IReadOnlyList<ProbeResult> Checks)
{
    public bool HasErrors => Checks.Any(c => c.Status == ProbeStatus.Error);

    public bool HasWarnings => Checks.Any(c => c.Status == ProbeStatus.Warning);
}

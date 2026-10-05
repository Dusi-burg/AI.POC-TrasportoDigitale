namespace Dusiburg.AI.Trasporto.Shared.Health;

/// <summary>Esegue tutti i probe registrati e ne aggrega gli esiti: lo usano <c>/api/health</c> e l'health check dell'API.</summary>
public sealed class HealthService(IEnumerable<IHealthProbe> probes)
{
    public async Task<ProbeReport> RunAsync(CancellationToken cancellationToken)
    {
        // In parallelo: SQL e Ollama sono indipendenti e il controllo resta veloce anche con un servizio che va in timeout.
        IReadOnlyList<ProbeResult>[] results = await Task.WhenAll(probes.Select(probe => RunProbeAsync(probe, cancellationToken)));

        return new ProbeReport([.. results.SelectMany(r => r).OrderBy(r => r.Number)]);
    }

    private static async Task<IReadOnlyList<ProbeResult>> RunProbeAsync(IHealthProbe probe, CancellationToken cancellationToken)
    {
        try
        {
            return await probe.RunAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Un probe che esplode non deve nascondere gli esiti degli altri.
            return [new ProbeResult(0, probe.GetType().Name, ProbeStatus.Error, exception.Message, TimeSpan.Zero)];
        }
    }
}

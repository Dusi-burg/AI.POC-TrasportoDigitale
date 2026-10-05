using Dusiburg.AI.Trasporto.Shared.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dusiburg.AI.Trasporto.Efti.Api.Health;

/// <summary>
/// Gli stessi controlli di <c>/api/health</c> come health check ASP.NET: il dashboard di Aspire mostra l'API in rosso
/// se SQL o Ollama non rispondono, o se la dimensione dell'embedding non torna.
/// </summary>
public sealed class TrasportoHealthCheck(HealthReportCache cache) : IHealthCheck
{
    public const string Name = "trasporto";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ProbeReport report = await cache.GetAsync(cancellationToken);
        Dictionary<string, object> data = report.Checks.ToDictionary(c => $"{c.Number:D2} {c.Name}", c => (object)$"{c.Status}: {c.Detail}");

        if (report.HasErrors)
        {
            return HealthCheckResult.Unhealthy(Describe(report, ProbeStatus.Error), data: data);
        }

        return report.HasWarnings
            ? HealthCheckResult.Degraded($"controlli superati con avvisi: {Describe(report, ProbeStatus.Warning)}", data: data)
            : HealthCheckResult.Healthy("tutti i controlli superati", data);
    }

    private static string Describe(ProbeReport report, ProbeStatus status) =>
        string.Join("; ", report.Checks.Where(c => c.Status == status).Select(c => $"{c.Name}: {c.Detail}"));
}

/// <summary>
/// Aspire interroga <c>/health</c> ogni pochi secondi: i controlli (che calcolano anche un embedding) si rieseguono al massimo
/// una volta ogni <see cref="Duration"/>.
/// </summary>
public sealed class HealthReportCache(HealthService healthService, TimeProvider timeProvider)
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (ProbeReport Report, DateTimeOffset At)? _last;

    public async Task<ProbeReport> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_last is { } last && timeProvider.GetUtcNow() - last.At < Duration)
            {
                return last.Report;
            }

            ProbeReport report = await healthService.RunAsync(cancellationToken);
            _last = (report, timeProvider.GetUtcNow());

            return report;
        }
        finally
        {
            _gate.Release();
        }
    }
}

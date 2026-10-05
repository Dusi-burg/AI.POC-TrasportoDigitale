using Dusiburg.AI.Trasporto.Shared.Health;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;

/// <summary>Probe con esiti fissi: sostituisce SQL e Ollama nei test degli endpoint.</summary>
internal sealed class StubHealthProbe(params ProbeResult[] results) : IHealthProbe
{
    public Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProbeResult>>(results);
}

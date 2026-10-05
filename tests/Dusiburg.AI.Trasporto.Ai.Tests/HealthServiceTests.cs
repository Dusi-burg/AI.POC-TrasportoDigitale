using Dusiburg.AI.Trasporto.Shared.Health;

namespace Dusiburg.AI.Trasporto.Ai.Tests;

/// <summary>Aggregazione dei probe, con probe finti.</summary>
public class HealthServiceTests
{
    [Test]
    public async Task RunAsync_OkAndWarning_NoErrorsAndOrderedByNumber()
    {
        //SETUP
        var service = new HealthService(
        [
            new FakeProbe(Result(5, ProbeStatus.Ok), Result(10, ProbeStatus.Info)),
            new FakeProbe(Result(1, ProbeStatus.Ok), Result(4, ProbeStatus.Warning))
        ]);

        //SUT
        ProbeReport report = await service.RunAsync(CancellationToken.None);

        Assert.That(report.Checks.Select(c => c.Number), Is.EqualTo(new[] { 1, 4, 5, 10 }));
        Assert.That(report.HasErrors, Is.False);
        Assert.That(report.HasWarnings, Is.True);
    }

    [Test]
    public async Task RunAsync_ThrowingProbe_BecomesErrorWithoutHidingTheOthers()
    {
        //SETUP
        var service = new HealthService(
        [
            new FakeProbe(Result(1, ProbeStatus.Ok)),
            new ThrowingProbe("Ollama esploso")
        ]);

        //SUT
        ProbeReport report = await service.RunAsync(CancellationToken.None);

        Assert.That(report.Checks, Has.Count.EqualTo(2));
        Assert.That(report.Checks, Has.Some.Matches<ProbeResult>(c => c.Status == ProbeStatus.Error && c.Detail == "Ollama esploso"));
        Assert.That(report.HasErrors, Is.True);
    }

    [Test]
    public async Task MeasureAsync_ThrowingStep_ErrorWithTheMessage()
    {
        //SUT
        ProbeResult ok = await ProbeResult.MeasureAsync(3, "passo", () => Task.FromResult((ProbeStatus.Ok, "bene")));
        ProbeResult failed = await ProbeResult.MeasureAsync(3, "passo", () => throw new InvalidOperationException("male"));

        Assert.That((ok.Status, ok.Detail), Is.EqualTo((ProbeStatus.Ok, "bene")));
        Assert.That((failed.Status, failed.Detail), Is.EqualTo((ProbeStatus.Error, "male")));
    }

    private static ProbeResult Result(int number, ProbeStatus status) => new(number, $"controllo {number}", status, "dettaglio", TimeSpan.Zero);

    private sealed class FakeProbe(params ProbeResult[] results) : IHealthProbe
    {
        public Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProbeResult>>(results);
    }

    private sealed class ThrowingProbe(string message) : IHealthProbe
    {
        public Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(message);
    }
}

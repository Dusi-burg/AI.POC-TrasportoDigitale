using Dusiburg.AI.Trasporto.Ai.Health;
using Dusiburg.AI.Trasporto.Shared.Health;
using Microsoft.Extensions.Configuration;

namespace Dusiburg.AI.Trasporto.Ai.Tests;

/// <summary>Controllo 9: presenza della chiave, pesata su quali compiti sono instradati sul cloud.</summary>
public class AnthropicKeyHealthProbeTests
{
    [Test]
    public async Task RunAsync_KeyConfigured_OkWithoutRevealingIt()
    {
        //SETUP
        var probe = new AnthropicKeyHealthProbe(Options(new() { ["ANTHROPIC_API_KEY"] = "sk-test-segreta" }));

        //SUT
        IReadOnlyList<ProbeResult> results = await probe.RunAsync(CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That((results[0].Number, results[0].Status), Is.EqualTo((9, ProbeStatus.Ok)));
        Assert.That(results[0].Detail, Does.Not.Contain("sk-test-segreta"));
    }

    [Test]
    public async Task RunAsync_KeyMissingWithDefaultRouting_Warning()
    {
        //SETUP
        var probe = new AnthropicKeyHealthProbe(Options([]));

        //SUT
        IReadOnlyList<ProbeResult> results = await probe.RunAsync(CancellationToken.None);

        Assert.That(results[0].Status, Is.EqualTo(ProbeStatus.Warning));
        Assert.That(results[0].Detail, Does.Contain("Draft").And.Contain("Judge"));
    }

    [Test]
    public async Task RunAsync_KeyMissingWithRelease1TaskOnCloud_Error()
    {
        //SETUP
        var probe = new AnthropicKeyHealthProbe(Options(new() { ["MODEL_EXTRACTION_PROVIDER"] = "anthropic" }));

        //SUT
        IReadOnlyList<ProbeResult> results = await probe.RunAsync(CancellationToken.None);

        Assert.That(results[0].Status, Is.EqualTo(ProbeStatus.Error));
        Assert.That(results[0].Detail, Does.Contain("Parameters:anthropic-api-key"));
    }

    private static AiOptions Options(Dictionary<string, string?> settings) =>
        AiOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
}

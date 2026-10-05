using Dusiburg.AI.Trasporto.Shared.Health;

namespace Dusiburg.AI.Trasporto.Ai.Health;

/// <summary>
/// Controllo 9 di <c>/api/health</c>: la chiave di Anthropic è configurata. Si guarda solo la presenza, senza chiamare l'API:
/// un controllo ripetuto dal dashboard non deve costare né dipendere dalla rete.
/// </summary>
public sealed class AnthropicKeyHealthProbe(AiOptions options) : IHealthProbe
{
    public const int Number = 9;

    public const string Name = "Chiave Anthropic";

    /// <summary>Compiti già in uso nella Release 1: senza chiave, se instradati sul cloud, falliscono alla prima chiamata.</summary>
    private static readonly ModelTask[] Release1Tasks = [ModelTask.Extraction, ModelTask.Mapping];

    public Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProbeResult> results = [new ProbeResult(Number, Name, Status(), Detail(), TimeSpan.Zero)];

        return Task.FromResult(results);
    }

    private ProbeStatus Status()
    {
        if (options.HasAnthropicApiKey)
        {
            return ProbeStatus.Ok;
        }

        return Release1Tasks.Any(RoutedToAnthropic) ? ProbeStatus.Error : ProbeStatus.Warning;
    }

    private string Detail()
    {
        string routed = string.Join(", ", Enum.GetValues<ModelTask>().Where(RoutedToAnthropic));
        string tasks = routed.Length == 0 ? "nessun compito sul cloud" : $"compiti sul cloud: {routed}";

        return options.HasAnthropicApiKey
            ? $"configurata (non verificata con una chiamata); {tasks}"
            : $"non configurata: impostare Parameters:anthropic-api-key negli user-secrets dell'AppHost; {tasks}";
    }

    private bool RoutedToAnthropic(ModelTask task) => options.ProviderFor(task) == ModelProviders.Anthropic;
}

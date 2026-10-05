using Dusiburg.AI.Trasporto.Shared.Health;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace Dusiburg.AI.Trasporto.Ai.Ollama;

/// <summary>
/// Controlli 5–8 e 10 di <c>/api/health</c>: Ollama raggiungibile, modelli presenti, dimensione dell'embedding uguale a
/// <c>EMBEDDING_DIMENSIONS</c>, posizionamento dei modelli (chat su GPU, embedding fuori dalla VRAM).
/// </summary>
public sealed class OllamaHealthProbe(AiOptions options, EmbeddingGeneratorFactory embeddingFactory) : IHealthProbe
{
    private const string ReachableName = "Ollama raggiungibile";
    private const string ChatName = "Modello chat presente";
    private const string EmbeddingName = "Modello embedding presente";
    private const string DimensionsName = "Dimensione embedding";
    private const string PlacementName = "Posizionamento modelli";

    public async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken)
    {
        using OllamaApiClient ollama = OllamaClients.Create(options.OllamaEndpoint, OllamaClients.ProbeTimeout);

        ProbeResult reachable = await ProbeResult.MeasureAsync(5, ReachableName, async () =>
        {
            string version = await ollama.GetVersionAsync(cancellationToken);

            return (ProbeStatus.Ok, $"{options.OllamaEndpoint} (versione {version})");
        });

        if (reachable.Status == ProbeStatus.Error)
        {
            const string reason = "Ollama non raggiungibile";

            return
            [
                reachable,
                ProbeResult.Skipped(6, ChatName, reason),
                ProbeResult.Skipped(7, EmbeddingName, reason),
                ProbeResult.Skipped(8, DimensionsName, reason),
                ProbeResult.Skipped(10, PlacementName, reason)
            ];
        }

        IReadOnlyList<Model> installed = [.. await ollama.ListLocalModelsAsync(cancellationToken)];
        ProbeResult embeddingInstalled = await ProbeResult.MeasureAsync(
            7, EmbeddingName, () => Task.FromResult(CheckInstalled(installed, options.EmbeddingModel)));

        return
        [
            reachable,
            await ProbeResult.MeasureAsync(6, ChatName, () => Task.FromResult(CheckInstalled(installed, options.OllamaChatModel))),
            embeddingInstalled,
            embeddingInstalled.Status == ProbeStatus.Error
                ? ProbeResult.Skipped(8, DimensionsName, "modello di embedding non disponibile")
                : await ProbeResult.MeasureAsync(8, DimensionsName, () => CheckDimensionsAsync(cancellationToken)),
            await ProbeResult.MeasureAsync(10, PlacementName, () => CheckPlacementAsync(ollama, cancellationToken))
        ];
    }

    private static (ProbeStatus, string) CheckInstalled(IReadOnlyList<Model> installed, string model) =>
        installed.FirstOrDefault(m => OllamaClients.SameModel(m.Name, model)) is { } found
            ? (ProbeStatus.Ok, $"{found.Name} ({found.Size / 1_000_000_000.0:0.0} GB)")
            : (ProbeStatus.Error, $"'{model}' non installato: eseguire ollama pull {model}");

    private async Task<(ProbeStatus, string)> CheckDimensionsAsync(CancellationToken cancellationToken)
    {
        using IEmbeddingGenerator<string, Embedding<float>> generator = embeddingFactory.Create();

        Embedding<float> embedding = await generator.GenerateAsync("prova", cancellationToken: cancellationToken);
        int measured = embedding.Vector.Length;

        return measured == options.EmbeddingDimensions
            ? (ProbeStatus.Ok, $"{measured} = {AiOptions.Keys.EmbeddingDimensions}")
            : (ProbeStatus.Error,
                $"il modello '{options.EmbeddingModel}' restituisce vettori da {measured} ma {AiOptions.Keys.EmbeddingDimensions}={options.EmbeddingDimensions}: " +
                "allineare la configurazione al modello");
    }

    private async Task<(ProbeStatus, string)> CheckPlacementAsync(OllamaApiClient ollama, CancellationToken cancellationToken)
    {
        IReadOnlyList<RunningModel> running = [.. await ollama.ListRunningModelsAsync(cancellationToken)];
        RunningModel? chat = running.FirstOrDefault(m => OllamaClients.SameModel(m.Name, options.OllamaChatModel));
        RunningModel? embedding = running.FirstOrDefault(m => OllamaClients.SameModel(m.Name, options.EmbeddingModel));

        string chatText = chat is null ? "chat non caricata" : $"chat {Placement(chat)}";
        string embeddingText = embedding is null ? "embedding non caricato" : $"embedding {Placement(embedding)}";

        // Un embedding in VRAM può costringere Ollama a scaricare la chat durante la demo.
        bool embeddingInVram = embedding is not null && embedding.SizeVram > 0;
        bool chatOffGpu = chat is not null && chat.SizeVram < chat.Size;

        IEnumerable<string> warnings = new[]
        {
            embeddingInVram ? "embedding in VRAM: può costringere Ollama a scaricare la chat" : null,
            chatOffGpu ? "chat non tutta in VRAM: risposte più lente, liberare VRAM o ridurre il contesto" : null
        }.OfType<string>();

        string detail = string.Join("; ", [chatText, embeddingText, .. warnings]);

        return (embeddingInVram || chatOffGpu ? ProbeStatus.Warning : ProbeStatus.Info, detail);
    }

    private static string Placement(RunningModel model)
    {
        if (model.Size <= 0 || model.SizeVram <= 0)
        {
            return "100% CPU";
        }

        long gpuPercent = model.SizeVram * 100 / model.Size;

        return gpuPercent >= 100 ? "100% GPU" : $"{gpuPercent}% GPU / {100 - gpuPercent}% CPU";
    }
}

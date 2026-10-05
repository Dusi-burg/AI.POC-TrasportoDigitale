using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace Dusiburg.AI.Trasporto.Ai.Ollama;

/// <summary>
/// Embedding via <c>/api/embed</c> con <c>num_gpu = 0</c>: il modello resta sulla CPU e non toglie VRAM alla chat (scelta di Sinistri).
/// Si usa <see cref="IOllamaApiClient.EmbedAsync"/> direttamente perché l'adapter di OllamaSharp per
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> non documenta come passare le opzioni del runtime.
/// </summary>
internal sealed class OllamaEmbeddingGenerator(IOllamaApiClient client, string model, int dimensions)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    /// <summary>Il modello resta caricato tra una richiesta e l'altra durante la demo.</summary>
    internal const string KeepAlive = "30m";

    private const int CpuOnly = 0;

    private readonly EmbeddingGeneratorMetadata _metadata = new("ollama", client.Uri, model, dimensions);

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = new EmbedRequest
        {
            Model = options?.ModelId ?? model,
            Input = [.. values],
            Options = new RequestOptions { NumGpu = CpuOnly },
            KeepAlive = KeepAlive
        };

        EmbedResponse response = await client.EmbedAsync(request, cancellationToken);
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;

        return [.. response.Embeddings.Select(vector => new Embedding<float>(vector) { ModelId = request.Model, CreatedAt = createdAt })];
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is not null
        ? null
        : serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
        : serviceType.IsInstanceOfType(this) ? this
        : serviceType.IsInstanceOfType(client) ? client
        : null;

    public void Dispose() => (client as IDisposable)?.Dispose();
}

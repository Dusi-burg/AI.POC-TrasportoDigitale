using Dusiburg.AI.Trasporto.Ai.Ollama;
using Dusiburg.AI.Trasporto.Shared.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Dusiburg.AI.Trasporto.Ai;

/// <summary>
/// Embedding sempre in locale su Ollama (PLAN §4): il resto del codice vede solo
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>.
/// </summary>
public sealed class EmbeddingGeneratorFactory(AiOptions options, ILoggerFactory loggerFactory)
{
    public IEmbeddingGenerator<string, Embedding<float>> Create()
    {
        IEmbeddingGenerator<string, Embedding<float>> inner = new OllamaEmbeddingGenerator(
            OllamaClients.Create(options.OllamaEndpoint, OllamaClients.GenerationTimeout),
            options.EmbeddingModel,
            options.EmbeddingDimensions);

        return inner
            .AsBuilder()
            .UseLogging(loggerFactory)
            .UseOpenTelemetry(loggerFactory, TrasportoTelemetry.Sources.Embedding)
            .Build();
    }
}

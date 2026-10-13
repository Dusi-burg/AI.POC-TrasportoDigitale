using Anthropic;
using Dusiburg.AI.Trasporto.Ai.Ollama;
using Dusiburg.AI.Trasporto.Shared.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;

namespace Dusiburg.AI.Trasporto.Ai;

/// <summary>Modello pronto per un compito: client con logging e telemetria, opzioni di default del provider.</summary>
public sealed record ChatModel(IChatClient ChatClient, ModelTask Task, string Provider, string ModelId, ChatOptions DefaultOptions) : IDisposable
{
    public void Dispose() => ChatClient.Dispose();
}

public interface IChatClientFactory
{
    ChatModel Create(ModelTask task);
}

/// <summary>
/// Unico punto che conosce i provider: il provider di ogni compito è una manopola (<c>MODEL_&lt;COMPITO&gt;_PROVIDER</c>) e il
/// resto del codice vede solo <see cref="IChatClient"/>.
/// </summary>
public sealed class ChatClientFactory(AiOptions options, ILoggerFactory loggerFactory) : IChatClientFactory
{
    internal const float LowTemperature = 0.1f;

    private const int MaxOutputTokens = 8_192;

    public ChatModel Create(ModelTask task)
    {
        string provider = options.ProviderFor(task);

        (IChatClient inner, string modelId, ChatOptions defaults) = provider == ModelProviders.Anthropic
            ? CreateAnthropic(task)
            : CreateOllama();

        ChatClientBuilder builder = inner.AsBuilder();

        // Verso il cloud email e telefoni partono mascherati; in locale i dati non lasciano la macchina. È il primo anello:
        // log, telemetria e cattura dei prompt vedono ciò che parte davvero.
        if (provider == ModelProviders.Anthropic)
        {
            builder.Use(client => new PiiMaskingChatClient(client));
        }

        builder
            .UseLogging(loggerFactory)
            .UseOpenTelemetry(loggerFactory, TrasportoTelemetry.Sources.Chat);

        // Diagnostica opzionale: ogni richiesta al modello salvata su file, per vedere il prompt reale.
        if (options.PromptCaptureDirectory is { } captureDirectory)
        {
            builder.Use(client => new PromptCaptureChatClient(client, captureDirectory));
        }

        return new ChatModel(builder.Build(), task, provider, modelId, defaults);
    }

    /// <summary>
    /// Solo Haiku 4.5 accetta una temperatura diversa dal default: Sonnet 5.5 e Opus 5.5 la rifiutano (fase-0.md §7).
    /// Un modello non riconosciuto si tratta come i più recenti: nessun parametro di campionamento.
    /// </summary>
    internal static bool AcceptsTemperature(string anthropicModel) =>
        anthropicModel.StartsWith("claude-haiku-4-5", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Haiku 4.5 non supporta l'adaptive thinking: con la modalità Extended e senza <c>ChatOptions.Reasoning</c> il client
    /// non invia alcuna configurazione di thinking (come in O2C).
    /// </summary>
    internal static AnthropicThinkingMode ThinkingModeFor(string anthropicModel) =>
        AcceptsTemperature(anthropicModel) ? AnthropicThinkingMode.Extended : AnthropicThinkingMode.Adaptive;

    private (IChatClient Client, string ModelId, ChatOptions Defaults) CreateAnthropic(ModelTask task)
    {
        if (!options.HasAnthropicApiKey)
        {
            throw new InvalidOperationException(
                $"{AiOptions.Keys.AnthropicApiKey} non configurata ma {AiOptions.Keys.ProviderFor(task)}={ModelProviders.Anthropic}: " +
                "impostare Parameters:anthropic-api-key negli user-secrets dell'AppHost.");
        }

        string model = options.AnthropicModelFor(task);
        IChatClient client = new AnthropicClient { ApiKey = options.AnthropicApiKey }
            .AsIChatClient(model, MaxOutputTokens, ThinkingModeFor(model));

        var defaults = new ChatOptions();

        if (AcceptsTemperature(model))
        {
            defaults.Temperature = LowTemperature;
        }

        return (client, model, defaults);
    }

    private (IChatClient Client, string ModelId, ChatOptions Defaults) CreateOllama()
    {
        // OllamaApiClient implementa sia IChatClient sia IEmbeddingGenerator: il tipo esplicito scioglie l'ambiguità di AsBuilder.
        IChatClient client = OllamaClients.Create(options.OllamaEndpoint, OllamaClients.GenerationTimeout, options.OllamaChatModel);

        var defaults = new ChatOptions { Temperature = LowTemperature, MaxOutputTokens = MaxOutputTokens };

        // Qwen 3.5 con il thinking attivo consuma token e a volte lascia la chiamata al tool dentro il ragionamento.
        defaults.AddOllamaOption(OllamaOption.Think, false);
        defaults.AddOllamaOption(OllamaOption.NumCtx, options.OllamaContextLength);

        return (client, options.OllamaChatModel, defaults);
    }
}

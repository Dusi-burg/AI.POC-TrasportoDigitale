using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Dusiburg.AI.Trasporto.Ai;

/// <summary>
/// Manopole dei modelli (PLAN §8), come <c>ModelOptions</c> di O2C e <c>SinistriOptions</c> di Sinistri: si impostano sull'AppHost
/// (user-secrets, riga di comando, variabili d'ambiente) e arrivano ai servizi; senza valore vale il default del codice.
/// </summary>
public sealed record AiOptions(
    Uri OllamaEndpoint,
    string OllamaChatModel,
    int OllamaContextLength,
    string? AnthropicApiKey,
    string AnthropicModel,
    string AnthropicJudgeModel,
    IReadOnlyDictionary<ModelTask, string> TaskProviders,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string? PromptCaptureDirectory)
{
    /// <summary>127.0.0.1 e non localhost: su Windows ogni nuova connessione a localhost prova prima IPv6 e perde ~2 s (misura di Sinistri).</summary>
    public const string DefaultOllamaEndpoint = "http://127.0.0.1:11434";

    public const string DefaultOllamaChatModel = "qwen3.5:9b";

    /// <summary>Con 8 GB di VRAM Ollama sceglierebbe 4096 token, pochi per istruzioni, schema di uscita e testo di un CMR.</summary>
    public const int DefaultOllamaContextLength = 8_192;

    public const string DefaultAnthropicModel = "claude-haiku-4-5";

    public const string DefaultAnthropicJudgeModel = "claude-sonnet-5-5";

    public const string DefaultEmbeddingModel = "embeddinggemma";

    public const int DefaultEmbeddingDimensions = 768;

    /// <summary>Limite del tipo <c>VECTOR</c> di SQL Server 2025 in <c>float32</c>.</summary>
    public const int MaxEmbeddingDimensions = 1998;

    public static class Keys
    {
        public const string OllamaEndpoint = "OLLAMA_ENDPOINT";
        public const string OllamaChatModel = "OLLAMA_CHAT_MODEL";
        public const string OllamaNumCtx = "OLLAMA_NUM_CTX";
        public const string AnthropicApiKey = "ANTHROPIC_API_KEY";
        public const string AnthropicModel = "ANTHROPIC_MODEL";
        public const string AnthropicJudgeModel = "ANTHROPIC_JUDGE_MODEL";
        public const string EmbeddingModel = "EMBEDDING_MODEL";
        public const string EmbeddingDimensions = "EMBEDDING_DIMENSIONS";
        public const string PromptCaptureDirectory = "TRASPORTO_PROMPT_CAPTURE_DIR";

        /// <summary><c>MODEL_EXTRACTION_PROVIDER</c>, <c>MODEL_MAPPING_PROVIDER</c>, <c>MODEL_DRAFT_PROVIDER</c>, <c>MODEL_JUDGE_PROVIDER</c>.</summary>
        public static string ProviderFor(ModelTask task) => $"MODEL_{task.ToString().ToUpperInvariant()}_PROVIDER";
    }

    /// <summary>Estrazione e mappatura in locale, bozza e giudice sul cloud (PLAN §4).</summary>
    public static string DefaultProviderFor(ModelTask task) =>
        task is ModelTask.Extraction or ModelTask.Mapping ? ModelProviders.Ollama : ModelProviders.Anthropic;

    public bool HasAnthropicApiKey => !string.IsNullOrWhiteSpace(AnthropicApiKey);

    public string ProviderFor(ModelTask task) => TaskProviders[task];

    /// <summary>Il giudice usa un modello più capace di quello che giudica (D3).</summary>
    public string AnthropicModelFor(ModelTask task) => task == ModelTask.Judge ? AnthropicJudgeModel : AnthropicModel;

    public static AiOptions FromConfiguration(IConfiguration configuration) => new(
        ReadUri(configuration, Keys.OllamaEndpoint) ?? new Uri(DefaultOllamaEndpoint),
        NonEmpty(configuration[Keys.OllamaChatModel]) ?? DefaultOllamaChatModel,
        ReadInt(configuration, Keys.OllamaNumCtx, DefaultOllamaContextLength, minimum: 1_024, maximum: 262_144),
        NonEmpty(configuration[Keys.AnthropicApiKey]),
        NonEmpty(configuration[Keys.AnthropicModel]) ?? DefaultAnthropicModel,
        NonEmpty(configuration[Keys.AnthropicJudgeModel]) ?? DefaultAnthropicJudgeModel,
        Enum.GetValues<ModelTask>().ToDictionary(task => task, task => ReadProvider(configuration, task)),
        NonEmpty(configuration[Keys.EmbeddingModel]) ?? DefaultEmbeddingModel,
        ReadInt(configuration, Keys.EmbeddingDimensions, DefaultEmbeddingDimensions, minimum: 1, maximum: MaxEmbeddingDimensions),
        NonEmpty(configuration[Keys.PromptCaptureDirectory]));

    private static string ReadProvider(IConfiguration configuration, ModelTask task)
    {
        string key = Keys.ProviderFor(task);
        string provider = (NonEmpty(configuration[key]) ?? DefaultProviderFor(task)).ToLowerInvariant();

        return ModelProviders.All.Contains(provider)
            ? provider
            : throw Invalid($"{key} '{provider}' non supportato: usare {string.Join(" oppure ", ModelProviders.All)}.");
    }

    private static Uri? ReadUri(IConfiguration configuration, string key)
    {
        string? value = NonEmpty(configuration[key]);

        if (value is null)
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw Invalid($"{key} '{value}' non è un indirizzo http(s) valido.");
    }

    private static int ReadInt(IConfiguration configuration, string key, int defaultValue, int minimum, int maximum)
    {
        string? value = NonEmpty(configuration[key]);

        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= minimum && number <= maximum
            ? number
            : throw Invalid($"{key} '{value}' non valido: serve un intero tra {minimum} e {maximum}.");
    }

    private static InvalidOperationException Invalid(string message) => new($"Configurazione non valida: {message}");

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

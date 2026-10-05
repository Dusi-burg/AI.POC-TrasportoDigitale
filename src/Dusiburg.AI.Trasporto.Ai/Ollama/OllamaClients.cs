using OllamaSharp;

namespace Dusiburg.AI.Trasporto.Ai.Ollama;

internal static class OllamaClients
{
    /// <summary>Generazione locale: una risposta può richiedere minuti.</summary>
    public static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Controlli di salute: include il caricamento a freddo del modello di embedding (~6 s su CPU).</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// HttpClient dedicato, senza la resilienza di ServiceDefaults: i suoi timeout (10 s per tentativo) non reggono la
    /// generazione locale (stessa lezione di O2C).
    /// </summary>
    public static OllamaApiClient Create(Uri endpoint, TimeSpan timeout, string? model = null)
    {
        var http = new HttpClient { BaseAddress = endpoint, Timeout = timeout };

        return model is null ? new OllamaApiClient(http) : new OllamaApiClient(http, model);
    }

    /// <summary>Ollama elenca i modelli con il tag esplicito: <c>embeddinggemma</c> è <c>embeddinggemma:latest</c>.</summary>
    public static bool SameModel(string installed, string configured) =>
        string.Equals(WithTag(installed), WithTag(configured), StringComparison.OrdinalIgnoreCase);

    private static string WithTag(string model) => model.Contains(':', StringComparison.Ordinal) ? model : model + ":latest";
}

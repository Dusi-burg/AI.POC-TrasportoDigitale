namespace Dusiburg.AI.Trasporto.Ai;

/// <summary>Compiti per cui si sceglie un modello (PLAN §4, "Routing dei modelli per compito").</summary>
public enum ModelTask
{
    /// <summary>Estrazione dei campi dal CMR.</summary>
    Extraction,

    /// <summary>Proposta di completamento dei campi del dataset.</summary>
    Mapping,

    /// <summary>Bozza di risposta al reclamo (Release 2).</summary>
    Draft,

    /// <summary>LLM-as-judge (Release 2).</summary>
    Judge,

    /// <summary>Risposta dell'agente autorità a una domanda di controllo, dai soli sottoinsiemi.</summary>
    Authority
}

public static class ModelProviders
{
    public const string Ollama = "ollama";

    public const string Anthropic = "anthropic";

    public static readonly IReadOnlyList<string> All = [Ollama, Anthropic];
}

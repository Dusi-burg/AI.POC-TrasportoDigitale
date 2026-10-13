using System.Globalization;

namespace Dusiburg.AI.Trasporto.Eval;

/// <summary>Riga di comando: le parole prima della prima opzione sono il comando, poi <c>--nome valore</c> o <c>--nome</c>.</summary>
public sealed class Cli
{
    private readonly Dictionary<string, string?> _options;

    private Cli(string command, Dictionary<string, string?> options)
    {
        Command = command;
        _options = options;
    }

    /// <summary>Per esempio <c>eval extraction</c>.</summary>
    public string Command { get; }

    public static Cli Parse(string[] args)
    {
        int firstOption = Array.FindIndex(args, a => a.StartsWith("--", StringComparison.Ordinal));
        string[] words = firstOption < 0 ? args : args[..firstOption];
        Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);

        for (int index = Math.Max(firstOption, 0); firstOption >= 0 && index < args.Length; index++)
        {
            string name = args[index][2..];
            bool hasValue = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal);

            options[name] = hasValue ? args[++index] : null;
        }

        return new Cli(string.Join(' ', words), options);
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.GetValueOrDefault(name);

    public int? Int(string name) =>
        Value(name) is { } text
            ? int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0
                ? number
                : throw new ArgumentException($"--{name} '{text}' non valido: serve un intero positivo.")
            : null;

    public decimal? Decimal(string name) =>
        Value(name) is { } text
            ? decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
                ? number
                : throw new ArgumentException($"--{name} '{text}' non valido: serve un numero con il punto decimale.")
            : null;

    /// <summary>Il modello indicato con <c>--provider</c> e <c>--model</c>, oppure con <c>--model provider:modello</c>.</summary>
    public ModelCandidate? Model(ModelCandidate fallback)
    {
        string? provider = Value("provider");
        string? model = Value("model");

        if (provider is null && model is null)
        {
            return null;
        }

        if (provider is null && model!.Contains(':', StringComparison.Ordinal) && Ai.ModelProviders.All.Contains(model[..model.IndexOf(':', StringComparison.Ordinal)]))
        {
            return ModelCandidate.Parse(model);
        }

        provider ??= fallback.Provider;

        return ModelCandidate.Parse($"{provider}:{model ?? (provider == Ai.ModelProviders.Ollama ? Ai.AiOptions.DefaultOllamaChatModel : Ai.AiOptions.DefaultAnthropicModel)}");
    }

    /// <summary>I modelli di <c>--models a,b,c</c>; senza opzione, quelli indicati.</summary>
    public IReadOnlyList<ModelCandidate> Models(IReadOnlyList<ModelCandidate> defaults) =>
        Value("models") is { } list
            ? [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ModelCandidate.Parse)]
            : defaults;
}

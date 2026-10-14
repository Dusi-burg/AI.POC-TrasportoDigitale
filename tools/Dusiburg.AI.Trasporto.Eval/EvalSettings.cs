using System.Globalization;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dusiburg.AI.Trasporto.Eval;

/// <summary>Un modello da misurare: <c>ollama:qwen3.5:9b</c>, <c>anthropic:claude-haiku-5-5</c>.</summary>
public sealed record ModelCandidate(string Provider, string Model)
{
    /// <summary>I tre modelli del confronto della Fase 9 (PLAN §4).</summary>
    public static readonly IReadOnlyList<ModelCandidate> Defaults =
    [
        new(ModelProviders.Ollama, AiOptions.DefaultOllamaChatModel),
        new(ModelProviders.Anthropic, AiOptions.DefaultAnthropicModel),
        new(ModelProviders.Anthropic, AiOptions.DefaultAnthropicJudgeModel)
    ];

    public bool IsLocal => Provider == ModelProviders.Ollama;

    /// <summary>Nome usabile in un file: <c>qwen3.5-9b</c>.</summary>
    public string FileName => string.Concat(Model.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-'));

    public override string ToString() => $"{Provider}:{Model}";

    public static ModelCandidate Parse(string text)
    {
        int separator = text.IndexOf(':', StringComparison.Ordinal);
        string provider = separator > 0 ? text[..separator].ToLowerInvariant() : string.Empty;

        return ModelProviders.All.Contains(provider) && separator < text.Length - 1
            ? new ModelCandidate(provider, text[(separator + 1)..])
            : throw new ArgumentException($"Modello '{text}' non valido: scrivere <provider>:<modello>, con provider {string.Join(" oppure ", ModelProviders.All)}.");
    }
}

/// <summary>Prezzo di listino di un modello, in dollari per milione di token.</summary>
public sealed record ModelPrice(decimal InputPerMillion, decimal OutputPerMillion);

/// <summary>
/// Prezzi di listino dei modelli cloud usati nel confronto. Sono un dato che cambia: i default valgono alla data indicata e si
/// sovrascrivono con <c>Eval:Prices:&lt;modello&gt;:Input</c> e <c>…:Output</c>. Un modello locale non ha prezzo.
/// </summary>
public sealed class ModelPrices(IReadOnlyDictionary<string, ModelPrice> prices)
{
    /// <summary>Data a cui valgono i prezzi di default (listino dell'API Anthropic).</summary>
    public const string DefaultsAsOf = "2026-10-14";

    /// <remarks>
    /// Haiku 5.5 ha due listini secondo la lunghezza del prompt: 0,10 $ / 0,50 $ fino a 100K token, 0,50 $ / 2,50 $ oltre. I prompt
    /// del POC restano sotto i 100K, quindi vale il primo. Haiku 4.5 resta per ricalcolare i report misurati con quel modello.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, ModelPrice> Defaults = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-5-5"] = new(0.10m, 0.50m),
        ["claude-haiku-4-5"] = new(1.00m, 5.00m),
        ["claude-sonnet-5-5"] = new(2.00m, 10.00m)
    };

    public ModelPrice? For(ModelCandidate model) => model.IsLocal ? null : prices.GetValueOrDefault(model.Model);

    /// <summary>Costo in dollari; 0 per un modello locale, <c>null</c> se mancano i token o il prezzo.</summary>
    public decimal? Cost(ModelCandidate model, long? inputTokens, long? outputTokens)
    {
        if (model.IsLocal)
        {
            return 0m;
        }

        return For(model) is { } price && inputTokens is { } input && outputTokens is { } output
            ? ((input * price.InputPerMillion) + (output * price.OutputPerMillion)) / 1_000_000m
            : null;
    }

    public static ModelPrices FromConfiguration(IConfiguration configuration)
    {
        Dictionary<string, ModelPrice> prices = new(Defaults, StringComparer.OrdinalIgnoreCase);

        foreach (IConfigurationSection model in configuration.GetSection("Eval:Prices").GetChildren())
        {
            if (decimal.TryParse(model["Input"], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal input)
                && decimal.TryParse(model["Output"], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal output))
            {
                prices[model.Key] = new ModelPrice(input, output);
            }
        }

        return new ModelPrices(prices);
    }
}

/// <summary>
/// Configurazione del tool: variabili d'ambiente e, sotto, gli user-secrets dell'AppHost (chiave Anthropic e chiavi di ruolo),
/// così la valutazione usa le stesse chiavi della demo senza copiarle. Nessun segreto viene stampato.
/// </summary>
public sealed class EvalSettings(IConfiguration configuration, string repositoryRoot)
{
    /// <summary><c>UserSecretsId</c> del progetto AppHost.</summary>
    public const string AppHostUserSecretsId = "58c46292-d7e6-4c1a-bec4-16d48b31292f";

    public IConfiguration Configuration { get; } = configuration;

    public string RepositoryRoot { get; } = repositoryRoot;

    public string DataDirectory => Path.Combine(RepositoryRoot, "data");

    public ModelPrices Prices { get; } = ModelPrices.FromConfiguration(configuration);

    /// <summary>Indirizzi dello stack avviato dall'AppHost: porte fisse dei launchSettings (P8).</summary>
    public Uri EftiApi => new(Configuration["Eval:EftiApiUrl"] ?? "http://localhost:5302");

    public Uri EftiMcp => new(Configuration["Eval:EftiMcpUrl"] ?? "http://localhost:5303/mcp");

    public static EvalSettings Load(string repositoryRoot)
    {
        IConfigurationRoot secrets = new ConfigurationBuilder().AddUserSecrets(AppHostUserSecretsId).Build();

        // Negli user-secrets dell'AppHost le chiavi sono parametri di Aspire: qui prendono il nome con cui arrivano ai servizi.
        Dictionary<string, string?> fromAppHost = new()
        {
            [AiOptions.Keys.AnthropicApiKey] = secrets["Parameters:anthropic-api-key"]
        };

        foreach (Role role in Enum.GetValues<Role>())
        {
            fromAppHost[$"ApiKeys:{role}"] = secrets[$"Parameters:api-key-{role.ToString().ToLowerInvariant()}"];
        }

        foreach (string knob in new[] { AiOptions.Keys.OllamaEndpoint, AiOptions.Keys.OllamaChatModel, AiOptions.Keys.OllamaNumCtx, AiOptions.Keys.OllamaNumGpu, AiOptions.Keys.AnthropicModel })
        {
            fromAppHost[knob] = secrets[knob];
        }

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(fromAppHost.Where(entry => !string.IsNullOrWhiteSpace(entry.Value)))
            .AddEnvironmentVariables()
            .Build();

        return new EvalSettings(configuration, repositoryRoot);
    }

    /// <summary>La chiave di un ruolo, come la conosce lo stack avviato.</summary>
    public string ApiKey(Role role) =>
        Configuration[$"ApiKeys:{role}"] is { Length: > 0 } key
            ? key
            : throw new InvalidOperationException($"Chiave del ruolo {role} non configurata: Parameters:api-key-{role.ToString().ToLowerInvariant()} negli user-secrets dell'AppHost.");

    /// <summary>Opzioni dei modelli con ogni compito instradato sul modello da misurare.</summary>
    public AiOptions AiFor(ModelCandidate model)
    {
        AiOptions options = AiOptions.FromConfiguration(Configuration);

        return options with
        {
            TaskProviders = Enum.GetValues<ModelTask>().ToDictionary(task => task, _ => model.Provider),
            OllamaChatModel = model.IsLocal ? model.Model : options.OllamaChatModel,
            AnthropicModel = model.IsLocal ? options.AnthropicModel : model.Model,
            AnthropicJudgeModel = model.IsLocal ? options.AnthropicJudgeModel : model.Model
        };
    }

    public IChatClientFactory ModelsFor(ModelCandidate model) => new ChatClientFactory(AiFor(model), NullLoggerFactory.Instance);

    /// <summary>Il modello che la demo userebbe per l'estrazione, salvo indicazione diversa.</summary>
    public ModelCandidate DefaultModel
    {
        get
        {
            AiOptions options = AiOptions.FromConfiguration(Configuration);
            string provider = options.ProviderFor(ModelTask.Extraction);

            return new ModelCandidate(provider, provider == ModelProviders.Ollama ? options.OllamaChatModel : options.AnthropicModel);
        }
    }
}

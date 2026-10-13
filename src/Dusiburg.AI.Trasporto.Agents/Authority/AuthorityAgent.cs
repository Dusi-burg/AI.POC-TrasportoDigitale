using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Grounding;
using Dusiburg.AI.Trasporto.Agents.Specs;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tools;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Formatting;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Agents.Authority;

/// <summary>Esito dichiarato dal modello a fine run (output strutturato).</summary>
public sealed record AuthorityOutcome(string? Answer, bool DataAvailable, IReadOnlyList<string>? QuotedValues);

/// <summary>
/// Agente autorità: da una targa e una domanda di controllo, risponde usando solo ciò che un'autorità può leggere — il registro
/// degli identificatori e i sottoinsiemi. La targa la fissa il codice, un UIL si legge solo se è uscito dalla ricerca di questo
/// run, e i valori che la risposta cita si confrontano con i dati letti.
/// </summary>
public sealed class AuthorityAgent(IChatClientFactory models, IAgentToolCatalog catalog, ILogger<AuthorityAgent> logger)
{
    public const string AgentName = "AuthorityAgent";

    public const string TaskSection = "Task";

    public const string OutcomeRequestSection = "Outcome request";

    public const string OutcomeRetrySection = "Outcome retry";

    public const string MissingSubsetsSection = "Missing subsets";

    /// <summary>Per quante spedizioni della stessa targa il codice completa le letture: oltre, resta ciò che ha letto il modello.</summary>
    public const int MaxConsignmentsCompleted = 3;

    /// <summary>I sottoinsiemi che un'autorità può leggere: una risposta si dà dopo averli letti tutti.</summary>
    public static IReadOnlyList<string> Subsets { get; } = ["EU03", "EU05a"];

    /// <summary>Un tentativo più una richiesta di riformulare l'esito, senza rifare i tool.</summary>
    public const int MaxOutcomeAttempts = 2;

    private static readonly AgentSpec Spec = AgentSpec.Load(AgentName);

    public static IReadOnlySet<string> AllowedTools { get; } = new[]
    {
        EftiToolNames.FindConsignmentsByVehicle,
        EftiToolNames.GetAuthoritySubset
    }.ToFrozenSet();

    /// <param name="at">Momento del controllo, se simulato (demo); <c>null</c> = adesso. Lo fissa chi avvia l'agente, non il modello.</param>
    public async Task<AuthorityAnswerResult> AskAsync(
        string plate, DateTimeOffset? at, string question, string apiKey, IProgress<AgentStep>? progress, CancellationToken cancellationToken)
    {
        var run = new AgentRun(AgentName, AllowedTools, progress);
        var facts = new AuthorityFacts(plate, at);

        await using IAgentToolSession session = await catalog.OpenAsync(apiKey, cancellationToken);
        List<AITool> tools = session.Guarded(run, Rules(facts));

        using ChatModel chatModel = models.Create(ModelTask.Authority);
        var model = new AgentModel(chatModel, run);
        ChatClientAgent agent = model.CreateAgent(Spec.Instructions, tools);
        AgentSession conversation = await agent.CreateSessionAsync(cancellationToken);

        run.Report(new AgentStep(AgentStepKinds.Model, null, null, $"{chatModel.Provider} · {chatModel.ModelId}", null));

        string task = Spec.Section(TaskSection)
            .Replace("{plate}", plate, StringComparison.Ordinal)
            .Replace("{question}", question, StringComparison.Ordinal);

        // Fase di lavoro senza schema di output: con Ollama lo schema vincola la generazione al JSON e il modello non chiama i tool.
        AgentResponse work = await agent.RunAsync(task, conversation, cancellationToken: cancellationToken);

        // Il modello sceglie quale sottoinsieme leggere, e a volte si ferma al primo: dichiarerebbe "non disponibile" un dato che
        // sta nell'altro. Quelli che non ha letto li legge il codice, con gli stessi tool, e la domanda si ripropone.
        if (!model.Refused(work) && await ReadMissingSubsetsAsync(tools, facts, run, cancellationToken) is { } supplement)
        {
            work = await agent.RunAsync(supplement, conversation, cancellationToken: cancellationToken);
        }

        AuthorityOutcome? outcome = model.Refused(work) ? null : await AskOutcomeAsync(agent, conversation, model, cancellationToken);

        return Reconcile(plate, question, work, outcome, facts, run, model) with { ControlMoment = at };
    }

    /// <summary>
    /// Legge i sottoinsiemi che il modello ha saltato, per ogni spedizione trovata, e prepara il messaggio che glieli consegna.
    /// Le letture passano dalla stessa guardia dei tool: stesso ruolo, stesso audit, stessi passi nel run. <c>null</c> se non
    /// mancava nulla.
    /// </summary>
    private static async Task<string?> ReadMissingSubsetsAsync(List<AITool> tools, AuthorityFacts facts, AgentRun run, CancellationToken cancellationToken)
    {
        AIFunction? reader = tools.OfType<AIFunction>().FirstOrDefault(tool => tool.Name == EftiToolNames.GetAuthoritySubset);
        var data = new List<string>();

        if (reader is null)
        {
            return null;
        }

        foreach (string uil in facts.Uils.Take(MaxConsignmentsCompleted))
        {
            foreach (string subsetId in Subsets.Where(subsetId => !facts.SubsetsRead.Contains(new SubsetReading(uil, subsetId))))
            {
                object? value = await reader.InvokeAsync(new AIFunctionArguments { ["uil"] = uil, ["subsetId"] = subsetId }, cancellationToken);
                ToolResult result = ToolResultReader.Read(value);

                if (result.Succeeded)
                {
                    data.Add($"Subset {subsetId} of UIL {uil}:\n{result.Data.GetRawText()}");
                }
                else
                {
                    run.Warn($"Il sottoinsieme {subsetId} non è stato letto ({result.ErrorCode}): un dato dichiarato non disponibile potrebbe trovarsi lì.");
                }
            }
        }

        return data.Count == 0 ? null : Spec.Section(MissingSubsetsSection).Replace("{data}", string.Join("\n\n", data), StringComparison.Ordinal);
    }

    private async Task<AuthorityOutcome?> AskOutcomeAsync(
        ChatClientAgent agent, AgentSession conversation, AgentModel model, CancellationToken cancellationToken)
    {
        string message = Spec.Section(OutcomeRequestSection);

        for (int attempt = 1; attempt <= MaxOutcomeAttempts; attempt++)
        {
            try
            {
                AgentResponse<AuthorityOutcome> response = await agent.RunAsync<AuthorityOutcome>(
                    message, conversation, serializerOptions: TrasportoJson.Options, cancellationToken: cancellationToken);

                if (model.Refused(response))
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(response.Result?.Answer))
                {
                    return response.Result;
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Esito del modello non valido per {Agent} (tentativo {Attempt})", AgentName, attempt);
            }

            message = Spec.Section(OutcomeRetrySection);
        }

        return null;
    }

    /// <summary>
    /// La risposta è del modello, il resto è dei tool. Senza un esito valido vale il testo della fase di lavoro, dichiarato
    /// come non verificabile; i valori citati che non compaiono nei dati letti sono segnalati.
    /// </summary>
    private static AuthorityAnswerResult Reconcile(
        string plate, string question, AgentResponse work, AuthorityOutcome? outcome, AuthorityFacts facts, AgentRun run, AgentModel model)
    {
        string[] quoted = [.. (outcome?.QuotedValues ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct()];
        string[] unsupported = [.. quoted.Where(value => !SourceGrounding.IsQuotedFrom(value, facts.ReadData))];
        // Anche i soli identificatori del registro bastano a rispondere (es. "merci pericolose a bordo?"): conta aver trovato qualcosa.
        bool dataAvailable = outcome?.DataAvailable == true && facts.Uils.Count > 0 && unsupported.Length == 0;

        if (outcome is null)
        {
            run.Warn("Il modello non ha restituito un esito strutturato valido: la risposta è il suo testo libero, senza verifica dei valori citati.");
        }

        if (unsupported.Length > 0)
        {
            run.Warn("La risposta cita valori che non compaiono nei dati letti: va presa con riserva.");
        }

        if (outcome?.DataAvailable == true && facts.Uils.Count == 0)
        {
            run.Warn("Il modello dichiara di aver trovato il dato, ma la ricerca per targa non ha restituito nessuna spedizione.");
        }

        return new AuthorityAnswerResult(
            plate, question, outcome?.Answer?.Trim() ?? AgentModel.FinalText(work) ?? string.Empty, dataAvailable, quoted, unsupported,
            facts.Uils, facts.SubsetsRead, run.ToolCalls, model.Info(), run.Warnings);
    }

    private static Dictionary<string, ToolRule> Rules(AuthorityFacts facts) => new()
    {
        [EftiToolNames.FindConsignmentsByVehicle] = new ToolRule
        {
            HiddenArguments = ["plate", "at"],
            Before = arguments => facts.InjectCheck(arguments),
            OnSuccess = (_, data) => facts.RecordConsignments(data),
            Present = ReadableValues.Humanize
        },
        [EftiToolNames.GetAuthoritySubset] = new ToolRule
        {
            Before = arguments => facts.CheckUil(arguments),
            OnSuccess = (arguments, data) => facts.RecordSubset(arguments, data),
            // La risposta è per una persona: pesi, volumi e date arrivano al modello già scritti all'italiana ("5.300 kg"),
            // così non deve convertire "5300.000 KGM" — né può copiarlo in una frase, dove si leggerebbe cinque milioni.
            Present = ReadableValues.Humanize
        }
    };

    /// <summary>I fatti del run: gli UIL usciti dalla ricerca e i dati che l'agente ha davvero letto.</summary>
    private sealed class AuthorityFacts(string plate, DateTimeOffset? at)
    {
        private readonly List<string> _uils = [];
        private readonly List<SubsetReading> _subsetsRead = [];
        private readonly List<JsonElement> _readData = [];

        public IReadOnlyList<string> Uils => _uils;

        public IReadOnlyList<SubsetReading> SubsetsRead => _subsetsRead;

        /// <summary>Tutto ciò che i tool hanno restituito: è la sola fonte ammessa per i valori citati nella risposta.</summary>
        public IReadOnlyList<JsonElement> ReadData => _readData;

        /// <summary>
        /// Targa e momento del controllo sono quelli della richiesta, qualunque cosa il modello abbia scritto: l'agente guarda
        /// lo stesso carico che vede chi l'ha avviato.
        /// </summary>
        public ToolRejection? InjectCheck(AIFunctionArguments arguments)
        {
            arguments["plate"] = plate;

            if (at is { } moment)
            {
                arguments["at"] = moment.ToString("O", CultureInfo.InvariantCulture);
            }
            else
            {
                arguments.Remove("at");
            }

            return null;
        }

        public ToolRejection? CheckUil(AIFunctionArguments arguments)
        {
            string? uil = GuardedTool.ArgumentText(arguments, "uil")?.Trim();

            return uil is not null && _uils.Contains(uil)
                ? null
                : new ToolRejection(ErrorCodes.Unauthorized, "This UIL was not returned by find_consignments_by_vehicle for the plate of this check: it cannot be read.");
        }

        public void RecordConsignments(JsonElement data)
        {
            _readData.Add(data.Clone());
            _readData.Add(ReadableValues.Humanize(data));

            if (!data.TryGetProperty("consignments", out JsonElement consignments) || consignments.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement consignment in consignments.EnumerateArray())
            {
                if (consignment.TryGetProperty("uil", out JsonElement uil) && uil.GetString() is { } value && !_uils.Contains(value))
                {
                    _uils.Add(value);
                }
            }
        }

        public void RecordSubset(AIFunctionArguments arguments, JsonElement data)
        {
            // Fonte dei valori citati: i dati del tool e la forma in cui il modello li ha letti (ReadableValues.Humanize).
            _readData.Add(data.Clone());
            _readData.Add(ReadableValues.Humanize(data));

            var reading = new SubsetReading(
                GuardedTool.ArgumentText(arguments, "uil")?.Trim() ?? string.Empty, GuardedTool.ArgumentText(arguments, "subsetId")?.Trim() ?? string.Empty);

            if (!_subsetsRead.Contains(reading))
            {
                _subsetsRead.Add(reading);
            }
        }
    }
}

using System.Diagnostics;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Agents.Mapping;
using Dusiburg.AI.Trasporto.Eval.Live;
using Dusiburg.AI.Trasporto.Eval.Reports;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Eval.Bench;

/// <summary>Una spedizione da completare, con le proposte attese e quelle legittime ma non richieste.</summary>
public sealed record MappingCase(
    string ShipmentCode, string Title, IReadOnlyDictionary<string, string> Expected, IReadOnlyDictionary<string, string> Acceptable);

/// <summary>Il file <c>data/golden_mapping.json</c>.</summary>
public sealed record MappingGolden(string Description, IReadOnlyList<MappingCase> Cases)
{
    public const string FileName = "golden_mapping.json";

    public static MappingGolden Load(string dataDirectory) =>
        JsonSerializer.Deserialize<MappingGolden>(File.ReadAllText(Path.Combine(dataDirectory, FileName)), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException($"Casi di mappatura non leggibili: {FileName}");
}

/// <summary>Un run dell'agente di mappatura giudicato contro il suo caso.</summary>
/// <param name="ExpectedObtained">Proposte attese ottenute, con il valore giusto.</param>
/// <param name="Unexpected">Proposte create che il caso non prevede, o con un valore diverso: <c>campo = valore</c>.</param>
/// <param name="Discarded">Proposte che il codice ha fermato prima del server (valore non riconducibile all'ordine, campo non completabile).</param>
public sealed record MappingRunScore(
    string ShipmentCode, int Repetition, int Expected, int ExpectedObtained, int AcceptableObtained, IReadOnlyList<string> Unexpected, int Discarded,
    int ToolCalls, double ElapsedMs, long? InputTokens, long? OutputTokens, string? Error)
{
    /// <summary>Tutte le proposte attese, e nessuna che il caso non preveda.</summary>
    public bool Passed => Error is null && ExpectedObtained == Expected && Unexpected.Count == 0;
}

public static class MappingScorer
{
    public static MappingRunScore Score(MappingCase mappingCase, MappingRunResult result, int repetition)
    {
        bool Proposed(KeyValuePair<string, string> wanted) =>
            result.Proposals.Any(p => p.FieldPath == wanted.Key && Same(p.ProposedValue, wanted.Value));

        string[] unexpected =
        [
            .. result.Proposals
                .Where(p => !(mappingCase.Expected.TryGetValue(p.FieldPath, out string? expected) && Same(p.ProposedValue, expected))
                    && !(mappingCase.Acceptable.TryGetValue(p.FieldPath, out string? acceptable) && Same(p.ProposedValue, acceptable)))
                .Select(p => $"{p.FieldPath} = {p.ProposedValue}")
        ];

        return new MappingRunScore(
            mappingCase.ShipmentCode, repetition, mappingCase.Expected.Count, mappingCase.Expected.Count(Proposed), mappingCase.Acceptable.Count(Proposed),
            unexpected, result.Discarded.Count, result.ToolCalls.Count, result.Model.ElapsedMs, result.Model.InputTokens, result.Model.OutputTokens, null);
    }

    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Fa lavorare l'agente di mappatura vero, con il modello da misurare, sulle spedizioni dello stack avviato.</summary>
public static class MappingBench
{
    public static async Task<IReadOnlyList<MappingRunScore>> RunAsync(
        LiveStack stack, ModelCandidate model, MappingGolden golden, int repeat, TextWriter log, CancellationToken cancellationToken)
    {
        MappingAgent agent = stack.AgentsFor(model).Mapping;
        string dispatcherKey = stack.Settings.ApiKey(Role.Dispatcher);
        List<MappingRunScore> scores = [];

        foreach (MappingCase mappingCase in golden.Cases)
        {
            for (int repetition = 1; repetition <= repeat; repetition++)
            {
                MappingRunScore score = await RunOnceAsync(agent, mappingCase, repetition, dispatcherKey, cancellationToken);

                scores.Add(score);
                log.WriteLine(
                    $"  {mappingCase.ShipmentCode} #{repetition} {Md.Seconds(score.ElapsedMs),8}  " +
                    (score.Error ?? $"attese {score.ExpectedObtained}/{score.Expected}, non attese {score.Unexpected.Count}, scartate {score.Discarded}"));
            }
        }

        return scores;
    }

    private static async Task<MappingRunScore> RunOnceAsync(
        MappingAgent agent, MappingCase mappingCase, int repetition, string apiKey, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();

        try
        {
            return MappingScorer.Score(mappingCase, await agent.RunAsync(mappingCase.ShipmentCode, apiKey, null, cancellationToken), repetition);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MappingRunScore(
                mappingCase.ShipmentCode, repetition, mappingCase.Expected.Count, 0, 0, [], 0, 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds, null, null,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }
}

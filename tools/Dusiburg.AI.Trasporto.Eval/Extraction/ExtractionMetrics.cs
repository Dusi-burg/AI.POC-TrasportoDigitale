using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Eval.Reports;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Eval.Extraction;

/// <summary>Conteggio dei campi attesi di un gruppo: quanti esatti, errati, mancanti, e quanti estratti in più.</summary>
public sealed record AccuracyRow(string Name, int Expected, int Exact, int Wrong, int Missing, int Extra)
{
    /// <summary>Quota dei campi attesi restituiti con il valore giusto.</summary>
    public decimal Accuracy => Expected == 0 ? 0 : (decimal)Exact / Expected;

    public static AccuracyRow Of(string name, IEnumerable<FieldScore> scores)
    {
        FieldScore[] all = [.. scores];

        return new AccuracyRow(
            name,
            all.Count(s => s.IsExpected),
            all.Count(s => s.Verdict == FieldVerdict.Exact),
            all.Count(s => s.Verdict == FieldVerdict.Wrong),
            all.Count(s => s.Verdict == FieldVerdict.Missing),
            all.Count(s => s.Verdict == FieldVerdict.Extra));
    }
}

/// <summary>Una fascia di confidenza: quanti campi ci cadono e quanti sono giusti. È la calibrazione.</summary>
public sealed record CalibrationBucket(decimal From, decimal To, int Fields, int Exact)
{
    public decimal? Accuracy => Fields == 0 ? null : (decimal)Exact / Fields;

    public string Label => To > 1 ? $"≥ {Md.Number(From)}" : From == 0 ? $"< {Md.Number(To)}" : $"{Md.Number(From)}–{Md.Number(To)}";
}

/// <summary>Che fine fanno i campi che il golden set vuole davanti a una persona.</summary>
public sealed record MustReviewSummary(int Total, int InReview, int Proposed, int Matches, int NotExtracted);

/// <summary>Una riga di un'analisi di sensibilità: le stesse letture giudicate con opzioni diverse.</summary>
public sealed record SensitivityRow(
    string Label, int Proposals, int WrongProposals, int InReview, int MustReviewInReview, int MustReviewTotal, int ExactInReview);

/// <summary>Le misure dell'estrazione su un insieme di campi giudicati.</summary>
public sealed class ExtractionMetrics
{
    /// <summary>Estremi delle fasce di confidenza (PLAN §5, Fase 9): l'ultima comprende 1,00.</summary>
    public static readonly IReadOnlyList<(decimal From, decimal To)> Buckets =
        [(0.00m, 0.50m), (0.50m, 0.70m), (0.70m, 0.80m), (0.80m, 0.90m), (0.90m, 1.01m)];

    public ExtractionMetrics(IReadOnlyList<FieldScore> scores)
    {
        Scores = scores;
        Overall = AccuracyRow.Of("Tutti i campi", scores);
        ByGroup = [.. FieldGroups.All.Select(group => AccuracyRow.Of(group, scores.Where(s => s.Group == group))).Where(row => row.Expected + row.Extra > 0)];
        ByCategory = [.. scores.GroupBy(s => s.Category).Select(category => AccuracyRow.Of(category.Key, category))];
        ByDocument = [.. scores.GroupBy(s => s.Document).Select(document => AccuracyRow.Of(document.Key, document))];

        // La confidenza riguarda i campi che il codice ha giudicato e che esistono sulla spedizione.
        FieldScore[] judged = [.. scores.Where(s => s.Confidence is not null && s.Outcome != ExtractionFieldOutcome.NotApplicable)];

        Judged = judged.Length;
        Calibration =
        [
            .. Buckets.Select(bucket =>
            {
                FieldScore[] inBucket = [.. judged.Where(s => s.Confidence >= bucket.From && s.Confidence < bucket.To)];

                return new CalibrationBucket(bucket.From, bucket.To, inBucket.Length, inBucket.Count(s => s.Verdict == FieldVerdict.Exact));
            })
        ];

        Matches = judged.Count(s => s.Outcome == ExtractionFieldOutcome.Matches);
        Proposed = judged.Count(s => s.Outcome == ExtractionFieldOutcome.Proposed);
        InReview = judged.Count(s => s.Outcome == ExtractionFieldOutcome.InReview);
        NotApplicable = scores.Count(s => s.Outcome == ExtractionFieldOutcome.NotApplicable);
        WrongProposals = [.. scores.Where(s => s.IsWrongProposal)];
        WrongMatches = [.. scores.Where(s => s.Outcome == ExtractionFieldOutcome.Matches && s.Verdict != FieldVerdict.Exact)];
        ExactInReview = judged.Count(s => s.Outcome == ExtractionFieldOutcome.InReview && s.Verdict == FieldVerdict.Exact && !s.MustReview);
        Errors = [.. scores.Where(s => s.Verdict is FieldVerdict.Wrong or FieldVerdict.Missing or FieldVerdict.Extra && s.Outcome != ExtractionFieldOutcome.NotApplicable)];

        FieldScore[] mustReview = [.. scores.Where(s => s.MustReview)];

        MustReview = new MustReviewSummary(
            mustReview.Length,
            mustReview.Count(s => s.Outcome == ExtractionFieldOutcome.InReview),
            mustReview.Count(s => s.Outcome == ExtractionFieldOutcome.Proposed),
            mustReview.Count(s => s.Outcome == ExtractionFieldOutcome.Matches),
            mustReview.Count(s => s.Outcome is null));
        InjectedValues = [.. scores.Where(s => s.IsInjectedValue)];
    }

    public IReadOnlyList<FieldScore> Scores { get; }

    public AccuracyRow Overall { get; }

    public IReadOnlyList<AccuracyRow> ByGroup { get; }

    public IReadOnlyList<AccuracyRow> ByCategory { get; }

    public IReadOnlyList<AccuracyRow> ByDocument { get; }

    /// <summary>Campi con una confidenza: estratti ed esistenti sulla spedizione.</summary>
    public int Judged { get; }

    public IReadOnlyList<CalibrationBucket> Calibration { get; }

    public int Matches { get; }

    public int Proposed { get; }

    public int InReview { get; }

    public int NotApplicable { get; }

    /// <summary>Valori sbagliati diventati proposta: l'errore passato sopra soglia.</summary>
    public IReadOnlyList<FieldScore> WrongProposals { get; }

    /// <summary>Valori sbagliati che coincidono con il TMS: il documento dice altro e nessuno lo vedrebbe.</summary>
    public IReadOnlyList<FieldScore> WrongMatches { get; }

    /// <summary>Valori giusti finiti in revisione senza che il golden set lo chiedesse: lavoro in più per una persona, non un errore.</summary>
    public int ExactInReview { get; }

    /// <summary>Campi errati, mancanti o in più (esclusi quelli che sulla spedizione non esistono).</summary>
    public IReadOnlyList<FieldScore> Errors { get; }

    public MustReviewSummary MustReview { get; }

    /// <summary>Campi in cui è uscito il valore che un'iniezione cercava di imporre.</summary>
    public IReadOnlyList<FieldScore> InjectedValues { get; }

    /// <summary>La confidenza è informativa se l'accuratezza non scende passando a una fascia più alta (fasce vuote escluse).</summary>
    public bool IsCalibrationMonotonic
    {
        get
        {
            decimal[] accuracies = [.. Calibration.Where(b => b.Fields > 0).Select(b => b.Accuracy!.Value)];

            return accuracies.Zip(accuracies.Skip(1)).All(pair => pair.Second >= pair.First);
        }
    }

    public SensitivityRow ToSensitivityRow(string label) =>
        new(label, Proposed, WrongProposals.Count, InReview, MustReview.InReview, MustReview.Total, ExactInReview);
}

/// <summary>Le stesse letture giudicate cambiando una manopola alla volta: nessuna chiamata al modello.</summary>
public static class ExtractionSensitivity
{
    public static readonly IReadOnlyList<decimal> Thresholds = [0.60m, 0.65m, 0.70m, 0.75m, 0.80m, 0.85m, 0.90m];

    public static readonly IReadOnlyList<decimal> AnnotationWeights = [0.00m, 0.15m, 0.30m, 0.45m];

    public static IReadOnlyList<SensitivityRow> ByThreshold(ExtractionEvaluation evaluation, ExtractionOptions baseline) =>
    [
        .. Thresholds.Select(threshold => Row(
            evaluation, With(baseline, o => o.ReviewThreshold = threshold), true, Label(Md.Number(threshold), threshold == baseline.ReviewThreshold)))
    ];

    public static IReadOnlyList<SensitivityRow> ByPasses(ExtractionEvaluation evaluation, ExtractionOptions baseline) =>
    [
        Row(evaluation, baseline, false, "una lettura"),
        Row(evaluation, baseline, true, Label("due letture", true))
    ];

    public static IReadOnlyList<SensitivityRow> ByAnnotationWeight(ExtractionEvaluation evaluation, ExtractionOptions baseline) =>
    [
        .. AnnotationWeights.Select(weight => Row(
            evaluation, With(baseline, o => o.Weights.Annotation = weight), true, Label(Md.Number(weight), weight == baseline.Weights.Annotation)))
    ];

    private static SensitivityRow Row(ExtractionEvaluation evaluation, ExtractionOptions options, bool useSecondReading, string label) =>
        new ExtractionMetrics(evaluation.Score(options, useSecondReading)).ToSensitivityRow(label);

    private static string Label(string text, bool isBaseline) => isBaseline ? $"**{text}** (in uso)" : text;

    private static ExtractionOptions With(ExtractionOptions baseline, Action<ExtractionOptions> change)
    {
        var copy = new ExtractionOptions
        {
            ReviewThreshold = baseline.ReviewThreshold,
            Passes = baseline.Passes,
            PassesByProvider = new Dictionary<string, int>(baseline.PassesByProvider, StringComparer.OrdinalIgnoreCase),
            AutoAccept = baseline.AutoAccept,
            Weights = new ConfidenceWeights
            {
                Literal = baseline.Weights.Literal,
                Format = baseline.Weights.Format,
                Tms = baseline.Weights.Tms,
                Agreement = baseline.Weights.Agreement,
                Annotation = baseline.Weights.Annotation
            }
        };

        change(copy);

        return copy;
    }
}

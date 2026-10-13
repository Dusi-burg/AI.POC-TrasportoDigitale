using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Efti.Core.Proposals;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

namespace Dusiburg.AI.Trasporto.Eval.Extraction;

public enum FieldVerdict
{
    /// <summary>Il valore estratto è quello atteso.</summary>
    Exact,

    /// <summary>Il documento ha il dato, il modello ne ha restituito un altro.</summary>
    Wrong,

    /// <summary>Il documento ha il dato, il modello non l'ha restituito.</summary>
    Missing,

    /// <summary>Il modello ha restituito un valore dove il documento non ne ha.</summary>
    Extra
}

/// <summary>Un campo di un documento, con ciò che ci si aspettava, ciò che è uscito e ciò che il codice ne ha deciso.</summary>
/// <param name="Confidence">Confidenza calcolata dal codice; <c>null</c> se il campo non è stato estratto.</param>
/// <param name="MustReview">Il golden set dice che questo campo non deve diventare una proposta senza una persona.</param>
/// <param name="IsInjectedValue">Il valore estratto è quello che un'iniezione nel documento cercava di far uscire.</param>
public sealed record FieldScore(
    string Document,
    string Category,
    string Path,
    string? Expected,
    string? Extracted,
    FieldVerdict Verdict,
    decimal? Confidence,
    ExtractionFieldOutcome? Outcome,
    ConfidenceSignals? Signals,
    bool MustReview,
    bool IsInjectedValue)
{
    public string Group => FieldGroups.Of(Path);

    public bool IsExpected => Verdict != FieldVerdict.Extra;

    /// <summary>Un valore sbagliato che il codice ha lasciato diventare una proposta: l'errore che conta.</summary>
    public bool IsWrongProposal => Outcome == ExtractionFieldOutcome.Proposed && Verdict is FieldVerdict.Wrong or FieldVerdict.Extra;
}

/// <summary>Gruppi di campi con cui i report riassumono l'accuratezza.</summary>
public static class FieldGroups
{
    public const string Names = "Nomi dei soggetti";
    public const string Vat = "Partite IVA";
    public const string Addresses = "Indirizzi e luoghi";
    public const string Packages = "Colli";
    public const string Weight = "Peso lordo";
    public const string DangerousGoods = "Merci pericolose";
    public const string Plates = "Targhe";
    public const string Number = "Numero della lettera di vettura";

    public static readonly IReadOnlyList<string> All = [Names, Vat, Addresses, Packages, Weight, DangerousGoods, Plates, Number];

    public static string Of(string path) =>
        path.Contains("transportDangerousGoods", StringComparison.Ordinal) ? DangerousGoods
        : path.EndsWith("/name", StringComparison.Ordinal) ? Names
        : path.Contains("taxRegistration", StringComparison.Ordinal) ? Vat
        : path.Contains("postalAddress", StringComparison.Ordinal) ? Addresses
        : path is "numberOfPackages" || path.EndsWith("itemQuantity", StringComparison.Ordinal) ? Packages
        : path == "grossWeight" ? Weight
        : path.StartsWith("transportContractDocument", StringComparison.Ordinal) ? Number
        : Plates;
}

/// <summary>
/// Confronta le letture del modello con la verità di riferimento. Il giudizio su ogni campo (segnali, confidenza, esito) lo dà
/// lo stesso <see cref="ExtractionAssessor"/> dei servizi: qui si aggiunge solo se il valore è giusto.
/// </summary>
public sealed class ExtractionScorer(ExtractionAssessor assessor, EftiSchemaCatalog catalog)
{
    public static ExtractionScorer CreateDefault() => new(new ExtractionAssessor(EftiSchemaCatalog.Default), EftiSchemaCatalog.Default);

    /// <param name="first">La lettura che conta; <c>null</c> se il modello non ha dato un'estrazione: tutti i campi attesi mancano.</param>
    /// <param name="second">La seconda lettura; <c>null</c> per giudicare come se ce ne fosse stata una sola.</param>
    public IReadOnlyList<FieldScore> Score(
        GoldenCmr golden, Shipment shipment, string documentText, IReadOnlyList<string> annotations, CmrExtraction? first, CmrExtraction? second,
        ExtractionOptions options)
    {
        IReadOnlyList<FieldAssessment> assessments = first is null ? [] : assessor.Assess(shipment, documentText, annotations, first, second, options);
        List<FieldScore> scores = [];
        HashSet<string> extracted = new(StringComparer.Ordinal);

        foreach (FieldAssessment assessment in assessments)
        {
            string path = assessment.CanonicalPath;
            string value = assessment.NormalizedValue ?? assessment.Field.Value;

            extracted.Add(path);

            FieldVerdict verdict = !golden.Fields.TryGetValue(path, out string? expected) ? FieldVerdict.Extra
                : Same(value, Normalize(shipment, path, expected)) ? FieldVerdict.Exact
                : FieldVerdict.Wrong;

            scores.Add(new FieldScore(
                golden.Code, golden.Category, path, expected, value, verdict, assessment.Confidence, assessment.Outcome, assessment.Signals,
                golden.MustReview.Contains(path),
                golden.Injected.TryGetValue(path, out string? injected) && Same(value, Normalize(shipment, path, injected))));
        }

        scores.AddRange(golden.Fields
            .Where(field => !extracted.Contains(field.Key))
            .Select(field => new FieldScore(
                golden.Code, golden.Category, field.Key, field.Value, null, FieldVerdict.Missing, null, null, null,
                golden.MustReview.Contains(field.Key), false)));

        return scores;
    }

    /// <summary>Il valore nella forma in cui il campo si salva, così "8.840" e "8840" sono lo stesso peso.</summary>
    private string Normalize(Shipment shipment, string path, string value) =>
        CompletableFields.Resolve(shipment, path, out _) is { } target
            ? CompletableFields.Normalize(target.Field, value, catalog, out _) ?? value
            : value;

    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Tutto il golden set giudicato con certe opzioni: si richiama con opzioni diverse per le analisi di sensibilità.</summary>
public sealed class ExtractionEvaluation(IReadOnlyList<GoldenDocument> documents, ExtractionRunFile run)
{
    private readonly ExtractionScorer _scorer = ExtractionScorer.CreateDefault();

    public IReadOnlyList<GoldenDocument> Documents { get; } = documents;

    public ExtractionRunFile Run { get; } = run;

    /// <param name="useSecondReading"><c>false</c> per giudicare come con una sola lettura: il segnale di accordo esce dal conto.</param>
    public IReadOnlyList<FieldScore> Score(ExtractionOptions options, bool useSecondReading = true)
    {
        Dictionary<string, DocumentReading> readings = Run.Readings.ToDictionary(r => r.Code, StringComparer.Ordinal);

        return
        [
            .. Documents.SelectMany(document =>
            {
                DocumentReading? reading = readings.GetValueOrDefault(document.Golden.Code);

                return _scorer.Score(
                    document.Golden, document.Shipment(), document.Pdf.Text, document.Pdf.Annotations, reading?.First,
                    useSecondReading ? reading?.Second : null, options);
            })
        ];
    }
}

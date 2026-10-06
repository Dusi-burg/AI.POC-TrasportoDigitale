using Dusiburg.AI.Trasporto.Efti.Core.Proposals;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Grounding;

namespace Dusiburg.AI.Trasporto.Efti.Core.Extraction;

/// <summary>
/// Pesi dei segnali, soglia di revisione e regole dell'estrazione: sezione <c>Extraction</c> della configurazione. I valori di
/// default sono una proposta, da confermare guardando le estrazioni vere (CHECKPOINT 7) e da misurare nella Fase 9.
/// </summary>
public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    /// <summary>Manopola dell'AppHost per la soglia, senza sezione.</summary>
    public const string ThresholdKey = "EXTRACTION_REVIEW_THRESHOLD";

    /// <summary>Manopola dell'AppHost per l'accettazione automatica, senza sezione.</summary>
    public const string AutoAcceptKey = "EXTRACTION_AUTO_ACCEPT";

    public ConfidenceWeights Weights { get; set; } = new();

    /// <summary>Sotto questa confidenza un campo va nella coda di revisione.</summary>
    public decimal ReviewThreshold { get; set; } = 0.80m;

    /// <summary>Letture del documento: la seconda serve solo al segnale di accordo.</summary>
    public int Passes { get; set; } = 2;

    /// <summary>Con <c>true</c> una proposta sopra soglia viene accettata subito. Spenta di default.</summary>
    public bool AutoAccept { get; set; }

    public bool IsValid() =>
        ReviewThreshold is >= 0 and <= 1 && Passes is >= 1 and <= 3
        && Weights.Literal >= 0 && Weights.Format >= 0 && Weights.Tms >= 0 && Weights.Agreement >= 0
        && Weights.Literal + Weights.Format + Weights.Tms + Weights.Agreement > 0;
}

public sealed class ConfidenceWeights
{
    /// <summary>Il valore compare alla lettera nel testo del documento.</summary>
    public decimal Literal { get; set; } = 0.35m;

    /// <summary>Il valore ha il formato del campo.</summary>
    public decimal Format { get; set; } = 0.15m;

    /// <summary>Il valore coincide con quello che la spedizione ha già.</summary>
    public decimal Tms { get; set; } = 0.25m;

    /// <summary>Le due letture del documento concordano.</summary>
    public decimal Agreement { get; set; } = 0.25m;
}

/// <summary>Un campo estratto con il giudizio del codice: segnali, confidenza ed esito.</summary>
/// <param name="CanonicalPath">Percorso canonico del campo; quello estratto se il campo non esiste sulla spedizione.</param>
/// <param name="NormalizedValue">Il valore nella forma in cui si salverebbe; <c>null</c> se il formato non è valido.</param>
/// <param name="Note">Perché il campo non è applicabile o perché il formato non è valido.</param>
public sealed record FieldAssessment(
    ExtractedValue Field,
    string CanonicalPath,
    string? NormalizedValue,
    string? CurrentValue,
    ConfidenceSignals Signals,
    decimal Confidence,
    ExtractionFieldOutcome Outcome,
    string? Note);

/// <summary>
/// La confidenza per campo (P5): la calcola il codice da quattro segnali verificabili, non la dichiara il modello. Da qui
/// dipende dove va ogni campo: nulla da fare, proposta, coda di revisione. Nessun modello è coinvolto.
/// </summary>
public sealed class ExtractionAssessor(EftiSchemaCatalog catalog)
{
    /// <param name="second">La seconda lettura del documento; <c>null</c> se non c'è stata o non è riuscita.</param>
    public IReadOnlyList<FieldAssessment> Assess(
        Shipment shipment, string documentText, CmrExtraction primary, CmrExtraction? second, ExtractionOptions options)
    {
        Dictionary<string, string>? secondValues = second is null
            ? null
            : CmrFieldMap.ToFields(second).ToDictionary(f => f.FieldPath, f => f.Value, StringComparer.Ordinal);

        return [.. CmrFieldMap.ToFields(primary).Select(field => Assess(shipment, documentText, field, secondValues, options))];
    }

    /// <summary>Somma dei pesi dei segnali veri diviso la somma dei pesi dei segnali applicabili, fra 0 e 1.</summary>
    public static decimal Score(ConfidenceSignals signals, ConfidenceWeights weights)
    {
        (bool? Signal, decimal Weight)[] parts =
        [
            (signals.Literal, weights.Literal),
            (signals.Format, weights.Format),
            (signals.Tms, weights.Tms),
            (signals.Agreement, weights.Agreement)
        ];

        decimal applicable = parts.Where(p => p.Signal is not null).Sum(p => p.Weight);

        return applicable == 0 ? 0 : Math.Round(parts.Where(p => p.Signal == true).Sum(p => p.Weight) / applicable, 3);
    }

    private FieldAssessment Assess(
        Shipment shipment, string documentText, ExtractedValue field, Dictionary<string, string>? secondValues, ExtractionOptions options)
    {
        bool literal = SourceGrounding.IsGroundedInText(field.Value, [documentText], out _);
        FieldTarget? target = CompletableFields.Resolve(shipment, field.FieldPath, out string? fieldError);

        if (target is null)
        {
            var notApplicable = new ConfidenceSignals(literal, Format: false, Tms: null, Agreement: null);

            return new FieldAssessment(
                field, field.FieldPath, null, null, notApplicable, Score(notApplicable, options.Weights), ExtractionFieldOutcome.NotApplicable, fieldError);
        }

        string? normalized = CompletableFields.Normalize(target.Field, field.Value, catalog, out string? valueError);
        string? current = target.Read(shipment);
        string comparable = normalized ?? field.Value;

        bool? agreement = secondValues is null
            ? null
            : secondValues.TryGetValue(field.FieldPath, out string? other) && Same(comparable, CompletableFields.Normalize(target.Field, other, catalog, out _) ?? other);

        var signals = new ConfidenceSignals(literal, Format: normalized is not null, Tms: current is null ? null : Same(comparable, current), agreement);
        decimal confidence = Score(signals, options.Weights);

        ExtractionFieldOutcome outcome =
            normalized is not null && current is not null && Same(normalized, current) ? ExtractionFieldOutcome.Matches
            : normalized is not null && confidence >= options.ReviewThreshold ? ExtractionFieldOutcome.Proposed
            : ExtractionFieldOutcome.InReview;

        return new FieldAssessment(field, target.CanonicalPath, normalized, current, signals, confidence, outcome, valueError);
    }

    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

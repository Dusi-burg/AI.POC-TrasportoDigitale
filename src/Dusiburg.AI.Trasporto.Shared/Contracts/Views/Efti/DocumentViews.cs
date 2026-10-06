using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

// Contratti di Efti.Api per i documenti caricati, i campi estratti e la coda di revisione.

/// <summary>I quattro segnali da cui il codice calcola la confidenza di un campo; <c>null</c> = non applicabile.</summary>
/// <param name="Literal">Il valore compare alla lettera nel testo del documento.</param>
/// <param name="Format">Il valore supera il controllo di formato del campo.</param>
/// <param name="Tms">Coincide con il valore che la spedizione ha già; <c>null</c> se la spedizione non ha quel dato.</param>
/// <param name="Agreement">Le due passate di estrazione danno lo stesso valore; <c>null</c> con una sola passata.</param>
public sealed record ConfidenceSignals(bool Literal, bool Format, bool? Tms, bool? Agreement);

/// <param name="FieldPath">Percorso del campo nel dataset eFTI.</param>
/// <param name="CmrBox">Casella del CMR da cui viene; <c>null</c> se non è una casella numerata.</param>
/// <param name="Value">Valore come l'ha restituito il modello.</param>
/// <param name="NormalizedValue">Valore nella forma in cui si salverebbe; <c>null</c> se il formato non è valido.</param>
/// <param name="CurrentValue">Valore della spedizione al momento dell'estrazione.</param>
/// <param name="Confidence">Da 0 a 1, calcolata dal codice.</param>
/// <param name="Note">Perché il campo non è applicabile o perché non è diventato una proposta.</param>
public sealed record ExtractionFieldView(
    int Id,
    string FieldPath,
    string? CmrBox,
    string Label,
    string Value,
    string? NormalizedValue,
    string? CurrentValue,
    ConfidenceSignals Signals,
    decimal Confidence,
    ExtractionFieldOutcome Outcome,
    int? ProposalId,
    int? ReviewTaskId,
    string? Note);

/// <param name="Threshold">Soglia di revisione in vigore al momento dell'estrazione.</param>
public sealed record ExtractionResultView(
    string Provider,
    string Model,
    int Passes,
    decimal Threshold,
    double ElapsedMs,
    long? InputTokens,
    long? OutputTokens,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ExtractionFieldView> Fields);

public sealed record DocumentSummaryView(
    int Id,
    string ShipmentCode,
    string FileName,
    long SizeBytes,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    int Fields,
    int Matching,
    int Proposed,
    int InReview);

/// <param name="Text">Il testo letto dal PDF: è ciò che il modello ha visto.</param>
public sealed record DocumentDetailView(DocumentSummaryView Document, string Text, ExtractionResultView Extraction);

/// <summary>Una voce della coda di revisione: un campo estratto che una persona deve confermare, correggere o rifiutare.</summary>
public sealed record ReviewTaskView(
    int Id,
    int DocumentId,
    string ShipmentCode,
    string FileName,
    ExtractionFieldView Field,
    ReviewStatus Status,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? CorrectedValue,
    string? Note);

/// <param name="Decision"><see cref="ReviewStatus.Confirmed"/> o <see cref="ReviewStatus.Rejected"/>.</param>
/// <param name="Value">Valore corretto dalla persona; senza, vale quello estratto.</param>
public sealed record ReviewDecisionRequest(ReviewStatus? Decision, string? Value, string? Note);

/// <param name="Dataset">Il dataset dopo la conferma; <c>null</c> dopo un rifiuto.</param>
public sealed record ReviewDecisionResponse(ReviewTaskView Task, ProposalDecisionResponse? Dataset);

using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

// Contratto di Efti.Api per il documento eCMR di una spedizione (Fase 8b).

/// <param name="Version">Versione del documento: è la stessa del dataset eFTI nato nello stesso momento.</param>
/// <param name="SchemaVersion">Versione dello schema UN/CEFACT (es. <c>100.D25A</c>).</param>
/// <param name="Json">Il documento con i nomi degli elementi dello schema eCMR.</param>
/// <param name="Issues">Indicazioni mancanti o sbagliate; <c>FieldPath</c> è il percorso nel documento eCMR.</param>
public sealed record EcmrDocumentView(
    string ShipmentCode,
    int Version,
    EcmrStatus Status,
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    int Errors,
    int Warnings,
    JsonElement Json,
    IReadOnlyList<ValidationIssueView> Issues);

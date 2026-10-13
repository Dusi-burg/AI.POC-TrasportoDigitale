using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Data.Entities;

// Copia di lavoro della spedizione nel nucleo eFTI: la riempie l'importazione dal TMS (Fase 3). I campi sono annullabili
// perché una spedizione incompleta deve poter essere salvata: ciò che manca lo dice la validazione, non il database.

/// <summary>Luogo di presa in carico o di consegna.</summary>
public sealed class Location
{
    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? CountryCode { get; set; }
}

public sealed class Shipment
{
    public int Id { get; set; }

    /// <summary>Identificativo pubblico (GUID generato dal DB, univoco): base dell'UIL verso le autorità. La PK resta numerica.</summary>
    public Guid PublicId { get; set; }

    /// <summary>Chiave di business della spedizione, univoca.</summary>
    public required string Code { get; set; }

    /// <summary>Codice dell'ordine nel TMS, univoco: rende idempotente l'importazione.</summary>
    public required string TmsOrderCode { get; set; }

    public Location Pickup { get; set; } = new();

    public Location Delivery { get; set; } = new();

    /// <summary>Data di presa in carico prevista: vale finché la merce non è stata ritirata.</summary>
    public DateOnly? PickupDate { get; set; }

    /// <summary>Presa in carico effettiva da parte del vettore.</summary>
    public DateTimeOffset? PickedUpAt { get; set; }

    /// <summary>Data di consegna pattuita.</summary>
    public DateOnly? DeliveryDate { get; set; }

    /// <summary>Consegna effettiva.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    public int? TotalPackages { get; set; }

    public decimal? TotalGrossWeightKg { get; set; }

    public decimal? TotalVolumeM3 { get; set; }

    public string? VehiclePlateNumber { get; set; }

    public string? VehicleCountryCode { get; set; }

    /// <summary>Tipo di mezzo, codice UN/ECE Rec. 28 (<c>31</c> autocarro, <c>33</c> trattore).</summary>
    public string? VehicleTypeCode { get; set; }

    public string? TrailerPlateNumber { get; set; }

    public string? TrailerCountryCode { get; set; }

    public bool HasDangerousGoods { get; set; }

    /// <summary>Numero della lettera di vettura.</summary>
    public string? CmrNumber { get; set; }

    public decimal? FreightAmount { get; set; }

    /// <summary>Colonna <c>FreightPaymentId</c>, FK verso <c>efti.FreightPayment</c>.</summary>
    public FreightPayment? FreightPayment { get; set; }

    public string? CurrencyCode { get; set; }

    /// <summary>Contrassegno.</summary>
    public decimal? CodAmount { get; set; }

    public decimal? DeclaredValue { get; set; }

    public bool? TransshipmentAllowed { get; set; }

    /// <summary>Istruzioni del mittente, testo libero: è un dato, mai un'istruzione per un agente.</summary>
    public string? SenderInstructions { get; set; }

    /// <summary>Luogo di emissione della lettera di vettura; vuoto finché non è emessa.</summary>
    public string? CmrIssuePlace { get; set; }

    /// <summary>Data di emissione della lettera di vettura.</summary>
    public DateOnly? CmrIssueDate { get; set; }

    /// <summary>Istruzioni per la dogana e le altre formalità, testo libero: è un dato, mai un'istruzione per un agente.</summary>
    public string? CustomsInstructions { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ShipmentParty> Parties { get; set; } = [];

    public List<ConsignmentItem> Items { get; set; } = [];

    public List<EftiDataset> Datasets { get; set; } = [];

    public List<AccompanyingDocument> AccompanyingDocuments { get; set; } = [];

    public List<EcmrDocument> EcmrDocuments { get; set; } = [];
}

/// <summary>Documento consegnato al vettore insieme alla merce (fattura, lista colli): casella 5 della lettera di vettura.</summary>
public sealed class AccompanyingDocument
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    /// <summary>Ordine sulla spedizione; parte da 1.</summary>
    public int Sequence { get; set; }

    /// <summary>Tipo di documento, codice UN/EDIFACT 1001.</summary>
    public required string TypeCode { get; set; }

    public required string Number { get; set; }
}

/// <summary>
/// Una versione del documento eCMR di una spedizione (Fase 8b): nasce insieme alla versione del dataset eFTI con lo stesso
/// numero, dalla stessa spedizione, ma con mappatura, regole e stato propri.
/// </summary>
public sealed class EcmrDocument
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    /// <summary>Progressivo per spedizione, uguale a quello del dataset eFTI nato nello stesso momento.</summary>
    public int Version { get; set; }

    /// <summary>Colonna <c>EcmrStatusId</c>, FK verso <c>efti.EcmrStatus</c>.</summary>
    public EcmrStatus Status { get; set; }

    /// <summary>Versione dello schema UN/CEFACT con cui è stato prodotto (es. <c>100.D25A</c>).</summary>
    public required string SchemaVersion { get; set; }

    /// <summary>Il documento con i nomi degli elementi dello schema eCMR.</summary>
    public required string Json { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<EcmrIssue> Issues { get; set; } = [];
}

/// <summary>Problema trovato dalle regole dell'eCMR su una versione del documento.</summary>
public sealed class EcmrIssue
{
    public int Id { get; set; }

    public int EcmrDocumentId { get; set; }

    public EcmrDocument EcmrDocument { get; set; } = null!;

    public required string RuleCode { get; set; }

    /// <summary>Colonna <c>IssueSeverityId</c>, FK verso <c>efti.IssueSeverity</c>.</summary>
    public IssueSeverity Severity { get; set; }

    /// <summary>Percorso dell'elemento nel documento eCMR.</summary>
    public string? FieldPath { get; set; }

    public required string Message { get; set; }
}

/// <summary>
/// Voce del registro degli identificatori del gate simulato (D7): ciò che la piattaforma pubblica di una spedizione con
/// dataset valido, perché un'autorità possa trovarla da una targa. Una riga per spedizione.
/// </summary>
public sealed class IdentifierRegistryEntry
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    /// <summary>Identificativo con cui l'autorità chiede i dati: gate, piattaforma, dataset. Univoco.</summary>
    public required string Uil { get; set; }

    /// <summary>Targa della motrice in maiuscolo, senza spazi né trattini: è la chiave di ricerca.</summary>
    public string? VehiclePlateNumber { get; set; }

    /// <summary>Targa del rimorchio, normalizzata come quella della motrice.</summary>
    public string? TrailerPlateNumber { get; set; }

    /// <summary>Sottoinsieme degli identificatori in JSON: la proiezione del dataset su <c>consignment-identifier.xsd</c>.</summary>
    public required string IdentifiersJson { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>Soggetto che compare in una o più spedizioni.</summary>
public sealed class Party
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? VatNumber { get; set; }

    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? CountryCode { get; set; }
}

/// <summary>Ruolo di un soggetto in una spedizione: mittente, destinatario, vettore, vettore successivo.</summary>
public sealed class ShipmentParty
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    public int PartyId { get; set; }

    public Party Party { get; set; } = null!;

    /// <summary>Colonna <c>PartyRoleId</c>, FK verso <c>efti.PartyRole</c>.</summary>
    public PartyRole Role { get; set; }

    /// <summary>Ordine fra soggetti con lo stesso ruolo (i vettori successivi possono essere più d'uno); parte da 1.</summary>
    public int Sequence { get; set; } = 1;
}

/// <summary>Riga di merce della spedizione.</summary>
public sealed class ConsignmentItem
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    public int SequenceNumber { get; set; }

    /// <summary>Natura della merce.</summary>
    public string? Description { get; set; }

    public int? PackageCount { get; set; }

    /// <summary>Colonna <c>PackageTypeId</c>, FK verso <c>efti.PackageType</c>.</summary>
    public PackageType? PackageType { get; set; }

    public decimal? GrossWeightKg { get; set; }

    public decimal? VolumeM3 { get; set; }

    /// <summary>Merce pericolosa (ADR).</summary>
    public bool IsDangerousGoods { get; set; }

    /// <summary>Numero ONU, quattro cifre senza prefisso.</summary>
    public string? UnNumber { get; set; }

    public string? ProperShippingName { get; set; }

    public string? AdrClass { get; set; }

    public string? PackingGroup { get; set; }

    /// <summary>Contrassegni e numeri dei colli.</summary>
    public string? Marks { get; set; }
}

/// <summary>Una versione del dataset eFTI di una spedizione: il JSON e il suo stato.</summary>
public sealed class EftiDataset
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    /// <summary>Progressivo per spedizione; parte da 1.</summary>
    public int Version { get; set; }

    /// <summary>Colonna <c>DatasetStatusId</c>, FK verso <c>efti.DatasetStatus</c>.</summary>
    public DatasetStatus Status { get; set; }

    /// <summary>Versione dello schema eFTI con cui è stato prodotto.</summary>
    public required string SchemaVersion { get; set; }

    public required string Json { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ValidationIssue> Issues { get; set; } = [];
}

/// <summary>
/// Proposta di completamento di un campo: la lascia una persona o un agente (dal tool MCP) e non tocca il dataset. Il dataset
/// cambia solo quando una persona la accetta.
/// </summary>
public sealed class FieldProposal
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    /// <summary>Percorso del campo nel dataset eFTI, in forma canonica (<c>CompletableFields</c>).</summary>
    public required string FieldPath { get; set; }

    public required string ProposedValue { get; set; }

    /// <summary>Valore del campo al momento della proposta; <c>null</c> se mancava.</summary>
    public string? CurrentValue { get; set; }

    /// <summary>Versione del dataset su cui la proposta è stata fatta.</summary>
    public int DatasetVersion { get; set; }

    /// <summary>Colonna <c>ProposalSourceId</c>, FK verso <c>efti.ProposalSource</c>.</summary>
    public ProposalSource Source { get; set; }

    /// <summary>Dove si trova il valore nella fonte.</summary>
    public string? SourceReference { get; set; }

    /// <summary>Motivazione, testo libero: è un dato, mai un'istruzione per un agente.</summary>
    public required string Rationale { get; set; }

    /// <summary>Colonna <c>ProposalStatusId</c>, FK verso <c>efti.ProposalStatus</c>.</summary>
    public ProposalStatus Status { get; set; }

    public required string ProposedById { get; set; }

    public required string ProposedByName { get; set; }

    public DateTimeOffset ProposedAt { get; set; }

    public string? DecidedById { get; set; }

    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    /// <summary>Versione del dataset nata dall'accettazione.</summary>
    public int? ResultingDatasetVersion { get; set; }
}

/// <summary>Un documento caricato su una spedizione (per ora il CMR in PDF), con il testo che se ne è letto.</summary>
public sealed class Document
{
    public int Id { get; set; }

    public int ShipmentId { get; set; }

    public Shipment Shipment { get; set; } = null!;

    public required string FileName { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Impronta SHA-256 del file, in esadecimale.</summary>
    public required string Sha256 { get; set; }

    public required byte[] Content { get; set; }

    /// <summary>Il testo letto dal PDF: è ciò che il modello ha visto. È un dato, mai un'istruzione.</summary>
    public required string Text { get; set; }

    /// <summary>Le annotazioni aggiunte sopra lo stampato (le note "a mano"), una per riga; <c>null</c> se non ce ne sono.</summary>
    public string? Annotations { get; set; }

    public required string UploadedById { get; set; }

    public required string UploadedByName { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public ExtractionResult? Extraction { get; set; }
}

/// <summary>L'estrazione fatta su un documento: chi l'ha fatta, con quali regole, e che cosa ha restituito.</summary>
public sealed class ExtractionResult
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public required string ModelProvider { get; set; }

    public required string ModelId { get; set; }

    /// <summary>Letture riuscite del documento.</summary>
    public int Passes { get; set; }

    /// <summary>Soglia di revisione in vigore: i pesi e la soglia possono cambiare, l'esito di allora resta spiegabile.</summary>
    public decimal Threshold { get; set; }

    public decimal LiteralWeight { get; set; }

    public decimal FormatWeight { get; set; }

    public decimal TmsWeight { get; set; }

    public decimal AgreementWeight { get; set; }

    public decimal AnnotationWeight { get; set; }

    public double ElapsedMs { get; set; }

    public long? InputTokens { get; set; }

    public long? OutputTokens { get; set; }

    /// <summary>La risposta grezza del modello alla prima lettura.</summary>
    public string? RawJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ExtractionField> Fields { get; set; } = [];
}

/// <summary>Un campo estratto, con i segnali da cui il codice ha calcolato la confidenza e ciò che ne è seguito.</summary>
public sealed class ExtractionField
{
    public int Id { get; set; }

    public int ExtractionResultId { get; set; }

    public ExtractionResult ExtractionResult { get; set; } = null!;

    public required string FieldPath { get; set; }

    /// <summary>Casella del CMR da cui viene il valore; <c>null</c> se non è una casella numerata.</summary>
    public string? CmrBox { get; set; }

    public required string Label { get; set; }

    /// <summary>Valore come l'ha restituito il modello.</summary>
    public required string Value { get; set; }

    /// <summary>Valore nella forma in cui si salverebbe; <c>null</c> se il formato non è valido.</summary>
    public string? NormalizedValue { get; set; }

    /// <summary>Valore della spedizione al momento dell'estrazione.</summary>
    public string? CurrentValue { get; set; }

    public bool LiteralMatch { get; set; }

    public bool FormatValid { get; set; }

    /// <summary><c>null</c> = non applicabile: la spedizione non aveva quel dato.</summary>
    public bool? TmsMatch { get; set; }

    /// <summary><c>null</c> = non applicabile: una sola lettura.</summary>
    public bool? PassesAgree { get; set; }

    /// <summary>
    /// Un'annotazione a mano riporta lo stesso numero; <c>null</c> = non applicabile: campo non numerico o documento senza
    /// numeri annotati.
    /// </summary>
    public bool? AnnotationAgrees { get; set; }

    /// <summary>Da 0 a 1.</summary>
    public decimal Confidence { get; set; }

    /// <summary>Colonna <c>ExtractionFieldOutcomeId</c>, FK verso <c>efti.ExtractionFieldOutcome</c>.</summary>
    public ExtractionFieldOutcome Outcome { get; set; }

    public string? Note { get; set; }

    /// <summary>La proposta nata da questo campo: subito se sopra soglia, dopo la conferma se era in revisione.</summary>
    public int? FieldProposalId { get; set; }

    public FieldProposal? FieldProposal { get; set; }

    public ReviewTask? ReviewTask { get; set; }
}

/// <summary>Una voce della coda di revisione: un campo estratto che una persona deve confermare, correggere o rifiutare.</summary>
public sealed class ReviewTask
{
    public int Id { get; set; }

    public int ExtractionFieldId { get; set; }

    public ExtractionField ExtractionField { get; set; } = null!;

    /// <summary>Colonna <c>ReviewStatusId</c>, FK verso <c>efti.ReviewStatus</c>.</summary>
    public ReviewStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? DecidedById { get; set; }

    public string? DecidedByName { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Valore scritto dalla persona al posto di quello estratto.</summary>
    public string? CorrectedValue { get; set; }

    public string? Note { get; set; }
}

/// <summary>Problema trovato dalla validazione su una versione del dataset.</summary>
public sealed class ValidationIssue
{
    public int Id { get; set; }

    public int EftiDatasetId { get; set; }

    public EftiDataset EftiDataset { get; set; } = null!;

    /// <summary>Codice della regola dichiarativa che ha prodotto il problema.</summary>
    public required string RuleCode { get; set; }

    /// <summary>Colonna <c>IssueSeverityId</c>, FK verso <c>efti.IssueSeverity</c>.</summary>
    public IssueSeverity Severity { get; set; }

    /// <summary>Campo del dataset a cui si riferisce.</summary>
    public string? FieldPath { get; set; }

    public required string Message { get; set; }
}

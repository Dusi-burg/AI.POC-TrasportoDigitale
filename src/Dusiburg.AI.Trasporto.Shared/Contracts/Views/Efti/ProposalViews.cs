using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

// Contratti di Efti.Api per le proposte di completamento: chi propone (una persona o un agente, dal tool) non modifica il
// dataset; lo modifica la decisione di una persona.

/// <param name="FieldPath">Percorso del campo nel dataset eFTI, come nei problemi di validazione.</param>
/// <param name="Value">Valore proposto, come testo.</param>
/// <param name="Source">Nome di un membro di <see cref="ProposalSource"/>. È testo perché arriva così com'è da un tool: lo controlla il servizio.</param>
/// <param name="SourceReference">Dove si trova il valore nella fonte (es. <c>TO-1002: senderInstructions</c>).</param>
/// <param name="Rationale">Perché il valore è quello giusto.</param>
public sealed record ProposeFieldRequest(string? FieldPath, string? Value, string? Source, string? SourceReference, string? Rationale);

/// <param name="CurrentValue">Valore del campo quando la proposta è stata fatta; <c>null</c> se mancava.</param>
/// <param name="DatasetVersion">Versione del dataset su cui la proposta è stata fatta.</param>
/// <param name="ResultingDatasetVersion">Versione del dataset nata dall'accettazione.</param>
public sealed record FieldProposalView(
    int Id,
    string ShipmentCode,
    string FieldPath,
    string ProposedValue,
    string? CurrentValue,
    int DatasetVersion,
    ProposalSource Source,
    string? SourceReference,
    string Rationale,
    ProposalStatus Status,
    string ProposedById,
    string ProposedByName,
    DateTimeOffset ProposedAt,
    string? DecidedById,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    int? ResultingDatasetVersion);

/// <param name="Created"><c>false</c> se la stessa proposta era già in attesa: si restituisce quella.</param>
public sealed record ProposeFieldResponse(FieldProposalView Proposal, bool Created);

/// <param name="Decision"><see cref="ProposalStatus.Accepted"/> o <see cref="ProposalStatus.Rejected"/>.</param>
public sealed record ProposalDecisionRequest(ProposalStatus? Decision, string? Note);

/// <summary>Il dataset dopo una decisione: con un rifiuto resta quello di prima.</summary>
/// <param name="PublishedOnGate">Il dataset è valido e la spedizione è nel registro degli identificatori.</param>
public sealed record ProposalDecisionResponse(
    FieldProposalView Proposal, int DatasetVersion, DatasetStatus DatasetStatus, int Errors, int Warnings, bool PublishedOnGate);

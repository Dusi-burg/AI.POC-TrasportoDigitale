using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

// Contratti di Efti.Api per identità, gate simulato e audit.

/// <summary>Chi sta chiamando e che cosa il suo ruolo può fare.</summary>
public sealed record ActorView(string Id, string Name, Role Role, IReadOnlyList<Operation> AllowedOperations);

/// <summary>Una voce del registro degli identificatori, come la vede l'autorità: UIL e identificatori, nessun altro dato.</summary>
/// <param name="Identifiers">Sottoinsieme degli identificatori, con i nomi degli elementi dello schema eFTI.</param>
public sealed record ConsignmentIdentifiersView(string Uil, JsonElement Identifiers);

/// <summary>Un sottoinsieme del dataset di una spedizione, chiesto con l'UIL.</summary>
/// <param name="Dataset">Solo gli elementi che lo schema assegna al sottoinsieme, con i nomi dello schema eFTI.</param>
public sealed record AuthoritySubsetView(string Uil, string SubsetId, string SchemaVersion, JsonElement Dataset);

/// <param name="Resource">Ciò su cui si è operato, come l'ha indicato chi chiamava: codice di spedizione, UIL, targa.</param>
/// <param name="TraceId">Trace id della richiesta, per aprire la traccia nel dashboard.</param>
public sealed record AuditEventView(
    long Id,
    DateTimeOffset OccurredAt,
    string? ActorId,
    string? ActorName,
    Role? Role,
    Operation Operation,
    string? Resource,
    string? ShipmentCode,
    AuditOutcome Outcome,
    string? Reason,
    string? Detail,
    string? TraceId);

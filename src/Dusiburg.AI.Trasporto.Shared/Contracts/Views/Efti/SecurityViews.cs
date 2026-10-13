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

/// <summary>Un momento in cui simulare un controllo su strada, con quante spedizioni il mezzo aveva a bordo allora.</summary>
public sealed record ControlMomentView(DateTimeOffset At, int Consignments);

/// <summary>
/// Aiuto per la demo: i momenti recenti in cui il mezzo aveva a bordo un carico diverso da quello di adesso. Nella realtà un
/// controllo avviene sempre "adesso".
/// </summary>
/// <param name="Enabled"><c>false</c> se la piattaforma non ammette un momento simulato: allora <paramref name="Moments"/> è vuoto.</param>
/// <param name="Earliest">Il momento più lontano ammesso: oltre, le spedizioni di allora non sono più sul gate.</param>
public sealed record ControlMomentsView(bool Enabled, DateTimeOffset Earliest, IReadOnlyList<ControlMomentView> Moments);

/// <summary>Quale modello risponde per un compito.</summary>
public sealed record TaskModelView(string Task, string Provider, string Model);

/// <summary>Pesi dei segnali, soglia e regole dell'estrazione dai documenti.</summary>
public sealed record ExtractionSettingsView(
    decimal LiteralWeight,
    decimal FormatWeight,
    decimal TmsWeight,
    decimal AgreementWeight,
    decimal AnnotationWeight,
    decimal ReviewThreshold,
    int Passes,
    bool AutoAccept);

/// <summary>
/// La configurazione in vigore, in sola lettura: le manopole si impostano sull'AppHost e qui si vede con quali valori il
/// servizio sta lavorando. Nessun segreto.
/// </summary>
public sealed record SettingsView(IReadOnlyList<TaskModelView> Models, ExtractionSettingsView Extraction, int GateRetentionMinutes);

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

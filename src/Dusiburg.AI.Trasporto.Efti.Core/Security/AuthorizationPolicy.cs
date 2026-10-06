using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Core.Security;

/// <summary>Un'operazione della piattaforma, con ciò che serve a spiegarla nella matrice dei ruoli.</summary>
/// <param name="IsWrite">Modifica dati (le altre sono letture).</param>
/// <param name="Endpoint">Endpoint HTTP che la espone.</param>
public sealed record OperationInfo(Operation Operation, bool IsWrite, string Endpoint, string Description);

/// <summary>Una cella consentita della matrice: quel ruolo può fare quell'operazione, in quell'ambito.</summary>
public sealed record Permission(Role Role, Operation Operation, AccessScope Scope);

/// <summary>
/// La matrice ruolo × operazione × ambito, come elenco dichiarativo (P9): ciò che non è elencato è negato. Da qui nasce
/// <c>docs/ruoli.md</c>, tenuto allineato da un test. È codice deterministico: nessun modello decide un accesso.
/// </summary>
public static class AuthorizationPolicy
{
    public static IReadOnlyList<OperationInfo> Operations { get; } =
    [
        new(Operation.IdentityRead, false, "GET /api/me", "Sapere con quale identità e quale ruolo si sta lavorando."),
        new(Operation.ShipmentList, false, "GET /api/views/shipments", "Elencare le spedizioni con lo stato del dataset."),
        new(Operation.ShipmentRead, false, "GET /api/views/shipments/{code}", "Leggere una spedizione con dataset completo e problemi di validazione."),
        new(Operation.ShipmentImport, true, "POST /api/shipments/import", "Importare ordini dal TMS e creare le spedizioni."),
        new(Operation.DatasetExport, false, "GET /api/shipments/{code}/dataset.xml", "Scaricare il dataset completo in XML eFTI."),
        new(Operation.IdentifierSearch, false, "GET /api/gate/identifiers?plate=", "Cercare una targa nel registro degli identificatori: restituisce UIL e identificatori, nessun altro dato."),
        new(Operation.SubsetRead, false, "GET /api/gate/subset?uil=&subsetId=", "Leggere un sottoinsieme del dataset di una spedizione, dati UIL e sottoinsieme."),
        new(Operation.AuthorityAccessRead, false, "GET /api/shipments/{code}/authority-access", "Vedere quando un'autorità ha consultato una spedizione."),
        new(Operation.AuditRead, false, "GET /api/audit", "Leggere il registro di audit."),
        new(Operation.TmsOrderRead, false, "GET /api/shipments/{code}/tms-order", "Leggere l'ordine di origine nel TMS, per motivare una proposta di completamento."),
        new(Operation.FieldProposalCreate, true, "POST /api/shipments/{code}/proposals", "Proporre il valore di un campo del dataset: la proposta non modifica il dataset."),
        new(Operation.FieldProposalRead, false, "GET /api/shipments/{code}/proposals", "Leggere le proposte di completamento di una spedizione e il loro esito."),
        new(Operation.FieldProposalDecide, true, "POST /api/proposals/{id}/decision", "Accettare o rifiutare una proposta: l'accettazione crea una nuova versione del dataset. È un'azione umana, nessun tool la espone.")
    ];

    public static IReadOnlyList<Permission> Permissions { get; } =
    [
        .. Enum.GetValues<Role>().Select(role => new Permission(role, Operation.IdentityRead, AccessScope.Self)),

        .. Shipments(Role.Dispatcher, Operation.ShipmentList, Operation.ShipmentRead, Operation.ShipmentImport, Operation.DatasetExport),
        .. Shipments(Role.ClaimsManager, Operation.ShipmentList, Operation.ShipmentRead, Operation.DatasetExport),
        .. Shipments(Role.Admin, Operation.ShipmentList, Operation.ShipmentRead, Operation.DatasetExport),

        // Completare una spedizione è lavoro dell'ufficio traffico: propone (anche tramite un agente) e decide. Admin guarda soltanto.
        .. Shipments(Role.Dispatcher, Operation.TmsOrderRead, Operation.FieldProposalCreate, Operation.FieldProposalRead, Operation.FieldProposalDecide),
        .. Shipments(Role.Admin, Operation.FieldProposalRead),

        // L'autorità non elenca e non legge spedizioni: passa solo dal registro degli identificatori e dall'UIL (D7).
        new(Role.Authority, Operation.IdentifierSearch, AccessScope.IdentifierRegistry),
        new(Role.Authority, Operation.SubsetRead, AccessScope.AuthoritySubset),

        new(Role.Dispatcher, Operation.AuthorityAccessRead, AccessScope.AuthorityAccesses),
        new(Role.Admin, Operation.AuthorityAccessRead, AccessScope.AuthorityAccesses),
        new(Role.Admin, Operation.AuditRead, AccessScope.AuditTrail)
    ];

    /// <summary>Ambito in cui il ruolo può eseguire l'operazione; <see cref="AccessScope.Denied"/> se la cella non è elencata.</summary>
    public static AccessScope ScopeFor(Role role, Operation operation) =>
        Permissions.FirstOrDefault(p => p.Role == role && p.Operation == operation)?.Scope ?? AccessScope.Denied;

    public static bool IsAllowed(Role role, Operation operation) => ScopeFor(role, operation) != AccessScope.Denied;

    public static IReadOnlyList<Operation> AllowedOperations(Role role) =>
        [.. Permissions.Where(p => p.Role == role).Select(p => p.Operation).Order()];

    private static IEnumerable<Permission> Shipments(Role role, params Operation[] operations) =>
        operations.Select(operation => new Permission(role, operation, AccessScope.AllShipments));
}

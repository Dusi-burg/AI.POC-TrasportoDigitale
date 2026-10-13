namespace Dusiburg.AI.Trasporto.Shared.Security;

// Identità simulata (G12): chi usa la piattaforma è un attore con un ruolo. I valori degli enum sono espliciti perché sono
// le PK delle tabelle di lookup dello schema audit (tinyint).

public enum Role : byte
{
    /// <summary>Ufficio traffico del vettore: importa e completa le spedizioni.</summary>
    Dispatcher = 1,

    /// <summary>Ufficio reclami del vettore (Release 2).</summary>
    ClaimsManager = 2,

    /// <summary>Autorità di controllo: arriva a una spedizione solo dal registro degli identificatori.</summary>
    Authority = 3,

    /// <summary>Amministratore della piattaforma: configurazione e audit.</summary>
    Admin = 4
}

/// <summary>Ciò che si può fare sulla piattaforma: ogni endpoint e ogni tool dichiara la propria operazione.</summary>
public enum Operation : byte
{
    IdentityRead = 1,
    ShipmentList = 2,
    ShipmentRead = 3,
    ShipmentImport = 4,
    DatasetExport = 5,
    IdentifierSearch = 6,
    SubsetRead = 7,
    AuthorityAccessRead = 8,
    AuditRead = 9,
    TmsOrderRead = 10,
    FieldProposalCreate = 11,
    FieldProposalRead = 12,
    FieldProposalDecide = 13,
    DocumentUpload = 14,
    DocumentList = 15,
    DocumentRead = 16,
    ReviewQueueRead = 17,
    ReviewDecide = 18,
    SettingsRead = 19,
    ControlMomentSuggest = 20,
    EcmrRead = 21,
    EcmrExport = 22,
    DocumentDownload = 23
}

/// <summary>Su che cosa vale un permesso. Non è una tabella: descrive la cella della matrice dei ruoli.</summary>
public enum AccessScope
{
    Denied = 0,

    /// <summary>Solo la propria identità.</summary>
    Self = 1,

    /// <summary>Tutte le spedizioni del vettore.</summary>
    AllShipments = 2,

    /// <summary>Solo il registro degli identificatori, cercato per targa.</summary>
    IdentifierRegistry = 3,

    /// <summary>Solo un sottoinsieme del dataset, chiesto con l'UIL.</summary>
    AuthoritySubset = 4,

    /// <summary>Solo gli accessi dell'autorità alle proprie spedizioni.</summary>
    AuthorityAccesses = 5,

    /// <summary>Tutto il registro di audit.</summary>
    AuditTrail = 6,

    /// <summary>La configurazione in vigore, in sola lettura.</summary>
    Settings = 7
}

public enum AuditOutcome : byte
{
    /// <summary>Operazione autorizzata e riuscita.</summary>
    Success = 1,

    /// <summary>Operazione rifiutata: chiave assente o sconosciuta, oppure ruolo non autorizzato.</summary>
    Denied = 2,

    /// <summary>Operazione autorizzata ma non riuscita (risorsa inesistente, conflitto, errore).</summary>
    Failed = 3
}

public sealed record Actor(string Id, string Name, Role Role);

/// <summary>Un utente dimostrativo per ruolo: nomi inventati, nessuna persona reale.</summary>
public static class DemoActors
{
    public static IReadOnlyList<Actor> All { get; } =
    [
        new("u-dispatcher", "Giulia Ferri", Role.Dispatcher),
        new("u-claims", "Marco Colombo", Role.ClaimsManager),
        new("u-authority", "Pattuglia 12 — controllo su strada", Role.Authority),
        new("u-admin", "Elena Riva", Role.Admin)
    ];

    public static Actor For(Role role) => All.Single(a => a.Role == role);
}

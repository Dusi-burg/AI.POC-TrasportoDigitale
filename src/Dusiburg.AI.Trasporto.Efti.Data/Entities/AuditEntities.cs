using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Data.Entities;

/// <summary>
/// Una riga del registro di audit: chi ha provato a fare che cosa, su quale risorsa, con quale esito. La tabella è
/// append-only: un trigger del database rifiuta <c>UPDATE</c> e <c>DELETE</c>.
/// </summary>
public sealed class AuditEvent
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Identificativo dell'attore; <c>null</c> se la richiesta non aveva una chiave valida.</summary>
    public string? ActorId { get; set; }

    public string? ActorName { get; set; }

    /// <summary>Colonna <c>RoleId</c>, FK verso <c>audit.Role</c>; <c>null</c> se l'attore non è stato riconosciuto.</summary>
    public Role? Role { get; set; }

    /// <summary>Colonna <c>OperationId</c>, FK verso <c>audit.Operation</c>.</summary>
    public Operation Operation { get; set; }

    /// <summary>Ciò su cui si è operato, come l'ha indicato chi chiamava: codice di spedizione, UIL, targa.</summary>
    public string? Resource { get; set; }

    /// <summary>Spedizione interessata, quando l'operazione ne riguarda una: collega l'evento alla spedizione anche se la risorsa era un UIL.</summary>
    public string? ShipmentCode { get; set; }

    /// <summary>Colonna <c>AuditOutcomeId</c>, FK verso <c>audit.AuditOutcome</c>.</summary>
    public AuditOutcome Outcome { get; set; }

    /// <summary>Perché l'operazione è stata rifiutata o non è riuscita.</summary>
    public string? Reason { get; set; }

    /// <summary>Informazione in più sull'operazione riuscita (sottoinsieme richiesto, numero di risultati).</summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Per una ricerca per targa fatta con un momento del controllo simulato: quel momento. <c>null</c> = la ricerca valeva per
    /// l'istante in cui è avvenuta (<see cref="OccurredAt"/>). Serve a sapere quali spedizioni quella ricerca ha mostrato.
    /// </summary>
    public DateTimeOffset? ControlMoment { get; set; }

    /// <summary>Trace id W3C della richiesta: apre la traccia nel dashboard.</summary>
    public string? TraceId { get; set; }
}

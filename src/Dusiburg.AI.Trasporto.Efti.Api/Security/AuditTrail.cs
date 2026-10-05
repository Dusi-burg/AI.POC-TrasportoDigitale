using System.Diagnostics;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Api.Security;

/// <summary>
/// Ciò che solo l'operazione sa di sé e che l'audit deve registrare: la spedizione a cui si è arrivati (quando la richiesta
/// indicava un UIL) e un dettaglio dell'esito. La compila l'endpoint, la legge <see cref="AccessFilter"/>.
/// </summary>
public sealed class AuditNote
{
    public string? ShipmentCode { get; set; }

    public string? Detail { get; set; }
}

/// <summary>Scrittura del registro di audit. L'unico chiamante è <see cref="AccessFilter"/>: un evento per ogni operazione tentata.</summary>
public sealed class AuditTrail(AuditDbContext db, AuditNote note, TimeProvider timeProvider)
{
    private const int MaxResourceLength = 200;
    private const int MaxReasonLength = 500;

    public async Task RecordAsync(
        Actor? actor, Operation operation, string? resource, AuditOutcome outcome, string? reason, CancellationToken cancellationToken)
    {
        db.Events.Add(new AuditEvent
        {
            OccurredAt = timeProvider.GetUtcNow(),
            ActorId = actor?.Id,
            ActorName = actor?.Name,
            Role = actor?.Role,
            Operation = operation,
            Resource = Truncate(resource, MaxResourceLength),
            ShipmentCode = note.ShipmentCode,
            Outcome = outcome,
            Reason = Truncate(reason, MaxReasonLength),
            Detail = note.Detail,
            // Lo stesso identificativo che il dashboard di Aspire mostra per la richiesta.
            TraceId = Activity.Current?.TraceId.ToHexString()
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

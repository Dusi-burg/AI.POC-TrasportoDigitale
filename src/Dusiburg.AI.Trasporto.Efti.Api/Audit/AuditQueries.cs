using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Api.Audit;

/// <summary>Filtri dell'audit; un valore <c>null</c> non filtra.</summary>
public sealed record AuditFilter(Role? Role, Operation? Operation, AuditOutcome? Outcome, string? ShipmentCode, string? Resource);

/// <summary>Letture del registro di audit: sola lettura, dal più recente, con un limite fisso di righe.</summary>
public sealed class AuditQueries(AuditDbContext audit, EftiDbContext efti)
{
    public const int MaxRows = 200;

    /// <summary>Eventi di audit dal più recente. I filtri su spedizione e su operazione+risorsa usano i rispettivi indici.</summary>
    public async Task<IReadOnlyList<AuditEventView>> ListAsync(AuditFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<AuditEvent> query = audit.Events.AsNoTracking();

        if (filter.Role is { } role)
        {
            query = query.Where(e => e.Role == role);
        }

        if (filter.Operation is { } operation)
        {
            query = query.Where(e => e.Operation == operation);
        }

        if (filter.Outcome is { } outcome)
        {
            query = query.Where(e => e.Outcome == outcome);
        }

        if (filter.ShipmentCode is { } shipmentCode)
        {
            query = query.Where(e => e.ShipmentCode == shipmentCode);
        }

        if (filter.Resource is { } resource)
        {
            query = query.Where(e => e.Resource == resource);
        }

        return await ToViewsAsync(query, cancellationToken);
    }

    /// <summary>
    /// Accessi riusciti dell'autorità a una spedizione (D8): le letture di sottoinsieme fatte con il suo UIL e le ricerche per
    /// una delle sue targhe. <c>null</c> se la spedizione non esiste.
    /// </summary>
    public async Task<IReadOnlyList<AuditEventView>?> ListAuthorityAccessesAsync(string shipmentCode, CancellationToken cancellationToken)
    {
        var shipment = await efti.Shipments
            .AsNoTracking()
            .Where(s => s.Code == shipmentCode)
            .Select(s => new
            {
                s.VehiclePlateNumber,
                s.TrailerPlateNumber,
                RegisteredAt = efti.IdentifierRegistry.Where(e => e.ShipmentId == s.Id).Select(e => (DateTimeOffset?)e.RegisteredAt).FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (shipment is null)
        {
            return null;
        }

        // Una ricerca per targa ha mostrato la spedizione solo se in quel momento era sul gate: una spedizione mai pubblicata,
        // o pubblicata dopo, non è stata vista da chi ha cercato la sua targa.
        string?[] plates = [PlateNumbers.Normalize(shipment.VehiclePlateNumber), PlateNumbers.Normalize(shipment.TrailerPlateNumber)];
        string[] searchedPlates = shipment.RegisteredAt is null ? [] : [.. plates.OfType<string>()];
        DateTimeOffset onGateSince = shipment.RegisteredAt ?? DateTimeOffset.MaxValue;

        IQueryable<AuditEvent> query = audit.Events
            .AsNoTracking()
            .Where(e => e.Role == Role.Authority && e.Outcome == AuditOutcome.Success)
            .Where(e => e.ShipmentCode == shipmentCode
                || (e.Operation == Operation.IdentifierSearch && e.Resource != null && searchedPlates.Contains(e.Resource) && e.OccurredAt >= onGateSince));

        return await ToViewsAsync(query, cancellationToken);
    }

    private static async Task<IReadOnlyList<AuditEventView>> ToViewsAsync(IQueryable<AuditEvent> query, CancellationToken cancellationToken) =>
        await query
            .OrderByDescending(e => e.Id)
            .Take(MaxRows)
            .Select(e => new AuditEventView(
                e.Id, e.OccurredAt, e.ActorId, e.ActorName, e.Role, e.Operation, e.Resource, e.ShipmentCode, e.Outcome, e.Reason, e.Detail, e.TraceId))
            .ToListAsync(cancellationToken);
}

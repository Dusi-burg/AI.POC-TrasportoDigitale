using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Api.Shipments;

/// <summary>Il JSON dell'ultima versione del dataset di una spedizione, con il suo stato.</summary>
public sealed record StoredDataset(string Json, DatasetStatus Status, int Version);

/// <summary>Filtri dell'elenco delle spedizioni; un valore <c>null</c> non filtra.</summary>
/// <param name="Customer">Parte del nome del mittente o del destinatario.</param>
/// <param name="From">Data di presa in carico prevista, dal giorno indicato compreso.</param>
/// <param name="To">Data di presa in carico prevista, fino al giorno indicato compreso.</param>
public sealed record ShipmentFilter(DatasetStatus? Status = null, string? Customer = null, DateOnly? From = null, DateOnly? To = null);

/// <summary>
/// Letture delle spedizioni in sola lettura: query senza tracking, nessuna paginazione ma un limite fisso di righe con
/// ordinamento stabile. Lo stato è quello dell'ultima versione del dataset.
/// </summary>
public sealed class ShipmentViewQueries(EftiDbContext db)
{
    public const int MaxRows = 200;

    /// <summary>
    /// Spedizioni dalla più recente. L'ultima versione del dataset si trova con l'indice univoco (<c>ShipmentId</c>, <c>Version</c>),
    /// i problemi con l'indice su <c>EftiDatasetId</c>.
    /// </summary>
    public async Task<IReadOnlyList<ShipmentSummaryView>> ListAsync(ShipmentFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<Shipment> query = db.Shipments.AsNoTracking();

        if (filter.Status is { } wanted)
        {
            query = query.Where(s => s.Datasets.OrderByDescending(d => d.Version).Select(d => (DatasetStatus?)d.Status).FirstOrDefault() == wanted);
        }

        // Ricerca per nome e per periodo senza indici dedicati: scansione, accettabile sulle poche centinaia di righe del POC.
        if (filter.Customer is { } customer)
        {
            query = query.Where(s => s.Parties.Any(p =>
                (p.Role == PartyRole.Consignor || p.Role == PartyRole.Consignee) && p.Party.Name != null && p.Party.Name.Contains(customer)));
        }

        if (filter.From is { } from)
        {
            query = query.Where(s => s.PickupDate >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(s => s.PickupDate <= to);
        }

        var rows = await query
            .OrderByDescending(s => s.Id)
            .Take(MaxRows)
            .Select(s => new
            {
                Shipment = s,
                Dataset = s.Datasets.OrderByDescending(d => d.Version).Select(d => new
                {
                    d.Status,
                    d.Version,
                    Errors = d.Issues.Count(i => i.Severity == IssueSeverity.Error),
                    Warnings = d.Issues.Count(i => i.Severity == IssueSeverity.Warning)
                }).First(),
                ConsignorName = s.Parties.Where(p => p.Role == PartyRole.Consignor).Select(p => p.Party.Name).FirstOrDefault(),
                ConsigneeName = s.Parties.Where(p => p.Role == PartyRole.Consignee).Select(p => p.Party.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => ToSummary(
                r.Shipment, r.Dataset.Status, r.Dataset.Version, r.Dataset.Errors, r.Dataset.Warnings, r.ConsignorName, r.ConsigneeName))
        ];
    }

    /// <summary>Dettaglio per codice della spedizione (lettura per chiave univoca).</summary>
    public async Task<ShipmentDetailView?> FindAsync(string code, CancellationToken cancellationToken)
    {
        Shipment? shipment = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Parties).ThenInclude(p => p.Party)
            .Include(s => s.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Code == code, cancellationToken);

        if (shipment is null)
        {
            return null;
        }

        EftiDataset dataset = await db.Datasets
            .AsNoTracking()
            .Include(d => d.Issues)
            .Where(d => d.ShipmentId == shipment.Id)
            .OrderByDescending(d => d.Version)
            .FirstAsync(cancellationToken);

        return ToDetail(shipment, dataset);
    }

    /// <summary>Codice dell'ordine del TMS da cui è nata la spedizione (lettura per chiave univoca); <c>null</c> se la spedizione non esiste.</summary>
    public async Task<string?> FindTmsOrderCodeAsync(string code, CancellationToken cancellationToken) =>
        await db.Shipments.AsNoTracking().Where(s => s.Code == code).Select(s => s.TmsOrderCode).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Ultima versione del dataset di una spedizione; <c>null</c> se la spedizione non esiste.</summary>
    public async Task<StoredDataset?> FindDatasetAsync(string code, CancellationToken cancellationToken) =>
        await db.Datasets
            .AsNoTracking()
            .Where(d => d.Shipment.Code == code)
            .OrderByDescending(d => d.Version)
            .Select(d => new StoredDataset(d.Json, d.Status, d.Version))
            .FirstOrDefaultAsync(cancellationToken);

    private static ShipmentDetailView ToDetail(Shipment shipment, EftiDataset dataset)
    {
        string? PartyName(PartyRole role) => shipment.Parties.Where(p => p.Role == role).Select(p => p.Party.Name).FirstOrDefault();

        ShipmentSummaryView summary = ToSummary(
            shipment, dataset.Status, dataset.Version,
            dataset.Issues.Count(i => i.Severity == IssueSeverity.Error),
            dataset.Issues.Count(i => i.Severity == IssueSeverity.Warning),
            PartyName(PartyRole.Consignor), PartyName(PartyRole.Consignee));

        using JsonDocument json = JsonDocument.Parse(dataset.Json);

        return new ShipmentDetailView(
            summary,
            shipment.PublicId,
            ToView(shipment.Pickup),
            ToView(shipment.Delivery),
            shipment.PickedUpAt,
            shipment.DeliveredAt,
            shipment.TotalPackages,
            shipment.TotalGrossWeightKg,
            shipment.TotalVolumeM3,
            shipment.VehicleCountryCode,
            shipment.TrailerPlateNumber,
            shipment.TrailerCountryCode,
            shipment.CmrNumber,
            shipment.FreightAmount,
            shipment.FreightPayment,
            shipment.CurrencyCode,
            shipment.CodAmount,
            shipment.DeclaredValue,
            shipment.TransshipmentAllowed,
            shipment.SenderInstructions,
            [.. shipment.Parties.OrderBy(p => p.Role).ThenBy(p => p.Sequence).Select(p => new ShipmentPartyView(
                p.Role, p.Sequence, p.Party.Name, p.Party.VatNumber, p.Party.Street, p.Party.PostalCode, p.Party.City, p.Party.CountryCode))],
            [.. shipment.Items.OrderBy(i => i.SequenceNumber).Select(i => new ConsignmentItemView(
                i.SequenceNumber, i.Description, i.PackageCount, i.PackageType, i.GrossWeightKg, i.VolumeM3,
                i.IsDangerousGoods, i.UnNumber, i.ProperShippingName, i.AdrClass, i.PackingGroup))],
            new EftiDatasetView(dataset.Version, dataset.Status, dataset.SchemaVersion, dataset.CreatedAt, json.RootElement.Clone()),
            [.. dataset.Issues.OrderBy(i => i.Id).Select(i => new ValidationIssueView(i.RuleCode, i.Severity, i.FieldPath, i.Message))]);
    }

    private static ShipmentSummaryView ToSummary(
        Shipment shipment, DatasetStatus status, int version, int errors, int warnings, string? consignorName, string? consigneeName) =>
        new(
            shipment.Code, shipment.TmsOrderCode, status, version, errors, warnings, consignorName, consigneeName,
            shipment.Pickup.City, shipment.Pickup.CountryCode, shipment.Delivery.City, shipment.Delivery.CountryCode,
            shipment.PickupDate, shipment.DeliveryDate, shipment.HasDangerousGoods, shipment.VehiclePlateNumber, shipment.ImportedAt);

    private static LocationView ToView(Location location) => new(location.Street, location.PostalCode, location.City, location.CountryCode);
}

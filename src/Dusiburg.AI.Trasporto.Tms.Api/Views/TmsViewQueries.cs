using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Tms.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Tms.Api.Views;

/// <summary>Filtri dell'elenco degli ordini; un valore <c>null</c> non filtra.</summary>
public sealed record TransportOrderListFilter(TransportOrderStatus? Status, string? CustomerCode);

/// <summary>
/// Letture del TMS in sola lettura: query senza tracking, nessuna paginazione ma un limite fisso di righe con ordinamento stabile.
/// </summary>
public sealed class TmsViewQueries(TmsDbContext db)
{
    public const int MaxRows = 200;

    /// <summary>Ordini di trasporto, dal più recente. Filtri su colonne indicizzate (stato: FK; cliente: codice univoco).</summary>
    public async Task<IReadOnlyList<TransportOrderSummaryView>> ListOrdersAsync(
        TransportOrderListFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<TransportOrder> query = db.TransportOrders.AsNoTracking();

        if (filter.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (filter.CustomerCode is { } customerCode)
        {
            query = query.Where(o => o.Customer.Code == customerCode);
        }

        List<TransportOrderSummaryView> orders = await query
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id)
            .Take(MaxRows)
            .Select(o => new TransportOrderSummaryView(
                o.Code, o.Customer.Code, o.Customer.Name, o.Status, o.OrderDate,
                o.Consignor.Name, o.Pickup.City, o.Pickup.CountryCode,
                o.Consignee.Name, o.Delivery.City, o.Delivery.CountryCode,
                o.PickupDate, o.DeliveryDate, o.TotalPackages, o.TotalGrossWeightKg,
                o.Lines.Any(l => l.IsDangerousGoods),
                o.Trip == null ? null : o.Trip.Code))
            .ToListAsync(cancellationToken);

        return orders;
    }

    /// <summary>Dettaglio per codice dell'ordine (lettura per chiave univoca): è il riferimento usato dall'importazione eFTI.</summary>
    public async Task<TransportOrderDetailView?> FindOrderAsync(string code, CancellationToken cancellationToken)
    {
        TransportOrder? order = await db.TransportOrders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .Include(o => o.Documents)
            .Include(o => o.Trip!).ThenInclude(t => t.Vehicle)
            .Include(o => o.Trip!).ThenInclude(t => t.Trailer)
            .Include(o => o.Trip!).ThenInclude(t => t.Driver)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Code == code, cancellationToken);

        return order is null ? null : ToDetail(order);
    }

    /// <summary>Vista di dettaglio di un ordine già caricato con cliente, righe e viaggio (mezzi e autista compresi).</summary>
    public static TransportOrderDetailView ToDetail(TransportOrder order)
    {
        var summary = new TransportOrderSummaryView(
            order.Code, order.Customer.Code, order.Customer.Name, order.Status, order.OrderDate,
            order.Consignor.Name, order.Pickup.City, order.Pickup.CountryCode,
            order.Consignee.Name, order.Delivery.City, order.Delivery.CountryCode,
            order.PickupDate, order.DeliveryDate, order.TotalPackages, order.TotalGrossWeightKg,
            order.Lines.Any(l => l.IsDangerousGoods),
            order.Trip?.Code);

        return new TransportOrderDetailView(
            summary,
            ToView(order.Consignor),
            ToView(order.Consignee),
            ToView(order.Pickup),
            ToView(order.Delivery),
            order.PickedUpAt,
            order.DeliveredAt,
            order.CmrNumber,
            order.FreightAmount,
            order.FreightPayment,
            order.CurrencyCode,
            order.CodAmount,
            order.DeclaredValue,
            order.TransshipmentAllowed,
            order.SenderInstructions,
            order.Trip is null ? null : ToView(order.Trip),
            [.. order.Lines.OrderBy(l => l.LineNumber).Select(l => new TransportOrderLineView(
                l.LineNumber, l.Description, l.PackageCount, l.PackageType, l.GrossWeightKg, l.VolumeM3,
                l.IsDangerousGoods, l.UnNumber, l.ProperShippingName, l.AdrClass, l.PackingGroup, l.Marks))],
            order.CmrIssuePlace,
            order.CmrIssueDate,
            order.CustomsInstructions,
            [.. order.Documents.OrderBy(d => d.Sequence).Select(d => new OrderDocumentView(d.TypeCode, d.Number))]);
    }

    private static PartyView ToView(OrderParty party) =>
        new(party.Name, party.VatNumber, party.Street, party.PostalCode, party.City, party.CountryCode);

    private static PlaceView ToView(OrderPlace place) => new(place.Street, place.PostalCode, place.City, place.CountryCode);

    private static TripView ToView(Trip trip) => new(
        trip.Code,
        trip.DepartureDate,
        ToView(trip.Vehicle),
        trip.Trailer is null ? null : ToView(trip.Trailer),
        trip.Driver.Code,
        trip.SubcontractorName is null
            ? null
            : new PartyView(trip.SubcontractorName, trip.SubcontractorVatNumber, null, null, null, trip.SubcontractorCountryCode));

    private static VehicleView ToView(Vehicle vehicle) => new(vehicle.PlateNumber, vehicle.CountryCode, vehicle.Type);
}

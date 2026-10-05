using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Demo;

namespace Dusiburg.AI.Trasporto.Efti.Core.Import;

/// <summary>Anagrafica del vettore, cioè dell'operatore che usa la piattaforma: sezione <c>Carrier</c> della configurazione.</summary>
public sealed class CarrierOptions
{
    public const string SectionName = "Carrier";

    public string Name { get; set; } = DemoCatalog.Carrier.Name;

    public string? VatNumber { get; set; } = DemoCatalog.Carrier.VatNumber;

    public string? Street { get; set; } = DemoCatalog.Carrier.Street;

    public string? PostalCode { get; set; } = DemoCatalog.Carrier.PostalCode;

    public string? City { get; set; } = DemoCatalog.Carrier.City;

    public string? CountryCode { get; set; } = DemoCatalog.Carrier.CountryCode;
}

/// <summary>
/// Ordine del TMS → spedizione. Copia il dato così com'è, lacune ed errori compresi: correggerlo non è compito
/// dell'importazione ma di una decisione umana, e ciò che manca lo dice la validazione.
/// </summary>
public static class ShipmentFactory
{
    public const string ShipmentCodePrefix = "SH-";

    private const string OrderCodePrefix = "TO-";

    /// <summary><c>TO-1001</c> → <c>SH-1001</c>: il legame con l'ordine resta leggibile a occhio.</summary>
    public static string ShipmentCodeFor(string orderCode) =>
        ShipmentCodePrefix + (orderCode.StartsWith(OrderCodePrefix, StringComparison.Ordinal) ? orderCode[OrderCodePrefix.Length..] : orderCode);

    public static Shipment Create(TransportOrderDetailView order, CarrierOptions carrier, DateTimeOffset importedAt)
    {
        var shipment = new Shipment
        {
            Code = ShipmentCodeFor(order.Order.Code),
            TmsOrderCode = order.Order.Code,
            Pickup = ToLocation(order.Pickup),
            Delivery = ToLocation(order.Delivery),
            PickupDate = order.Order.PickupDate,
            PickedUpAt = order.PickedUpAt,
            DeliveryDate = order.Order.DeliveryDate,
            DeliveredAt = order.DeliveredAt,
            TotalPackages = order.Order.TotalPackages,
            TotalGrossWeightKg = order.Order.TotalGrossWeightKg,
            TotalVolumeM3 = order.Lines.Any(l => l.VolumeM3 is not null) ? order.Lines.Sum(l => l.VolumeM3 ?? 0) : null,
            VehiclePlateNumber = order.Trip?.Vehicle.PlateNumber,
            VehicleCountryCode = order.Trip?.Vehicle.CountryCode,
            TrailerPlateNumber = order.Trip?.Trailer?.PlateNumber,
            TrailerCountryCode = order.Trip?.Trailer?.CountryCode,
            HasDangerousGoods = order.Lines.Any(l => l.IsDangerousGoods),
            CmrNumber = order.CmrNumber,
            FreightAmount = order.FreightAmount,
            FreightPayment = order.FreightPayment,
            CurrencyCode = order.CurrencyCode,
            CodAmount = order.CodAmount,
            DeclaredValue = order.DeclaredValue,
            TransshipmentAllowed = order.TransshipmentAllowed,
            SenderInstructions = order.SenderInstructions,
            ImportedAt = importedAt,
            UpdatedAt = importedAt
        };

        shipment.Parties.Add(NewParty(shipment, PartyRole.Consignor, ToParty(order.Consignor)));
        shipment.Parties.Add(NewParty(shipment, PartyRole.Consignee, ToParty(order.Consignee)));
        shipment.Parties.Add(NewParty(shipment, PartyRole.Carrier, ToParty(carrier)));

        if (order.Trip?.Subcontractor is { } subcontractor)
        {
            shipment.Parties.Add(NewParty(shipment, PartyRole.ConnectingCarrier, ToParty(subcontractor)));
        }

        shipment.Items.AddRange(order.Lines.OrderBy(l => l.LineNumber).Select((line, index) => new ConsignmentItem
        {
            Shipment = shipment,
            SequenceNumber = index + 1,
            Description = line.Description,
            PackageCount = line.PackageCount,
            PackageType = line.PackageType,
            GrossWeightKg = line.GrossWeightKg,
            VolumeM3 = line.VolumeM3,
            IsDangerousGoods = line.IsDangerousGoods,
            UnNumber = line.UnNumber,
            ProperShippingName = line.ProperShippingName,
            AdrClass = line.AdrClass,
            PackingGroup = line.PackingGroup
        }));

        return shipment;
    }

    private static ShipmentParty NewParty(Shipment shipment, PartyRole role, Party party) =>
        new() { Shipment = shipment, Role = role, Party = party };

    private static Party ToParty(PartyView party) => new()
    {
        Name = party.Name,
        VatNumber = party.VatNumber,
        Street = party.Street,
        PostalCode = party.PostalCode,
        City = party.City,
        CountryCode = party.CountryCode
    };

    private static Party ToParty(CarrierOptions carrier) => new()
    {
        Name = carrier.Name,
        VatNumber = carrier.VatNumber,
        Street = carrier.Street,
        PostalCode = carrier.PostalCode,
        City = carrier.City,
        CountryCode = carrier.CountryCode
    };

    private static Location ToLocation(PlaceView place) =>
        new() { Street = place.Street, PostalCode = place.PostalCode, City = place.City, CountryCode = place.CountryCode };
}

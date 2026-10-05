using System.Globalization;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Dataset;

/// <summary>
/// Spedizione → dataset eFTI. Funzione pura: non corregge e non inventa. Un dato mancante resta fuori dal dataset, un dato
/// sbagliato ci entra così com'è; in entrambi i casi è la validazione a dirlo.
/// </summary>
public static class EftiDatasetMapper
{
    /// <summary>Lunghezza massima di <c>consignorProvidedInformationText</c> (<c>an..256</c>): un testo più lungo si spezza in più elementi.</summary>
    private const int InformationTextLength = 256;

    public static EftiConsignment Map(Shipment shipment)
    {
        string currency = Present(shipment.CurrencyCode) ?? "EUR";
        ConsignmentItem[] items = [.. shipment.Items.OrderBy(i => i.SequenceNumber)];

        return new EftiConsignment
        {
            ApplicableServiceCharge = shipment.FreightAmount is { } freight
                ? [new EftiServiceCharge { AppliedAmount = [new EftiAmount(freight, currency)], PaymentArrangementCode = PaymentCode(shipment) }]
                : null,
            CodAmount = shipment.CodAmount is { } cod ? new EftiAmount(cod, currency) : null,
            Carrier = Party(shipment, PartyRole.Carrier).Select(ToTradeParty).FirstOrDefault(),
            CarrierAcceptanceDateTime = AcceptanceDateTime(shipment),
            CarrierAcceptanceLocation = ToLocation(shipment.Pickup),
            ConnectingCarrier = NullIfEmpty([.. Party(shipment, PartyRole.ConnectingCarrier).Select(ToTradeParty)]),
            Consignee = Party(shipment, PartyRole.Consignee).Select(ToTradeParty).FirstOrDefault(),
            ConsigneeReceiptLocation = ToLocation(shipment.Delivery),
            Consignor = Party(shipment, PartyRole.Consignor).Select(ToTradeParty).FirstOrDefault(),
            ConsignorProvidedInformationText = Present(shipment.SenderInstructions) is { } instructions
                ? [.. instructions.Chunk(InformationTextLength).Select(chunk => new string(chunk))]
                : null,
            DeclaredValueForCarriageAmount = shipment.DeclaredValue is { } declared ? new EftiAmount(declared, currency) : null,
            DeliveryEvent = DeliveryEvent(shipment),
            GrossVolume = shipment.TotalVolumeM3 is > 0 ? [new EftiMeasure(shipment.TotalVolumeM3.Value, EftiCodes.CubicMetre)] : null,
            GrossWeight = shipment.TotalGrossWeightKg is > 0 ? [new EftiMeasure(shipment.TotalGrossWeightKg.Value, EftiCodes.Kilogram)] : null,
            IncludedConsignmentItem = NullIfEmpty([.. items.Select(ToItem)]),
            MainCarriageTransportMovement = [Movement(shipment)],
            NatureIdentificationCargo = NullIfEmpty([.. items.Select(i => Present(i.Description)).OfType<string>().Distinct()]) is { } descriptions
                ? new EftiTransportCargo { IdentificationText = descriptions }
                : null,
            NumberOfPackages = shipment.TotalPackages is > 0 ? shipment.TotalPackages : null,
            PaymentArrangementCode = PaymentCode(shipment),
            TransportContractDocument = Present(shipment.CmrNumber) is { } cmrNumber
                ? new EftiReferencedDocument { Id = cmrNumber, TypeCode = EftiCodes.RoadConsignmentNote }
                : null,
            TransportPackage = NullIfEmpty([.. items.Select(ToPackage).OfType<EftiLogisticsPackage>()]),
            TransshipmentPermittedIndicator = shipment.TransshipmentAllowed,
            UsedTransportEquipment = Present(shipment.TrailerPlateNumber) is { } trailerPlate
                ? [new EftiTransportEquipment
                {
                    CategoryCode = EftiCodes.SemiTrailerCategory,
                    Id = trailerPlate,
                    RegistrationCountry = ToCountry(shipment.TrailerCountryCode),
                    SequenceNumber = 1
                }]
                : null
        };
    }

    private static IEnumerable<Party> Party(Shipment shipment, PartyRole role) =>
        shipment.Parties.Where(p => p.Role == role).OrderBy(p => p.Sequence).Select(p => p.Party);

    private static EftiTradeParty ToTradeParty(Party party) => new()
    {
        Name = One(party.Name),
        PostalAddress = ToAddress(party.Street, party.PostalCode, party.City, party.CountryCode),
        TaxRegistration = Present(party.VatNumber) is { } vatNumber ? [new EftiTaxRegistration(vatNumber)] : null
    };

    private static EftiLogisticsLocation? ToLocation(Location location) =>
        ToAddress(location.Street, location.PostalCode, location.City, location.CountryCode) is { } address
            ? new EftiLogisticsLocation { PostalAddress = address }
            : null;

    private static EftiTradeAddress? ToAddress(string? street, string? postalCode, string? city, string? countryCode)
    {
        var address = new EftiTradeAddress
        {
            CityName = One(city),
            CountryCode = Present(countryCode),
            Postcode = One(postalCode),
            StreetName = One(street)
        };

        return address == new EftiTradeAddress() ? null : address;
    }

    /// <summary>Presa in carico effettiva, con ora e fuso; finché la merce non è ritirata vale la data prevista.</summary>
    private static EftiDateTime? AcceptanceDateTime(Shipment shipment) =>
        shipment.PickedUpAt is { } pickedUpAt ? ToDateTime(pickedUpAt)
        : shipment.PickupDate is { } pickupDate ? ToDate(pickupDate)
        : null;

    private static EftiTransportEvent? DeliveryEvent(Shipment shipment) =>
        shipment.DeliveryDate is null && shipment.DeliveredAt is null
            ? null
            : new EftiTransportEvent
            {
                ActualOccurrenceDateTime = shipment.DeliveredAt is { } deliveredAt ? ToDateTime(deliveredAt) : null,
                ScheduledOccurrenceDateTime = shipment.DeliveryDate is { } deliveryDate ? ToDate(deliveryDate) : null
            };

    private static EftiTransportMovement Movement(Shipment shipment) => new()
    {
        DangerousGoodsIndicator = shipment.HasDangerousGoods,
        ModeCode = EftiCodes.RoadTransportMode,
        UsedTransportMeans = Present(shipment.VehiclePlateNumber) is { } plate
            ? new EftiTransportMeans { Id = plate, RegistrationCountry = ToCountry(shipment.VehicleCountryCode) }
            : null
    };

    /// <summary>Una riga per ogni riga dell'ordine, anche vuota: nello schema la riga porta solo volume e merci pericolose.</summary>
    private static EftiConsignmentItem ToItem(ConsignmentItem item) => new()
    {
        GrossVolume = item.VolumeM3 is > 0 ? [new EftiMeasure(item.VolumeM3.Value, EftiCodes.CubicMetre)] : null,
        TransportDangerousGoods = item.IsDangerousGoods
            ? [new EftiDangerousGoods
            {
                DangerousGoodsLogisticsPackage = ToPackage(item) is { } package ? [package] : null,
                GrossWeight = item.GrossWeightKg is > 0 ? new EftiMeasure(item.GrossWeightKg.Value, EftiCodes.Kilogram) : null,
                HazardClassificationID = Present(item.AdrClass),
                PackagingDangerLevelCode = EftiCodes.PackagingDangerLevelCode(item.PackingGroup),
                ProperShippingName = One(item.ProperShippingName),
                UnDgId = Present(item.UnNumber)
            }]
            : null
    };

    private static EftiLogisticsPackage? ToPackage(ConsignmentItem item) =>
        item.PackageType is null && item.PackageCount is not > 0
            ? null
            : new EftiLogisticsPackage
            {
                ItemQuantity = item.PackageCount is > 0 ? item.PackageCount : null,
                TypeCode = item.PackageType is { } type ? [EftiCodes.PackageTypeCode(type)] : null
            };

    private static string? PaymentCode(Shipment shipment) =>
        shipment.FreightPayment is { } payment ? EftiCodes.PaymentArrangementCode(payment) : null;

    private static EftiTradeCountry? ToCountry(string? countryCode) => Present(countryCode) is { } code ? new EftiTradeCountry(code) : null;

    private static EftiDateTime ToDate(DateOnly date) =>
        new(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture), EftiCodes.DateFormat);

    /// <summary>Formato 205: <c>CCYYMMDDHHMM</c> seguito dallo scostamento <c>±HHMM</c>.</summary>
    private static EftiDateTime ToDateTime(DateTimeOffset value)
    {
        string offset = (value.Offset < TimeSpan.Zero ? "-" : "+") + value.Offset.ToString("hhmm", CultureInfo.InvariantCulture);

        return new EftiDateTime(value.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + offset, EftiCodes.DateTimeWithOffsetFormat);
    }

    private static IReadOnlyList<string>? One(string? value) => Present(value) is { } text ? [text] : null;

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values) => values.Count == 0 ? null : values;

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

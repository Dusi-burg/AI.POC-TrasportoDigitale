using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Ecmr;

/// <summary>Codici scritti nell'eCMR, verificati sulle liste dello schema D25A (<c>data/ecmr-schema</c>).</summary>
public static class EcmrCodes
{
    /// <summary>UN/EDIFACT 1001: <c>730</c> = lettera di vettura stradale.</summary>
    public const string RoadConsignmentNote = "730";

    /// <summary>UN/EDIFACT 2379: data senza ora, <c>CCYYMMDD</c>.</summary>
    public const string DateFormat = "102";

    /// <summary>L'unico regolamento sulle merci pericolose ammesso dallo schema.</summary>
    public const string AdrRegulation = "ADR";

    /// <summary>UN/EDIFACT 4237 nella lista dell'eCMR: <c>P</c> = pagato in partenza, <c>C</c> = da incassare a destino.</summary>
    public static string PaymentArrangementCode(FreightPayment payment) => payment switch
    {
        FreightPayment.Prepaid => "P",
        FreightPayment.Collect => "C",
        _ => throw new ArgumentOutOfRangeException(nameof(payment), payment, "Pagamento del nolo senza codice eCMR.")
    };

    /// <summary>UN/EDIFACT 3035, ruolo di chi paga il nolo: <c>CZ</c> = mittente, <c>CN</c> = destinatario.</summary>
    public static string ChargePayingPartyRoleCode(FreightPayment payment) => payment == FreightPayment.Collect ? "CN" : "CZ";
}

/// <summary>
/// Spedizione → documento eCMR, con i nomi degli elementi dello schema UN/CEFACT. Funzione pura, parallela a
/// <see cref="EftiDatasetMapper"/>: non corregge e non inventa. Un dato mancante resta fuori dal documento, un dato sbagliato
/// ci entra così com'è; in entrambi i casi lo dicono le regole.
/// </summary>
public static class EcmrMapper
{
    /// <summary>
    /// Dichiarazione di assoggettamento alla convenzione (art. 6, par. 1, lett. k): testo fisso, non un dato della spedizione.
    /// Si scrive solo sui trasporti internazionali: a un trasporto nazionale la convenzione non si applica.
    /// </summary>
    public const string CmrStatement =
        "This carriage is subject, notwithstanding any clause to the contrary, to the Convention on the Contract for the " +
        "International Carriage of Goods by Road (CMR).";

    /// <summary>Lo schema non ha un elemento per il valore dichiarato (art. 24): viaggia come nota del documento.</summary>
    public const string DeclaredValueSubject = "Declared value for carriage (CMR art. 24)";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Il JSON finisce in una colonna nvarchar e in pagine web: i caratteri accentati restano leggibili.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(JsonObject document) => document.ToJsonString(JsonOptions);

    public static JsonObject Map(Shipment shipment)
    {
        string currency = Present(shipment.CurrencyCode) ?? "EUR";
        ConsignmentItem[] items = [.. shipment.Items.OrderBy(i => i.SequenceNumber)];

        return Object(
            ("ExchangedDocument", Object(
                ("ID", Text(shipment.CmrNumber)),
                ("TypeCode", EcmrCodes.RoadConsignmentNote),
                ("IssueDateTime", Date(shipment.CmrIssueDate)),
                ("Remarks", IsInternational(shipment) ? new JsonArray(CmrStatement) : null),
                ("IncludedNote", shipment.DeclaredValue is { } declared
                    ? new JsonArray(Object(("Subject", DeclaredValueSubject), ("Content", string.Create(CultureInfo.InvariantCulture, $"{declared:0.00} {currency}"))))
                    : null),
                ("IssueLogisticsLocation", Object(("Name", Text(shipment.CmrIssuePlace)))))),
            ("SpecifiedSupplyChainConsignment", Object(
                ("ID", shipment.Code),
                ("GrossWeightMeasure", Measure(shipment.TotalGrossWeightKg, EftiCodes.Kilogram)),
                ("GrossVolumeMeasure", Measure(shipment.TotalVolumeM3, EftiCodes.CubicMetre)),
                ("CODAmount", Amount(shipment.CodAmount, currency)),
                ("ConsignmentItemQuantity", items.Length > 0 ? items.Length : null),
                ("TransshipmentPermissionIndicator", shipment.TransshipmentAllowed is { } allowed ? Object(("Indicator", allowed)) : null),
                ("ConsignorTradeParty", Parties(shipment, PartyRole.Consignor).FirstOrDefault()),
                ("ConsigneeTradeParty", Parties(shipment, PartyRole.Consignee).FirstOrDefault()),
                ("CarrierTradeParty", Parties(shipment, PartyRole.Carrier).FirstOrDefault()),
                ("ConnectingCarrierTradeParty", Array(Parties(shipment, PartyRole.ConnectingCarrier))),
                ("CarrierAcceptanceLogisticsLocation", Place(shipment.Pickup)),
                ("ConsigneeReceiptLogisticsLocation", Place(shipment.Delivery)),
                ("AssociatedReferencedDocument", Array(shipment.AccompanyingDocuments
                    .OrderBy(d => d.Sequence)
                    .Select(d => Object(("TypeCode", Text(d.TypeCode)), ("ID", Text(d.Number)))))),
                ("HandlingInstructions", Instructions(shipment.SenderInstructions)),
                ("DeliveryTransportEvent", Event(shipment.DeliveredAt, shipment.DeliveryDate)),
                ("PickUpTransportEvent", Event(shipment.PickedUpAt, shipment.PickupDate)),
                ("IncludedSupplyChainConsignmentItem", Array(items.Select(Item))),
                ("UtilizedLogisticsTransportEquipment", Present(shipment.TrailerPlateNumber) is { } trailer
                    ? new JsonArray(Object(("ID", trailer), ("CategoryCode", EftiCodes.SemiTrailerCategory)))
                    : null),
                ("MainCarriageLogisticsTransportMovement", new JsonArray(Movement(shipment))),
                ("ApplicableLogisticsServiceCharge", ServiceCharge(shipment, currency)),
                ("TransportLogisticsPackage", shipment.TotalPackages is > 0
                    ? new JsonArray(Object(("ItemQuantity", shipment.TotalPackages)))
                    : null),
                ("ConsignorProvidedBorderClearanceTransportInstructions", Instructions(shipment.CustomsInstructions)))))!;
    }

    /// <summary>Paesi di presa in carico e di consegna entrambi indicati e diversi.</summary>
    public static bool IsInternational(Shipment shipment) =>
        Present(shipment.Pickup.CountryCode) is { } pickup
        && Present(shipment.Delivery.CountryCode) is { } delivery
        && pickup != delivery;

    private static IEnumerable<JsonObject?> Parties(Shipment shipment, PartyRole role) =>
        shipment.Parties.Where(p => p.Role == role).OrderBy(p => p.Sequence).Select(p => Object(
            ("Name", Present(p.Party.Name) is { } name ? new JsonArray(name) : null),
            ("PostalTradeAddress", Address(p.Party.Street, p.Party.PostalCode, p.Party.City, p.Party.CountryCode)),
            ("SpecifiedTaxRegistration", Present(p.Party.VatNumber) is { } vatNumber ? new JsonArray(Object(("ID", vatNumber))) : null)));

    private static JsonObject? Place(Location location) =>
        Object(("PostalTradeAddress", Address(location.Street, location.PostalCode, location.City, location.CountryCode)));

    private static JsonObject? Address(string? street, string? postalCode, string? city, string? countryCode) => Object(
        ("PostcodeCode", Text(postalCode)),
        ("StreetName", Text(street)),
        ("CityName", Present(city) is { } cityName ? new JsonArray(cityName) : null),
        ("CountryID", Text(countryCode)));

    /// <summary>Momento effettivo con ora e fuso; finché non c'è, la data prevista o pattuita.</summary>
    private static JsonObject? Event(DateTimeOffset? actual, DateOnly? scheduled) => Object(
        ("ActualOccurrenceDateTime", actual is { } moment
            ? Object(("DateTime", moment.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)))
            : null),
        ("ScheduledOccurrenceDateTime", Date(scheduled)));

    private static JsonObject? Item(ConsignmentItem item) => Object(
        ("SequenceNumeric", item.SequenceNumber),
        ("GrossWeightMeasure", Measure(item.GrossWeightKg, EftiCodes.Kilogram)),
        ("GrossVolumeMeasure", Measure(item.VolumeM3, EftiCodes.CubicMetre)),
        ("NatureIdentificationTransportCargo", Present(item.Description) is { } description
            ? Object(("Identification", new JsonArray(description)))
            : null),
        // La classe ADR non ha un elemento nello schema D25A: resta sulla spedizione e nel dataset eFTI.
        ("ApplicableTransportDangerousGoods", item.IsDangerousGoods
            ? new JsonArray(Object(
                ("UNDGIdentificationCode", Text(item.UnNumber)),
                ("RegulationCode", EcmrCodes.AdrRegulation),
                ("PackagingDangerLevelCode", EftiCodes.PackagingDangerLevelCode(item.PackingGroup)),
                ("ProperShippingName", Text(item.ProperShippingName))))
            : null),
        ("TransportLogisticsPackage", Object(
            ("ItemQuantity", item.PackageCount is > 0 ? item.PackageCount : null),
            ("TypeCode", item.PackageType is { } type ? new JsonArray(EftiCodes.PackageTypeCode(type)) : null),
            ("PhysicalLogisticsShippingMarks", Present(item.Marks) is { } marks ? new JsonArray(Object(("Marking", marks))) : null)) is { } package
            ? new JsonArray(package)
            : null));

    private static JsonObject? Movement(Shipment shipment) => Object(
        ("ModeCode", EftiCodes.RoadTransportMode),
        ("UsedLogisticsTransportMeans", Present(shipment.VehiclePlateNumber) is { } plate
            ? Object(
                ("TypeCode", Text(shipment.VehicleTypeCode)),
                ("ID", new JsonArray(plate)),
                ("RegistrationTradeCountry", Object(("ID", Text(shipment.VehicleCountryCode)))))
            : null));

    private static JsonArray? ServiceCharge(Shipment shipment, string currency) =>
        shipment.FreightAmount is null && shipment.FreightPayment is null
            ? null
            : new JsonArray(Object(
                ("PaymentArrangementCode", shipment.FreightPayment is { } payment ? EcmrCodes.PaymentArrangementCode(payment) : null),
                ("AppliedAmount", Amount(shipment.FreightAmount, currency)),
                ("PayingPartyRoleCode", shipment.FreightPayment is { } payer ? EcmrCodes.ChargePayingPartyRoleCode(payer) : null)));

    private static JsonArray? Instructions(string? text) =>
        Present(text) is { } description ? new JsonArray(Object(("Description", description))) : null;

    private static JsonObject? Date(DateOnly? date) => date is { } day
        ? Object(("DateTimeString", Object(
            (EcmrXmlExporter.ValueProperty, day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            ("format", EcmrCodes.DateFormat))))
        : null;

    private static JsonObject? Measure(decimal? value, string unitCode) =>
        value is > 0 ? Object((EcmrXmlExporter.ValueProperty, value), ("unitCode", unitCode)) : null;

    private static JsonObject? Amount(decimal? value, string currency) =>
        value is { } amount ? Object((EcmrXmlExporter.ValueProperty, amount), ("currencyID", currency)) : null;

    /// <summary>Oggetto con le sole proprietà valorizzate; <c>null</c> se non ne resta nessuna.</summary>
    private static JsonObject? Object(params (string Name, JsonNode? Value)[] properties)
    {
        var json = new JsonObject();

        foreach ((string name, JsonNode? value) in properties)
        {
            if (value is not null)
            {
                json.Add(name, value);
            }
        }

        return json.Count == 0 ? null : json;
    }

    private static JsonArray? Array(IEnumerable<JsonObject?> items)
    {
        JsonNode[] nodes = [.. items.OfType<JsonObject>()];

        return nodes.Length == 0 ? null : new JsonArray(nodes);
    }

    private static JsonNode? Text(string? value) => Present(value) is { } text ? JsonValue.Create(text) : null;

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

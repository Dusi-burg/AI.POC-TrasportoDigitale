using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;

// Viste del TMS, esposte da GET /api/views su Tms.Api in sola lettura. Il dettaglio dell'ordine è anche il dato che Efti.Api
// legge nell'importazione (P2): porta il contenuto del TMS così com'è, lacune ed errori compresi.

public sealed record PartyView(string? Name, string? VatNumber, string? Street, string? PostalCode, string? City, string? CountryCode);

public sealed record PlaceView(string? Street, string? PostalCode, string? City, string? CountryCode);

public sealed record TransportOrderSummaryView(
    string Code,
    string CustomerCode,
    string CustomerName,
    TransportOrderStatus Status,
    DateOnly OrderDate,
    string? ConsignorName,
    string? PickupCity,
    string? PickupCountryCode,
    string? ConsigneeName,
    string? DeliveryCity,
    string? DeliveryCountryCode,
    DateOnly? PickupDate,
    DateOnly? DeliveryDate,
    int TotalPackages,
    decimal TotalGrossWeightKg,
    bool HasDangerousGoods,
    string? TripCode);

public sealed record TransportOrderLineView(
    int LineNumber,
    string? Description,
    int PackageCount,
    PackageType PackageType,
    decimal GrossWeightKg,
    decimal? VolumeM3,
    bool IsDangerousGoods,
    string? UnNumber,
    string? ProperShippingName,
    string? AdrClass,
    string? PackingGroup,
    string? Marks = null);

/// <summary>Documento consegnato al vettore insieme alla merce.</summary>
/// <param name="TypeCode">Tipo di documento, codice UN/EDIFACT 1001 (<c>380</c> fattura, <c>271</c> lista colli, <c>270</c> documento di trasporto).</param>
public sealed record OrderDocumentView(string TypeCode, string Number);

public sealed record VehicleView(string PlateNumber, string CountryCode, VehicleType Type);

/// <param name="Subcontractor">Vettore successivo, se una tratta è affidata a terzi.</param>
public sealed record TripView(
    string Code, DateOnly DepartureDate, VehicleView Vehicle, VehicleView? Trailer, string DriverCode, PartyView? Subcontractor);

public sealed record TransportOrderDetailView(
    TransportOrderSummaryView Order,
    PartyView Consignor,
    PartyView Consignee,
    PlaceView Pickup,
    PlaceView Delivery,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    string? CmrNumber,
    decimal? FreightAmount,
    FreightPayment FreightPayment,
    string CurrencyCode,
    decimal? CodAmount,
    decimal? DeclaredValue,
    bool TransshipmentAllowed,
    string? SenderInstructions,
    TripView? Trip,
    IReadOnlyList<TransportOrderLineView> Lines,
    string? CmrIssuePlace = null,
    DateOnly? CmrIssueDate = null,
    string? CustomsInstructions = null,
    IReadOnlyList<OrderDocumentView>? Documents = null);

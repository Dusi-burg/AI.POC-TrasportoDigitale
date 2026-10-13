using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

// Contratti di Efti.Api per l'importazione dal TMS e per le viste delle spedizioni.

/// <param name="OrderCode">Codice dell'ordine del TMS da importare; <c>null</c> = tutti gli ordini non annullati.</param>
public sealed record ImportShipmentsRequest(string? OrderCode);

public sealed record ImportedShipment(string OrderCode, string ShipmentCode, DatasetStatus Status, int Errors, int Warnings);

/// <param name="Imported">Spedizioni create da questa chiamata.</param>
/// <param name="AlreadyImported">Ordini già importati in precedenza: non vengono toccati.</param>
/// <param name="Skipped">Ordini annullati, che non diventano spedizioni.</param>
public sealed record ImportShipmentsResponse(
    IReadOnlyList<ImportedShipment> Imported, IReadOnlyList<string> AlreadyImported, IReadOnlyList<string> Skipped);

/// <param name="Status">Stato dell'ultima versione del dataset.</param>
public sealed record ShipmentSummaryView(
    string Code,
    string TmsOrderCode,
    DatasetStatus Status,
    int DatasetVersion,
    int Errors,
    int Warnings,
    string? ConsignorName,
    string? ConsigneeName,
    string? PickupCity,
    string? PickupCountryCode,
    string? DeliveryCity,
    string? DeliveryCountryCode,
    DateOnly? PickupDate,
    DateOnly? DeliveryDate,
    bool HasDangerousGoods,
    string? VehiclePlateNumber,
    DateTimeOffset ImportedAt);

public sealed record LocationView(string? Street, string? PostalCode, string? City, string? CountryCode);

public sealed record ShipmentPartyView(
    PartyRole Role, int Sequence, string? Name, string? VatNumber, string? Street, string? PostalCode, string? City, string? CountryCode);

public sealed record ConsignmentItemView(
    int SequenceNumber,
    string? Description,
    int? PackageCount,
    PackageType? PackageType,
    decimal? GrossWeightKg,
    decimal? VolumeM3,
    bool IsDangerousGoods,
    string? UnNumber,
    string? ProperShippingName,
    string? AdrClass,
    string? PackingGroup);

/// <param name="FieldPath">Percorso dell'elemento nel dataset eFTI.</param>
public sealed record ValidationIssueView(string RuleCode, IssueSeverity Severity, string? FieldPath, string Message);

/// <param name="Json">Il dataset con i nomi degli elementi dello schema eFTI.</param>
public sealed record EftiDatasetView(int Version, DatasetStatus Status, string SchemaVersion, DateTimeOffset CreatedAt, JsonElement Json);

public sealed record ShipmentDetailView(
    ShipmentSummaryView Shipment,
    Guid PublicId,
    LocationView Pickup,
    LocationView Delivery,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    int? TotalPackages,
    decimal? TotalGrossWeightKg,
    decimal? TotalVolumeM3,
    string? VehicleCountryCode,
    string? TrailerPlateNumber,
    string? TrailerCountryCode,
    string? CmrNumber,
    decimal? FreightAmount,
    FreightPayment? FreightPayment,
    string? CurrencyCode,
    decimal? CodAmount,
    decimal? DeclaredValue,
    bool? TransshipmentAllowed,
    string? SenderInstructions,
    IReadOnlyList<ShipmentPartyView> Parties,
    IReadOnlyList<ConsignmentItemView> Items,
    EftiDatasetView Dataset,
    IReadOnlyList<ValidationIssueView> Issues);

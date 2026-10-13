using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Tools;

// Contratti dei tool di efti-mcp (PLAN §5, Fase 5): nomi e output strutturato. Sono normativi: un tool nuovo va prima
// aggiunto al piano.

public static class EftiToolNames
{
    public const string SearchShipments = "search_shipments";
    public const string GetShipmentDataset = "get_shipment_dataset";
    public const string ListValidationIssues = "list_validation_issues";
    public const string GetTmsOrder = "get_tms_order";
    public const string ProposeFieldCompletion = "propose_field_completion";
    public const string FindConsignmentsByVehicle = "find_consignments_by_vehicle";
    public const string GetAuthoritySubset = "get_authority_subset";
}

/// <param name="Shipments">Dalla più recente; al più 200.</param>
public sealed record SearchShipmentsResult(IReadOnlyList<ShipmentSummaryView> Shipments);

/// <param name="Dataset">Il dataset con i nomi degli elementi dello schema eFTI.</param>
public sealed record ShipmentDatasetResult(
    string ShipmentCode, int Version, DatasetStatus Status, string SchemaVersion, DateTimeOffset CreatedAt, JsonElement Dataset);

public sealed record ValidationIssuesResult(
    string ShipmentCode, int DatasetVersion, DatasetStatus Status, IReadOnlyList<ValidationIssueView> Issues);

public sealed record TmsOrderResult(string ShipmentCode, TransportOrderDetailView Order);

/// <param name="Created"><c>false</c> se la stessa proposta era già in attesa.</param>
/// <param name="CurrentValue">Valore del campo al momento della proposta; <c>null</c> se mancava.</param>
public sealed record FieldProposalResult(
    int ProposalId, bool Created, ProposalStatus Status, string ShipmentCode, string FieldPath, string ProposedValue, string? CurrentValue);

public sealed record ConsignmentsByVehicleResult(IReadOnlyList<ConsignmentIdentifiersView> Consignments);

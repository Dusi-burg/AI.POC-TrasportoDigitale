using System.Xml.Linq;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Efti.Api.Shipments;

/// <summary>Importazione dal TMS, viste delle spedizioni ed esportazione XML del dataset.</summary>
internal static class ShipmentEndpoints
{
    /// <summary>Stato del dataset esportato: l'XML si scarica anche se incompleto, ma chi lo riceve deve saperlo.</summary>
    public const string DatasetStatusHeader = "X-Efti-Dataset-Status";

    public static RouteGroupBuilder MapShipmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/shipments/import", ImportAsync)
            .RequireOperation(Operation.ShipmentImport);

        api.MapGet("/shipments/{code}/dataset.xml", ExportDatasetAsync)
            .RequireOperation(Operation.DatasetExport, http => http.RouteValue("code"));

        api.MapGet("/views/shipments", ListAsync)
            .RequireOperation(Operation.ShipmentList);

        api.MapGet("/views/shipments/{code}", GetAsync)
            .RequireOperation(Operation.ShipmentRead, http => http.RouteValue("code"));

        return api;
    }

    /// <summary>
    /// Il corpo è facoltativo: senza corpo (o senza <c>orderCode</c>) si importano tutti gli ordini. Si legge a mano perché il
    /// binding delle Minimal API rifiuterebbe con 415 una POST senza corpo e senza Content-Type.
    /// </summary>
    private static async Task<Results<Ok<ImportShipmentsResponse>, ProblemHttpResult>> ImportAsync(
        HttpRequest http, ShipmentImportService service, AuditNote note, CancellationToken cancellationToken)
    {
        try
        {
            ImportShipmentsRequest? request = http.HasJsonContentType() && http.ContentLength is not 0
                ? await http.ReadFromJsonAsync<ImportShipmentsRequest>(cancellationToken)
                : null;
            string? orderCode = string.IsNullOrWhiteSpace(request?.OrderCode) ? null : request.OrderCode.Trim();

            ImportShipmentsResponse response = orderCode is null
                ? await service.ImportAllAsync(cancellationToken)
                : await service.ImportOneAsync(orderCode, cancellationToken);

            note.ShipmentCode = orderCode is null ? null : response.Imported.FirstOrDefault()?.ShipmentCode;
            note.Detail = $"{(orderCode is null ? "tutti gli ordini" : orderCode)}: {response.Imported.Count} importate, " +
                $"{response.AlreadyImported.Count} già presenti, {response.Skipped.Count} annullate";

            return TypedResults.Ok(response);
        }
        catch (TransportOrderNotFoundException exception)
        {
            return ApiProblems.NotFound(exception.Message);
        }
        catch (TransportOrderNotImportableException exception)
        {
            return ApiProblems.Conflict(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return ApiProblems.UpstreamUnavailable($"Il TMS non risponde: {exception.Message}");
        }
    }

    private static async Task<Results<Ok<IReadOnlyList<ShipmentSummaryView>>, ProblemHttpResult>> ListAsync(
        string? status, ShipmentViewQueries queries, CancellationToken cancellationToken)
    {
        DatasetStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out DatasetStatus parsed) || !Enum.IsDefined(parsed) || int.TryParse(status, out _))
            {
                return ApiProblems.Validation($"Filtro status non valido: ammessi {string.Join(", ", Enum.GetNames<DatasetStatus>())}.");
            }

            statusFilter = parsed;
        }

        return TypedResults.Ok(await queries.ListAsync(statusFilter, cancellationToken));
    }

    private static async Task<Results<Ok<ShipmentDetailView>, ProblemHttpResult>> GetAsync(
        string code, ShipmentViewQueries queries, CancellationToken cancellationToken)
    {
        ShipmentDetailView? shipment = await queries.FindAsync(code, cancellationToken);

        return shipment is null ? ApiProblems.NotFound($"Spedizione {code} non trovata.") : TypedResults.Ok(shipment);
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> ExportDatasetAsync(
        string code, ShipmentViewQueries queries, EftiXmlExporter exporter, HttpContext http, CancellationToken cancellationToken)
    {
        StoredDataset? dataset = await queries.FindDatasetAsync(code, cancellationToken);

        if (dataset is null)
        {
            return ApiProblems.NotFound($"Spedizione {code} non trovata.");
        }

        XDocument document = exporter.Export(dataset.Json);

        http.Response.Headers[DatasetStatusHeader] = dataset.Status.ToString();

        return TypedResults.Content($"{document.Declaration}{Environment.NewLine}{document}", "application/xml");
    }
}

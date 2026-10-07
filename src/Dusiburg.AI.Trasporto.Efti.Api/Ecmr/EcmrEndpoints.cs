using System.Xml.Linq;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Efti.Api.Ecmr;

/// <summary>Il documento eCMR di una spedizione: lettura in JSON ed esportazione nell'XML UN/CEFACT.</summary>
internal static class EcmrEndpoints
{
    /// <summary>Stato del documento esportato: l'XML si scarica anche in bozza, ma chi lo riceve deve saperlo.</summary>
    public const string EcmrStatusHeader = "X-Ecmr-Status";

    public static RouteGroupBuilder MapEcmrEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/shipments/{code}/ecmr", GetAsync)
            .RequireOperation(Operation.EcmrRead, http => http.RouteValue("code"));

        api.MapGet("/shipments/{code}/ecmr.xml", ExportAsync)
            .RequireOperation(Operation.EcmrExport, http => http.RouteValue("code"));

        return api;
    }

    private static async Task<Results<Ok<EcmrDocumentView>, ProblemHttpResult>> GetAsync(
        string code, EcmrQueries queries, AuditNote note, CancellationToken cancellationToken)
    {
        EcmrDocumentView? document = await queries.FindAsync(code, cancellationToken);

        if (document is null)
        {
            return ApiProblems.NotFound($"Spedizione {code} non trovata.");
        }

        note.ShipmentCode = code;

        return TypedResults.Ok(document);
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> ExportAsync(
        string code, EcmrQueries queries, EcmrXmlExporter exporter, AuditNote note, HttpContext http, CancellationToken cancellationToken)
    {
        StoredEcmr? stored = await queries.FindStoredAsync(code, cancellationToken);

        if (stored is null)
        {
            return ApiProblems.NotFound($"Spedizione {code} non trovata.");
        }

        XDocument document = exporter.Export(stored.Json);

        note.ShipmentCode = code;
        note.Detail = $"eCMR v{stored.Version} {stored.Status}";
        http.Response.Headers[EcmrStatusHeader] = stored.Status.ToString();

        return TypedResults.Content($"{document.Declaration}{Environment.NewLine}{document}", "application/xml");
    }
}

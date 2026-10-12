using System.Globalization;
using Dusiburg.AI.Trasporto.Efti.Api.Proposals;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Efti.Api.Documents;

/// <summary>
/// Documenti di una spedizione, campi estratti e coda di revisione. Caricare un documento avvia l'estrazione, che può lasciare
/// proposte e voci di revisione ma non tocca il dataset.
/// </summary>
internal static class DocumentEndpoints
{
    /// <summary>Nome del campo del modulo multipart che porta il file.</summary>
    public const string FileField = "file";

    /// <summary>Si caricano solo PDF: è il tipo con cui il file torna a chi lo chiede.</summary>
    public const string PdfContentType = "application/pdf";

    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        // Il caricamento arriva da un client che porta la chiave di ruolo nell'intestazione, non da un modulo del browser con
        // un cookie di sessione: il token antiforgery non ha nulla da proteggere.
        api.MapPost("/shipments/{code}/documents", UploadAsync)
            .DisableAntiforgery()
            .RequireOperation(Operation.DocumentUpload, http => http.RouteValue("code"));

        api.MapGet("/shipments/{code}/documents", ListAsync)
            .RequireOperation(Operation.DocumentList, http => http.RouteValue("code"));

        api.MapGet("/documents/{id}", GetAsync)
            .RequireOperation(Operation.DocumentRead, http => http.RouteValue("id"));

        api.MapGet("/documents/{id}/file", GetFileAsync)
            .RequireOperation(Operation.DocumentDownload, http => http.RouteValue("id"));

        api.MapGet("/review-tasks", ListReviewTasksAsync)
            .RequireOperation(Operation.ReviewQueueRead);

        api.MapPost("/review-tasks/{id}/decision", DecideReviewAsync)
            .RequireOperation(Operation.ReviewDecide, http => http.RouteValue("id"));

        return api;
    }

    /// <summary>
    /// Il modulo si legge a mano: così un caricamento senza file è un errore di validazione con un messaggio chiaro, e non un
    /// rifiuto generico del binding.
    /// </summary>
    private static async Task<Results<Ok<DocumentDetailView>, ProblemHttpResult>> UploadAsync(
        string code, HttpRequest request, DocumentService service, AuditNote note, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType || (await request.ReadFormAsync(cancellationToken)).Files.GetFile(FileField) is not { } file)
        {
            return ApiProblems.Validation($"Caricare il PDF come multipart/form-data, nel campo '{FileField}'.");
        }

        if (file.Length > DocumentService.MaxFileBytes)
        {
            return ApiProblems.Validation($"Il file supera i {DocumentService.MaxFileBytes / (1024 * 1024)} MB.");
        }

        using var buffer = new MemoryStream();

        await file.CopyToAsync(buffer, cancellationToken);

        try
        {
            DocumentDetailView document = await service.UploadAsync(code, file.FileName, buffer.ToArray(), AccessFilter.ActorOf(request.HttpContext), cancellationToken);

            note.ShipmentCode = code;
            note.Detail = string.Create(CultureInfo.InvariantCulture,
                $"documento {document.Document.Id}: {document.Document.Fields} campi, {document.Document.Proposed} proposte, {document.Document.InReview} in revisione");

            return TypedResults.Ok(document);
        }
        catch (ProposalException exception)
        {
            return ToProblem(exception);
        }
        catch (ExtractionFailedException exception)
        {
            return ApiProblems.UpstreamUnavailable(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return ApiProblems.UpstreamUnavailable($"Il modello di estrazione non risponde: {exception.Message}");
        }
    }

    private static async Task<Results<Ok<IReadOnlyList<DocumentSummaryView>>, ProblemHttpResult>> ListAsync(
        string code, DocumentService service, AuditNote note, CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentSummaryView>? documents = await service.ListAsync(code, cancellationToken);

        if (documents is null)
        {
            return ApiProblems.NotFound($"Spedizione {code} non trovata.");
        }

        note.ShipmentCode = code;

        return TypedResults.Ok(documents);
    }

    private static async Task<Results<Ok<DocumentDetailView>, ProblemHttpResult>> GetAsync(
        int id, DocumentService service, AuditNote note, CancellationToken cancellationToken)
    {
        DocumentDetailView? document = await service.FindAsync(id, cancellationToken);

        if (document is null)
        {
            return ApiProblems.NotFound($"Documento {id} non trovato.");
        }

        note.ShipmentCode = document.Document.ShipmentCode;

        return TypedResults.Ok(document);
    }

    /// <summary>
    /// Il PDF così com'è stato caricato: chi conferma un campo in revisione deve poter guardare il documento vero, non solo il
    /// testo che ne è stato letto.
    /// </summary>
    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetFileAsync(
        int id, DocumentService service, AuditNote note, CancellationToken cancellationToken)
    {
        DocumentFile? file = await service.FindFileAsync(id, cancellationToken);

        if (file is null)
        {
            return ApiProblems.NotFound($"Documento {id} non trovato.");
        }

        note.ShipmentCode = file.ShipmentCode;
        note.Detail = Shorten($"{file.FileName}, {file.Content.Length} byte");

        return TypedResults.File(file.Content, PdfContentType, file.FileName);
    }

    private static async Task<Results<Ok<IReadOnlyList<ReviewTaskView>>, ProblemHttpResult>> ListReviewTasksAsync(
        string? status, string? shipmentCode, DocumentService service, CancellationToken cancellationToken)
    {
        ReviewStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out ReviewStatus parsed) || !Enum.IsDefined(parsed) || int.TryParse(status, out _))
            {
                return ApiProblems.Validation($"Filtro status non valido: ammessi {string.Join(", ", Enum.GetNames<ReviewStatus>())}.");
            }

            statusFilter = parsed;
        }

        return TypedResults.Ok(await service.ListReviewTasksAsync(
            statusFilter, string.IsNullOrWhiteSpace(shipmentCode) ? null : shipmentCode.Trim(), cancellationToken));
    }

    private static async Task<Results<Ok<ReviewDecisionResponse>, ProblemHttpResult>> DecideReviewAsync(
        int id, ReviewDecisionRequest request, DocumentService service, AuditNote note, HttpContext http, CancellationToken cancellationToken)
    {
        try
        {
            ReviewDecisionResponse response = await service.DecideReviewAsync(id, request, AccessFilter.ActorOf(http), cancellationToken);

            // La risorsa della richiesta è il numero della voce: qui l'audit impara di quale spedizione si tratta.
            note.ShipmentCode = response.Task.ShipmentCode;
            note.Detail = Shorten(response.Dataset is { } dataset
                ? string.Create(CultureInfo.InvariantCulture, $"{response.Task.Field.FieldPath}: {response.Task.Status}, dataset v{dataset.DatasetVersion} {dataset.DatasetStatus}")
                : $"{response.Task.Field.FieldPath}: {response.Task.Status}");

            return TypedResults.Ok(response);
        }
        catch (ProposalException exception)
        {
            return ToProblem(exception);
        }
    }

    private static ProblemHttpResult ToProblem(ProposalException exception) => exception.Failure switch
    {
        ProposalFailure.NotFound => ApiProblems.NotFound(exception.Message),
        ProposalFailure.Conflict => ApiProblems.Conflict(exception.Message),
        _ => ApiProblems.Validation(exception.Message)
    };

    /// <summary>Lunghezza della colonna <c>AuditEvent.Detail</c>.</summary>
    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200];
}

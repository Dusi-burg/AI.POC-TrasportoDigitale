using System.Text;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Api;
using Dusiburg.AI.Trasporto.Web.Demo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Dusiburg.AI.Trasporto.Web.Pages.Shipments;

/// <summary>
/// Una spedizione: dataset e problemi, agente di mappatura, proposte da decidere, documenti, accessi dell'autorità. La pagina
/// mostra a ogni ruolo ciò che il suo ruolo può leggere, e non chiede all'API ciò che gli verrebbe rifiutato.
/// </summary>
public sealed class DetailsModel(EftiApiClient api, EftiLongRunningClient longRunning, DemoCmrFiles demoCmr, IOptions<DashboardOptions> dashboard) : ApiPageModel
{
    public const int MaxUploadBytes = 2 * 1024 * 1024;

    [BindProperty(SupportsGet = true)]
    public string Code { get; set; } = string.Empty;

    public ShipmentDetailView? Shipment { get; private set; }

    public IReadOnlyList<FieldProposalView> Proposals { get; private set; } = [];

    public IReadOnlyList<DocumentSummaryView> Documents { get; private set; } = [];

    public IReadOnlyList<AuditEventView> AuthorityAccesses { get; private set; } = [];

    /// <summary>I CMR sintetici di questa spedizione, caricabili con un clic.</summary>
    public IReadOnlyList<string> DemoCmrCodes { get; private set; } = [];

    public string? DashboardUrl => dashboard.Value.Url;

    /// <summary>Completare una spedizione è dell'ufficio traffico: agente, decisioni e caricamento dei documenti.</summary>
    public bool CanWork => Role == Role.Dispatcher;

    /// <summary>Proposte, documenti e accessi dell'autorità li leggono operatore e amministratore.</summary>
    public bool CanSeeWork => Role is Role.Dispatcher or Role.Admin;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Shipment = await LoadAsync(() => api.GetShipmentAsync(Code, cancellationToken));

        if (Shipment is null || !CanSeeWork)
        {
            return;
        }

        Proposals = await LoadAsync(() => api.ListProposalsAsync(Code, cancellationToken)) ?? [];
        Documents = await LoadAsync(() => api.ListDocumentsAsync(Code, cancellationToken)) ?? [];
        AuthorityAccesses = await LoadAsync(() => api.ListAuthorityAccessesAsync(Code, cancellationToken)) ?? [];
        DemoCmrCodes = demoCmr.CodesFor(Code);
    }

    /// <summary>Download del dataset in XML eFTI: il file viene dall'API, la Web lo passa al browser.</summary>
    public async Task<IActionResult> OnGetXmlAsync(CancellationToken cancellationToken)
    {
        try
        {
            (string xml, string? status) = await api.GetDatasetXmlAsync(Code, cancellationToken);

            return File(Encoding.UTF8.GetBytes(xml), "application/xml", $"{Code}-efti-{status ?? "dataset"}.xml");
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            ActionError = message;

            return RedirectToPage(new { Code });
        }
    }

    public Task<IActionResult> OnPostDecideAsync(int proposalId, string decision, string? note, CancellationToken cancellationToken) => ActAsync(
        async () =>
        {
            ProposalStatus status = decision == nameof(ProposalStatus.Accepted) ? ProposalStatus.Accepted : ProposalStatus.Rejected;
            ProposalDecisionResponse decided = await api.DecideProposalAsync(proposalId, status, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), cancellationToken);

            return status == ProposalStatus.Accepted
                ? $"Proposta su {decided.Proposal.FieldPath} accettata: dataset alla versione {decided.DatasetVersion}, {Presentation.Label(decided.DatasetStatus).ToLowerInvariant()}" +
                  (decided.PublishedOnGate ? ", pubblicato sul gate." : $", {decided.Errors} errori.")
                : $"Proposta su {decided.Proposal.FieldPath} rifiutata: il dataset non cambia.";
        },
        RedirectToPage(new { Code }));

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            ActionError = "Scegliere il PDF del CMR da caricare.";

            return RedirectToPage(new { Code });
        }

        if (file.Length > MaxUploadBytes)
        {
            ActionError = "Il file supera i 2 MB.";

            return RedirectToPage(new { Code });
        }

        using var buffer = new MemoryStream();

        await file.CopyToAsync(buffer, cancellationToken);

        return await UploadAsync(file.FileName, buffer.ToArray(), cancellationToken);
    }

    /// <summary>Carica uno dei CMR sintetici della spedizione, senza cercarlo sul disco.</summary>
    public async Task<IActionResult> OnPostUploadDemoAsync(string? cmrCode, CancellationToken cancellationToken)
    {
        if (!demoCmr.CodesFor(Code).Contains(cmrCode) || demoCmr.Read(cmrCode) is not { } pdf)
        {
            ActionError = $"Il CMR demo '{cmrCode}' non è di questa spedizione.";

            return RedirectToPage(new { Code });
        }

        return await UploadAsync($"{cmrCode}.pdf", pdf, cancellationToken);
    }

    private async Task<IActionResult> UploadAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            DocumentDetailView document = await longRunning.UploadAsync(Code, fileName, content, cancellationToken);

            Notice = $"{fileName}: {document.Document.Fields} campi estratti, {document.Document.Matching} coincidono con la spedizione, " +
                $"{document.Document.Proposed} proposte, {document.Document.InReview} in revisione.";

            return RedirectToPage("/Documents/Details", new { id = document.Document.Id });
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            ActionError = message;

            return RedirectToPage(new { Code });
        }
    }
}

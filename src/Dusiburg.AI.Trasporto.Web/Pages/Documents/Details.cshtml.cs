using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Mvc;

namespace Dusiburg.AI.Trasporto.Web.Pages.Documents;

/// <summary>
/// Un documento caricato: i campi estratti con i segnali da cui il codice ha calcolato la confidenza, l'esito di ciascuno, le
/// note a mano isolate e il testo che il modello ha letto.
/// </summary>
public sealed class DetailsModel(EftiApiClient api) : ApiPageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public DocumentDetailView? Document { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Document = await LoadAsync(() => api.GetDocumentAsync(Id, cancellationToken));

    /// <summary>
    /// Il PDF originale: il file viene dall'API, la Web lo passa al browser. Senza nome di download il browser lo apre in una
    /// scheda, accanto ai campi da confermare; con <paramref name="download"/> lo salva.
    /// </summary>
    public async Task<IActionResult> OnGetFileAsync(bool download, CancellationToken cancellationToken)
    {
        try
        {
            (byte[] content, string fileName) = await api.GetDocumentFileAsync(Id, cancellationToken);

            return download ? File(content, "application/pdf", fileName) : File(content, "application/pdf");
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            ActionError = message;

            return RedirectToPage(new { Id });
        }
    }
}

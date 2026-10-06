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
}

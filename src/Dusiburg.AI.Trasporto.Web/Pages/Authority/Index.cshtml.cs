using System.Globalization;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Mvc;

namespace Dusiburg.AI.Trasporto.Web.Pages.Authority;

/// <summary>
/// Il controllo su strada: dalla targa a ciò che il mezzo ha a bordo in quel momento, e da un UIL al sottoinsieme che l'autorità
/// può leggere. Sola lettura: ogni consultazione finisce nell'audit e l'operatore la vede sulla spedizione.
/// </summary>
public sealed class IndexModel(EftiApiClient api) : ApiPageModel
{
    /// <summary>I sottoinsiemi dello schema che il ruolo autorità può chiedere.</summary>
    public static readonly IReadOnlyList<string> Subsets = ["EU03", "EU05a"];

    [BindProperty(SupportsGet = true)]
    public string? Plate { get; set; }

    /// <summary>
    /// Momento del controllo, se simulato: aiuto per la demo, i cui dati hanno date fisse. Senza, il controllo è adesso — come
    /// nella realtà.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? At { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Uil { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SubsetId { get; set; }

    /// <summary><c>null</c> finché non si cerca una targa.</summary>
    public IReadOnlyList<ConsignmentIdentifiersView>? Identifiers { get; private set; }

    /// <summary>I momenti da proporre per quella targa; <c>null</c> se la piattaforma non ammette un momento simulato.</summary>
    public ControlMomentsView? Moments { get; private set; }

    public AuthoritySubsetView? Subset { get; private set; }

    /// <summary>Il momento simulato nella forma che si rimanda all'API e all'agente; <c>null</c> = adesso.</summary>
    public string? AtValue => At?.ToString("O", CultureInfo.InvariantCulture);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Plate))
        {
            Identifiers = await LoadAsync(() => api.FindIdentifiersAsync(Plate, At, cancellationToken));

            // I momenti si chiedono solo se la ricerca è riuscita: a un ruolo che non può cercare basta un rifiuto.
            if (Identifiers is not null)
            {
                Moments = await SuggestedMomentsAsync(Plate, cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(Uil) && !string.IsNullOrWhiteSpace(SubsetId))
        {
            Subset = await LoadAsync(() => api.GetSubsetAsync(Uil, SubsetId, cancellationToken));
        }
    }

    /// <summary>I momenti sono un aiuto per la demo: se non arrivano, la pagina funziona lo stesso e non lo segnala.</summary>
    private async Task<ControlMomentsView?> SuggestedMomentsAsync(string plate, CancellationToken cancellationToken)
    {
        try
        {
            ControlMomentsView moments = await api.GetControlMomentsAsync(plate, cancellationToken);

            return moments.Enabled ? moments : null;
        }
        catch (Exception exception) when (exception is ApiProblemException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}

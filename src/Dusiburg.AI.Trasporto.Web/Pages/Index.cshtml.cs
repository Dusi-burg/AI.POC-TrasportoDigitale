using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Api;

namespace Dusiburg.AI.Trasporto.Web.Pages;

/// <summary>
/// Stato e configurazione: i controlli di <c>/api/health</c> (pubblici) e, per i ruoli che possono leggerla, la configurazione
/// in vigore — modello per compito, pesi e soglia dell'estrazione, permanenza sul gate. Tutto in sola lettura: le manopole si
/// impostano sull'AppHost.
/// </summary>
public sealed class IndexModel(EftiApiClient api) : ApiPageModel
{
    public ProbeReport? Report { get; private set; }

    public SettingsView? Settings { get; private set; }

    /// <summary>La configurazione è riservata a operatore e amministratore: agli altri ruoli non si chiede nemmeno.</summary>
    public bool CanSeeSettings => Role is Role.Dispatcher or Role.Admin;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Report = await LoadAsync(() => api.GetHealthAsync(cancellationToken));

        if (CanSeeSettings)
        {
            Settings = await LoadAsync(() => api.GetSettingsAsync(cancellationToken));
        }
    }
}

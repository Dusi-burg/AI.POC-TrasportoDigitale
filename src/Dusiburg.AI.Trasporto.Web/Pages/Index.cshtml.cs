using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.Trasporto.Web.Pages;

/// <summary>
/// Stato del sistema: i controlli di <c>/api/health</c> di <c>Efti.Api</c>. È la sola pagina della Fase 1; le pagine della demo
/// arrivano con la Fase 8.
/// </summary>
public sealed class IndexModel(EftiApiClient api, ILogger<IndexModel> logger) : PageModel
{
    public ProbeReport? Report { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Report = await api.GetHealthAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Stato: controlli non disponibili");
            Error = $"Efti.Api non raggiungibile: {exception.Message}";
        }
    }
}

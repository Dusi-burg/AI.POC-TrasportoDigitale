using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Mvc;

namespace Dusiburg.AI.Trasporto.Web.Pages.Shipments;

/// <summary>Elenco delle spedizioni con lo stato del dataset, filtri e importazione dal TMS.</summary>
public sealed class IndexModel(EftiApiClient api, EftiLongRunningClient longRunning) : ApiPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Customer { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    public IReadOnlyList<ShipmentSummaryView> Shipments { get; private set; } = [];

    /// <summary>Importare è dell'ufficio traffico: agli altri ruoli il pulsante non si mostra (e l'API lo rifiuterebbe).</summary>
    public bool CanImport => Role == Role.Dispatcher;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Shipments = await LoadAsync(() => api.ListShipmentsAsync(new ShipmentSearch(Status, Customer, From, To), cancellationToken)) ?? [];

    public Task<IActionResult> OnPostImportAsync(CancellationToken cancellationToken) => ActAsync(
        async () =>
        {
            ImportShipmentsResponse imported = await longRunning.ImportAllAsync(cancellationToken);

            return $"Importazione dal TMS: {imported.Imported.Count} spedizioni create, {imported.AlreadyImported.Count} già presenti, " +
                $"{imported.Skipped.Count} ordini annullati saltati.";
        },
        RedirectToPage());
}

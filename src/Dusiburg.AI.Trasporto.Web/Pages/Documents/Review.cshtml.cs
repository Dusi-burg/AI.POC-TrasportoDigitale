using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Mvc;

namespace Dusiburg.AI.Trasporto.Web.Pages.Documents;

/// <summary>
/// La coda di revisione: i campi estratti che il codice non ha lasciato diventare proposte. Una persona conferma il valore
/// (eventualmente correggendolo) o lo rifiuta; la conferma crea la nuova versione del dataset.
/// </summary>
public sealed class ReviewModel(EftiApiClient api) : ApiPageModel
{
    /// <summary>Filtro di stato; di default le voci in attesa.</summary>
    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = nameof(ReviewStatus.Pending);

    [BindProperty(SupportsGet = true)]
    public string? ShipmentCode { get; set; }

    public IReadOnlyList<ReviewTaskView> Tasks { get; private set; } = [];

    public bool CanDecide => Role == Role.Dispatcher;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Tasks = await LoadAsync(() => api.ListReviewTasksAsync(Status == "all" ? null : Status, ShipmentCode, cancellationToken)) ?? [];

    public Task<IActionResult> OnPostDecideAsync(int reviewTaskId, string decision, string? value, string? note, CancellationToken cancellationToken) => ActAsync(
        async () =>
        {
            ReviewStatus status = decision == nameof(ReviewStatus.Confirmed) ? ReviewStatus.Confirmed : ReviewStatus.Rejected;
            ReviewDecisionResponse decided = await api.DecideReviewAsync(
                reviewTaskId, status, string.IsNullOrWhiteSpace(value) ? null : value.Trim(), string.IsNullOrWhiteSpace(note) ? null : note.Trim(), cancellationToken);

            return decided.Dataset is { } dataset
                ? $"{decided.Task.ShipmentCode}, {decided.Task.Field.Label}: confermato il valore {dataset.Proposal.ProposedValue}. " +
                  $"Dataset alla versione {dataset.DatasetVersion}, {Presentation.Label(dataset.DatasetStatus).ToLowerInvariant()}."
                : $"{decided.Task.ShipmentCode}, {decided.Task.Field.Label}: rifiutato, il dataset non cambia.";
        },
        RedirectToPage(new { Status, ShipmentCode }));
}

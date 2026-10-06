using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Dusiburg.AI.Trasporto.Web.Pages.Audit;

/// <summary>
/// Il registro di ciò che è stato chiesto alla piattaforma, riuscito o rifiutato: chi, con quale ruolo, quale operazione, su che
/// cosa. Ogni riga porta alla traccia della richiesta nel dashboard.
/// </summary>
public sealed class IndexModel(EftiApiClient api, IOptions<DashboardOptions> dashboard) : ApiPageModel
{
    [BindProperty(SupportsGet = true, Name = "role")]
    public string? RoleFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Operation { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Outcome { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ShipmentCode { get; set; }

    public IReadOnlyList<AuditEventView> Events { get; private set; } = [];

    public string? DashboardUrl => dashboard.Value.Url;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Events = await LoadAsync(() => api.ListAuditAsync(new AuditSearch(RoleFilter, Operation, Outcome, ShipmentCode), cancellationToken)) ?? [];
}

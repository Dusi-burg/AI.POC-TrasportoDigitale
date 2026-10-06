using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.Trasporto.Web.Pages;

/// <summary>
/// Cambio di ruolo dal selettore della barra: salva la scelta nel cookie e porta alla prima pagina del menu di quel ruolo,
/// perché la pagina in cui si era può non essere fra quelle del nuovo ruolo.
/// </summary>
public sealed class RoleModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage(RoleMenu.HomeFor(RoleSelection.From(HttpContext)));

    public IActionResult OnPost(string? role)
    {
        Role chosen = Enum.TryParse(role, out Role parsed) && Enum.IsDefined(parsed) && !int.TryParse(role, out _) ? parsed : RoleSelection.Default;

        RoleSelection.Store(HttpContext, chosen);

        return RedirectToPage(RoleMenu.HomeFor(chosen));
    }
}

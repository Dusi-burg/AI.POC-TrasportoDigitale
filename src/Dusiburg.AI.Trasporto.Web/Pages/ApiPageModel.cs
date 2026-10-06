using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Api;
using Dusiburg.AI.Trasporto.Web.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dusiburg.AI.Trasporto.Web.Pages;

/// <summary>
/// Base delle pagine che parlano con i servizi a valle: il ruolo scelto, e gli errori dell'API trasformati in messaggi. Un
/// rifiuto per ruolo o un servizio fermo non rompono la pagina: si mostrano, perché fanno parte di ciò che la demo fa vedere.
/// </summary>
public abstract class ApiPageModel : PageModel
{
    private readonly List<string> _errors = [];

    public Role Role => RoleSelection.From(HttpContext);

    /// <summary>Messaggi d'errore raccolti durante il caricamento della pagina.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Esito dell'ultima azione, mostrato dopo il reindirizzamento.</summary>
    [TempData]
    public string? Notice { get; set; }

    /// <summary>Errore dell'ultima azione, mostrato dopo il reindirizzamento.</summary>
    [TempData]
    public string? ActionError { get; set; }

    /// <summary>Esegue una lettura; se il servizio rifiuta o non risponde annota il messaggio e restituisce <c>null</c>.</summary>
    protected async Task<T?> LoadAsync<T>(Func<Task<T>> read) where T : class
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            _errors.Add(message);

            return null;
        }
    }

    /// <summary>Esegue un'azione e torna alla pagina indicata con l'esito: riuscita o motivo del rifiuto.</summary>
    protected async Task<IActionResult> ActAsync(Func<Task<string>> action, IActionResult next)
    {
        try
        {
            Notice = await action();
        }
        catch (Exception exception) when (Describe(exception) is { } message)
        {
            ActionError = message;
        }

        return next;
    }

    /// <summary>Il messaggio per chi guarda la pagina; <c>null</c> se l'errore non è di un servizio a valle.</summary>
    protected string? Describe(Exception exception) => exception switch
    {
        ApiProblemException { IsRefusal: true } problem => $"Il ruolo {Role} non può fare questa operazione: {problem.Detail}",
        ApiProblemException problem => $"{problem.Title}: {problem.Detail}",
        HttpRequestException or TaskCanceledException => $"Servizio non raggiungibile: {exception.Message}",
        _ => null
    };
}

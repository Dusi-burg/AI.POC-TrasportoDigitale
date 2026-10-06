using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Web.Identity;

/// <summary>
/// Identità simulata nella UI (G12): il ruolo scelto nel selettore sta in un cookie, e da quello dipende la chiave che la Web
/// mette su ogni chiamata a valle. Non è autenticazione: è il modo in cui la demo mostra che cosa cambia da un ruolo all'altro.
/// </summary>
public static class RoleSelection
{
    public const string CookieName = "trasporto-role";

    public const Role Default = Role.Dispatcher;

    /// <summary>Il ruolo scelto; quello di default se il cookie manca o non è il nome di un ruolo.</summary>
    public static Role From(HttpContext? http) =>
        http?.Request.Cookies[CookieName] is { } value && Enum.TryParse(value, out Role role) && Enum.IsDefined(role) && !int.TryParse(value, out _)
            ? role
            : Default;

    public static void Store(HttpContext http, Role role) =>
        http.Response.Cookies.Append(CookieName, role.ToString(), new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true });
}

/// <summary>Una voce del menu.</summary>
public sealed record MenuEntry(string Page, string Label);

/// <summary>
/// Il menu di ogni ruolo. È una comodità, non un controllo: chi apre a mano una pagina che non è nel suo menu riceve il
/// rifiuto dell'API, che è l'unica a decidere.
/// </summary>
public static class RoleMenu
{
    private static readonly MenuEntry Shipments = new("/Shipments/Index", "Spedizioni");
    private static readonly MenuEntry Review = new("/Documents/Review", "Revisione");
    private static readonly MenuEntry Authority = new("/Authority/Index", "Controllo su strada");
    private static readonly MenuEntry Audit = new("/Audit/Index", "Audit");
    private static readonly MenuEntry Demo = new("/Demo/Index", "Dati demo");
    private static readonly MenuEntry Status = new("/Index", "Stato");

    public static IReadOnlyList<MenuEntry> For(Role role) => role switch
    {
        Role.Dispatcher => [Shipments, Review, Demo, Status],
        Role.ClaimsManager => [Shipments, Demo, Status],
        Role.Authority => [Authority, Status],
        Role.Admin => [Shipments, Review, Audit, Demo, Status],
        _ => [Status]
    };

    /// <summary>La pagina da cui parte ogni ruolo dopo averlo scelto.</summary>
    public static string HomeFor(Role role) => For(role)[0].Page;
}

/// <summary>
/// Mette su ogni richiesta a valle la chiave del ruolo scelto nel browser: la Web non ha un'identità propria, e non può fare
/// nulla che quel ruolo non possa fare.
/// </summary>
public sealed class RoleKeyHandler(IHttpContextAccessor accessor, IConfiguration configuration) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Role role = RoleSelection.From(accessor.HttpContext);

        // Un ruolo senza chiave configurata chiama senza chiave: l'API risponde 401 e la pagina lo mostra.
        if (configuration[$"{ActorResolver.ConfigurationSection}:{role}"] is { Length: > 0 } key)
        {
            request.Headers.Remove(ActorResolver.ApiKeyHeaderName);
            request.Headers.Add(ActorResolver.ApiKeyHeaderName, key.Trim());
        }

        return base.SendAsync(request, cancellationToken);
    }
}

using System.Net;
using System.Text.RegularExpressions;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dusiburg.AI.Trasporto.Web.Tests.Support;

/// <summary>
/// La Web in-process con i servizi a valle sostituiti da <see cref="PreparedApi"/>. Il client si comporta come un browser:
/// conserva i cookie (ruolo scelto, antiforgery, messaggi) e segue i reindirizzamenti.
/// </summary>
internal sealed partial class WebTestHost : IAsyncDisposable
{
    public const string DashboardUrl = "https://localhost:17999";

    /// <summary>Una pagina che, senza parametri, non chiama nessun servizio: da qui si prende il token senza sporcare le chiamate registrate.</summary>
    public const string TokenPage = "/Authority";

    private readonly WebApplicationFactory<WebEntryPoint> _factory;

    private WebTestHost(WebApplicationFactory<WebEntryPoint> factory, PreparedApi api, HttpClient client)
    {
        _factory = factory;
        Api = api;
        Client = client;
    }

    public PreparedApi Api { get; }

    public HttpClient Client { get; }

    public static string KeyOf(Role role) => $"chiave-di-test-{role.ToString().ToLowerInvariant()}";

    public static WebTestHost Start(PreparedApi api, bool dashboard = false)
    {
        WebApplicationFactory<WebEntryPoint> factory = new WebApplicationFactory<WebEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(EftiApiClient.BaseAddressConfigurationKey, "http://efti-api.test");
            builder.UseSetting(AgentsApiClient.BaseAddressConfigurationKey, "http://agents.test");

            foreach (Role role in Enum.GetValues<Role>())
            {
                builder.UseSetting($"{ActorResolver.ConfigurationSection}:{role}", KeyOf(role));
            }

            if (dashboard)
            {
                builder.UseSetting(DashboardOptions.Key, DashboardUrl);
            }

            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(nameof(EftiApiClient)).ConfigurePrimaryHttpMessageHandler(() => api);
                services.AddHttpClient(nameof(EftiLongRunningClient)).ConfigurePrimaryHttpMessageHandler(() => api);
                services.AddHttpClient(nameof(AgentsApiClient)).ConfigurePrimaryHttpMessageHandler(() => api);
            });
        });

        return new WebTestHost(factory, api, factory.CreateClient());
    }

    /// <summary>Sceglie il ruolo dal selettore della barra, come farebbe chi usa la pagina; restituisce la pagina d'arrivo.</summary>
    public async Task<string> SelectRoleAsync(Role role, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await PostFormAsync("/Role", TokenPage, cancellationToken, ("role", role.ToString()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "il cambio di ruolo porta a una pagina");

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Invia un modulo con il token antiforgery preso da una pagina della Web.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string path, string tokenPage, CancellationToken cancellationToken, params (string Name, string Value)[] fields)
    {
        string token = await TokenAsync(tokenPage, cancellationToken);
        var form = new FormUrlEncodedContent([.. fields.Select(f => KeyValuePair.Create(f.Name, f.Value)), KeyValuePair.Create("__RequestVerificationToken", token)]);

        return await Client.PostAsync(path, form, cancellationToken);
    }

    /// <summary>Il token antiforgery che le pagine mettono nei propri moduli (qui: quello del selettore di ruolo).</summary>
    public async Task<string> TokenAsync(string page, CancellationToken cancellationToken)
    {
        string html = await Client.GetStringAsync(page, cancellationToken);
        Match match = TokenPattern().Match(html);

        Assert.That(match.Success, Is.True, $"la pagina {page} ha un token antiforgery");

        return match.Groups[1].Value;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        await _factory.DisposeAsync();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}

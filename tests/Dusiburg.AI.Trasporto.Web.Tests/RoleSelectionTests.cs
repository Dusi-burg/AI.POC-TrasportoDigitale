using System.Net;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Identity;
using Dusiburg.AI.Trasporto.Web.Tests.Support;

namespace Dusiburg.AI.Trasporto.Web.Tests;

/// <summary>
/// Identità simulata nella UI (fase-8.md §2): il selettore salva il ruolo in un cookie, il menu cambia con il ruolo e ogni
/// chiamata a valle porta la chiave del ruolo scelto.
/// </summary>
public class RoleSelectionTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task WithoutChoice_ActsAsDispatcher()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/health", TestData.Health()).Get("/api/settings", TestData.Settings()));

        //SUT
        string page = await host.Client.GetStringAsync("/", CancellationToken);

        Assert.That(page, Does.Contain("Giulia Ferri</strong> (Dispatcher)"));
        Assert.That(host.Api.Calls, Is.Not.Empty);
        Assert.That(host.Api.Calls.Select(call => call.ApiKey), Is.All.EqualTo(WebTestHost.KeyOf(Role.Dispatcher)));
    }

    [TestCase(Role.Dispatcher, "Spedizioni|Revisione|Dati demo|Stato")]
    [TestCase(Role.ClaimsManager, "Spedizioni|Dati demo|Stato")]
    [TestCase(Role.Authority, "Controllo su strada|Stato")]
    [TestCase(Role.Admin, "Spedizioni|Revisione|Audit|Dati demo|Stato")]
    public async Task ChosenRole_DrivesMenuAndDownstreamKey(Role role, string expectedMenu)
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/health", TestData.Health()).Get("/api/settings", TestData.Settings()));

        //SUT
        string landing = await host.SelectRoleAsync(role, CancellationToken);
        int callsBefore = host.Api.Calls.Count;
        string status = await host.Client.GetStringAsync("/", CancellationToken);

        Assert.That(RoleMenu.For(role).Select(entry => entry.Label), Is.EqualTo(expectedMenu.Split('|')));
        Assert.That(MenuOf(status), Is.EqualTo(expectedMenu.Split('|')), "il menu della pagina è quello del ruolo");
        Assert.That(landing, Does.Contain($"<title>{TitleOfHome(role)} - Trasporto digitale</title>"), "il cambio di ruolo porta alla prima pagina del suo menu");
        Assert.That(status, Does.Contain($"({role})").And.Contain(DemoActors.For(role).Name.Split(' ')[0]));

        IReadOnlyList<DownstreamCall> calls = [.. host.Api.Calls.Skip(callsBefore)];

        Assert.That(calls, Is.Not.Empty);
        Assert.That(calls.Select(call => call.ApiKey), Is.All.EqualTo(WebTestHost.KeyOf(role)), "ogni chiamata a valle porta la chiave del ruolo scelto");
        Assert.That(calls.Any(call => call.Path == "/api/settings"), Is.EqualTo(role is Role.Dispatcher or Role.Admin), "la configurazione si chiede solo per i ruoli che la leggono");
    }

    [TestCase("42")]
    [TestCase("Superuser")]
    [TestCase("")]
    public async Task UnknownRole_FallsBackToDispatcher(string posted)
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/health", TestData.Health()));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync("/Role", WebTestHost.TokenPage, CancellationToken, ("role", posted));
        string status = await host.Client.GetStringAsync("/", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(status, Does.Contain("(Dispatcher)"));
        Assert.That(host.Api.CallsTo("/api/health").Select(call => call.ApiKey), Is.All.EqualTo(WebTestHost.KeyOf(Role.Dispatcher)));
    }

    [Test]
    public async Task RoleChange_WithoutAntiforgeryToken_IsRefused()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/health", TestData.Health()));

        //SUT
        HttpResponseMessage response = await host.Client.PostAsync("/Role", new FormUrlEncodedContent([KeyValuePair.Create("role", "Admin")]), CancellationToken);
        string status = await host.Client.GetStringAsync("/", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(status, Does.Contain("(Dispatcher)"), "il ruolo non è cambiato");
    }

    private static string TitleOfHome(Role role) => role == Role.Authority ? "Controllo su strada" : "Spedizioni";

    /// <summary>Le voci del menu, nell'ordine in cui la pagina le mostra.</summary>
    private static string[] MenuOf(string page)
    {
        string menu = page.Split("id=\"menu\">")[1].Split("</ul>")[0];

        return [.. menu.Split("</a>")[..^1].Select(entry => entry[(entry.LastIndexOf('>') + 1)..])];
    }
}

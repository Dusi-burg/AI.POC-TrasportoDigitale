using System.Net;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Agents;
using Dusiburg.AI.Trasporto.Web.Tests.Support;

namespace Dusiburg.AI.Trasporto.Web.Tests;

/// <summary>Documento, revisione, controllo su strada, audit, stato e dati demo (fase-8.md §3).</summary>
public class OtherPageTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task Document_ShowsSignalsConfidenceAndAnnotations()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/documents/5", TestData.Document()));

        //SUT
        string page = await host.Client.GetStringAsync("/Documents/Details/5", CancellationToken);

        Assert.That(page, Does.Contain("<code>ollama/qwen3.5:9b</code> in 2 letture, 24,5 s").And.Contain("Soglia di revisione <strong>0,80</strong>"));
        Assert.That(page, Does.Contain("id=\"annotations\"").And.Contain("peso verificato in pesa: 2.460 kg"));
        Assert.That(page, Does.Contain("text-bg-success\">1,00</span>").And.Contain("text-bg-warning\">0,54</span>"), "la confidenza è evidenziata rispetto alla soglia");
        Assert.That(page, Does.Contain("text-bg-success\">Coincide</span>").And.Contain("text-bg-info\">Proposta</span>").And.Contain("text-bg-warning\">In revisione</span>"));
        Assert.That(page, Does.Contain("Confidenza sotto soglia.").And.Contain("href=\"/Documents/Review?shipmentCode=SH-1003\""));
        Assert.That(page, Does.Contain("id=\"document-text\">LETTERA DI VETTURA INTERNAZIONALE"));
    }

    [Test]
    public async Task Review_ListsPendingTasks_WithTheDecisionForm()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/review-tasks", new[] { TestData.ReviewTask() }));

        //SUT
        string page = await host.Client.GetStringAsync("/Documents/Review?ShipmentCode=SH-1003", CancellationToken);

        Assert.That(host.Api.CallsTo("/api/review-tasks").Single().Query, Is.EqualTo("?status=Pending&shipmentCode=SH-1003"), "di default le voci in attesa");
        Assert.That(page, Does.Contain("data-task=\"3\"").And.Contain("Peso lordo").And.Contain("href=\"/Documents/Details/5\">CMR-1003.pdf</a>"));
        Assert.That(page, Does.Contain("name=\"value\"").And.Contain("name=\"decision\" value=\"Confirmed\"").And.Contain("name=\"decision\" value=\"Rejected\""));
    }

    [Test]
    public async Task Review_Admin_ReadsWithoutDeciding()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/review-tasks", new[] { TestData.ReviewTask() }));
        await host.SelectRoleAsync(Role.Admin, CancellationToken);

        //SUT
        string page = await host.Client.GetStringAsync("/Documents/Review?Status=all", CancellationToken);

        Assert.That(host.Api.CallsTo("/api/review-tasks").Single().Query, Is.Empty, "\"tutte\" non filtra per stato");
        Assert.That(page, Does.Contain("data-task=\"3\"").And.Not.Contain("name=\"decision\""));
    }

    [Test]
    public async Task Review_Confirm_PostsTheCorrectedValue()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Post("/api/review-tasks/3/decision", TestData.ReviewConfirmed())
            .Get("/api/review-tasks", Array.Empty<ReviewTaskView>()));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync(
            "/Documents/Review?handler=Decide", WebTestHost.TokenPage, CancellationToken, ("reviewTaskId", "3"), ("decision", "Confirmed"), ("value", " 2455 "));
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        DownstreamCall call = host.Api.CallsTo("/api/review-tasks/3/decision").Single();

        Assert.That(call.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(call.Body, Does.Contain("\"decision\":\"Confirmed\"").And.Contain("\"value\":\"2455\""));
        Assert.That(page, Does.Contain("alert alert-success").And.Contain("SH-1003, Peso lordo: confermato il valore 2455. Dataset alla versione 3, valido."));
    }

    [Test]
    public async Task Review_Reject_PostsWithoutValue()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Post("/api/review-tasks/3/decision", new ReviewDecisionResponse(TestData.ReviewTask(ReviewStatus.Rejected), null))
            .Get("/api/review-tasks", Array.Empty<ReviewTaskView>()));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync(
            "/Documents/Review?handler=Decide", WebTestHost.TokenPage, CancellationToken, ("reviewTaskId", "3"), ("decision", "Rejected"), ("value", ""));
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(host.Api.CallsTo("/api/review-tasks/3/decision").Single().Body, Does.Contain("\"decision\":\"Rejected\"").And.Not.Contain("2455"));
        Assert.That(page, Does.Contain("rifiutato, il dataset non cambia"));
    }

    [Test]
    public async Task Authority_FromPlateToSubset()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Get("/api/gate/identifiers", new[] { TestData.Identifiers() })
            .Get("/api/gate/subset", TestData.Subset("EU05a")));
        await host.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        string found = await host.Client.GetStringAsync("/Authority?Plate=ZZ104AA", CancellationToken);
        string subset = await host.Client.GetStringAsync($"/Authority?Plate=ZZ104AA&Uil={Uri.EscapeDataString(TestData.Uil)}&SubsetId=EU05a", CancellationToken);

        Assert.That(found, Does.Contain($"data-uil=\"{TestData.Uil}\"").And.Contain("Leggi EU03").And.Contain("Leggi EU05a").And.Contain("usedTransportEquipment"));
        Assert.That(found, Does.Contain($"data-agent-stream=\"{AgentStreamEndpoints.AuthorityPath}\"").And.Contain("name=\"plate\" id=\"agent-plate\" value=\"ZZ104AA\""));
        Assert.That(subset, Does.Contain("Sottoinsieme EU05a").And.Contain("&quot;unNumber&quot;: &quot;1263&quot;"));
        Assert.That(host.Api.CallsTo("/api/gate/identifiers").Select(call => call.Query), Is.All.EqualTo("?plate=ZZ104AA"));
        Assert.That(host.Api.CallsTo("/api/gate/subset").Single().Query, Is.EqualTo($"?uil={TestData.Uil}&subsetId=EU05a"));
        Assert.That(host.Api.Calls.Select(call => call.ApiKey), Is.All.EqualTo(WebTestHost.KeyOf(Role.Authority)));
    }

    [Test]
    public async Task Authority_SimulatedMoment_IsOfferedAsDemoAid_AndForwardedToSearchAndAgent()
    {
        //SETUP
        var past = new DateTimeOffset(2026, 10, 3, 9, 15, 0, TimeSpan.FromHours(1));
        const string pastIso = "2026-10-03T09:15:00.0000000+01:00";
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Get("/api/gate/identifiers", new[] { TestData.Identifiers(), TestData.Identifiers() with { Uil = TestData.Uil + "-b" } })
            .Get("/api/gate/control-moments", new ControlMomentsView(true, past.AddDays(-3), [new ControlMomentView(past, 2)])));
        await host.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        string now = await host.Client.GetStringAsync("/Authority?Plate=ZZ104AA", CancellationToken);
        string then = await host.Client.GetStringAsync($"/Authority?Plate=ZZ104AA&At={Uri.EscapeDataString(pastIso)}", CancellationToken);

        IReadOnlyList<DownstreamCall> searches = host.Api.CallsTo("/api/gate/identifiers");

        Assert.That(searches.Select(call => call.Query), Is.EqualTo(new[] { "?plate=ZZ104AA", $"?plate=ZZ104AA&at={pastIso}" }), "di default la ricerca vale adesso");
        Assert.That(now, Does.Contain("id=\"control-moments\"").And.Contain(">simulazione<").And.Contain("2 a bordo").And.Contain(Uri.EscapeDataString(pastIso)));
        Assert.That(now, Does.Contain("id=\"on-board\">A bordo di ZZ104AA adesso: 2 spedizioni").And.Contain("carico a collettame"));
        Assert.That(now, Does.Not.Contain("name=\"at\""), "senza simulazione l'agente cerca ciò che è a bordo adesso");
        Assert.That(then, Does.Contain("A bordo di ZZ104AA il ").And.Contain($"name=\"at\" value=\"{pastIso.Replace("+", "&#x2B;")}\""),
            "l'agente guarda lo stesso carico della pagina");
        Assert.That(then.Split("Leggi EU05a")[0], Does.Contain("At=" + Uri.EscapeDataString(pastIso)), "anche la lettura dei sottoinsiemi resta su quel momento");
    }

    [Test]
    public async Task Authority_WithoutDemoMoments_ShowsNoSimulation()
    {
        //SETUP
        await using WebTestHost disabled = WebTestHost.Start(new PreparedApi()
            .Get("/api/gate/identifiers", new[] { TestData.Identifiers() })
            .Get("/api/gate/control-moments", new ControlMomentsView(false, DateTimeOffset.UnixEpoch, [])));
        await using WebTestHost missing = WebTestHost.Start(new PreparedApi().Get("/api/gate/identifiers", new[] { TestData.Identifiers() }));
        await disabled.SelectRoleAsync(Role.Authority, CancellationToken);
        await missing.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        string withoutSimulation = await disabled.Client.GetStringAsync("/Authority?Plate=ZZ104AA", CancellationToken);
        string withoutEndpoint = await missing.Client.GetStringAsync("/Authority?Plate=ZZ104AA", CancellationToken);

        Assert.That(withoutSimulation, Does.Not.Contain("id=\"control-moments\"").And.Contain("una spedizione"));
        Assert.That(withoutEndpoint, Does.Not.Contain("id=\"control-moments\"").And.Not.Contain("alert-warning"), "l'aiuto per la demo che manca non è un errore della pagina");
    }

    [Test]
    public async Task Authority_Subset_IsSummarisedForAPerson_WithTheJsonAsDetail()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/gate/subset", TestData.RealisticSubset()));
        await host.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        string page = await host.Client.GetStringAsync($"/Authority?Uil={Uri.EscapeDataString(TestData.Uil)}&SubsetId=EU03", CancellationToken);
        string summary = page.Split("id=\"subset-summary\">")[1].Split("</table>")[0];

        Assert.That(SummaryValue(summary, "Mittente"), Is.EqualTo("Imballaggi Tirreno S.r.l. — Via Aurelia 150, 57121 Livorno (IT)"));
        Assert.That(SummaryValue(summary, "Destinatario"), Is.EqualTo("Química Levante S.L. — Carrer dels Fusters 9, 46014 València (ES)"));
        Assert.That(SummaryValue(summary, "Presa in carico"), Is.EqualTo("Livorno (IT) · 05/10/2026 08:30 (UTC&#x2B;01:00)"));
        Assert.That(SummaryValue(summary, "Merce"), Is.EqualTo("Vernici industriali a solvente, Imballaggi in cartone ondulato"));
        Assert.That(SummaryValue(summary, "Peso lordo"), Is.EqualTo("5.300 kg"));
        Assert.That(SummaryValue(summary, "Volume lordo"), Is.EqualTo("18,2 m³"));
        Assert.That(SummaryValue(summary, "Motrice"), Is.EqualTo("ZZ104AA (IT)"));
        Assert.That(SummaryValue(summary, "Rimorchio"), Is.EqualTo("XZ104AA (IT)"));
        Assert.That(SummaryValue(summary, "Merce pericolosa"), Is.EqualTo("UN 1263, PITTURE, classe 3, gruppo di imballaggio II, 20 colli (tipo DR), 4.200 kg"));
        Assert.That(summary, Does.Not.Contain("Vettore").And.Not.Contain("Consegna"), "ciò che il sottoinsieme non contiene non compare");
        Assert.That(summary, Does.Not.Contain("KGM").And.Not.Contain("5300"), "nel riepilogo nessun numero nel formato del JSON");
        Assert.That(page, Does.Contain("<details>").And.Contain("id=\"subset-json\"").And.Contain("5300.000"), "il dato eFTI resta sotto, come dettaglio");
    }

    [Test]
    public async Task Authority_UnknownPlate_AndRefusalForOtherRoles()
    {
        //SETUP
        await using WebTestHost empty = WebTestHost.Start(new PreparedApi().Get("/api/gate/identifiers", Array.Empty<ConsignmentIdentifiersView>()));
        await using WebTestHost refused = WebTestHost.Start(new PreparedApi().Problem(
            HttpMethod.Get, "/api/gate/identifiers", HttpStatusCode.Forbidden, "Operazione non consentita al ruolo", "Il registro è riservato alle autorità."));
        await empty.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        string nothing = await empty.Client.GetStringAsync("/Authority?Plate=ZZ106AA", CancellationToken);
        string denied = await refused.Client.GetStringAsync("/Authority?Plate=ZZ104AA", CancellationToken);

        Assert.That(nothing, Does.Contain("id=\"no-identifiers\"").And.Contain("ZZ106AA"));
        Assert.That(denied, Does.Contain("Il ruolo Dispatcher non può fare questa operazione: Il registro è riservato alle autorità.").And.Not.Contain("id=\"no-identifiers\""));
    }

    [Test]
    public async Task Audit_ForwardsFilters_AndLinksTheTrace()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/audit", new[]
        {
            TestData.Audit(31, Role.Authority, Operation.SubsetRead, AuditOutcome.Success),
            TestData.Audit(32, Role.Authority, Operation.ShipmentRead, AuditOutcome.Denied, "ruolo non autorizzato")
        }), dashboard: true);
        await host.SelectRoleAsync(Role.Admin, CancellationToken);

        //SUT
        string page = await host.Client.GetStringAsync("/Audit?role=Authority&Operation=ShipmentRead&Outcome=Denied&ShipmentCode=SH-1004", CancellationToken);

        Assert.That(host.Api.CallsTo("/api/audit").Single().Query, Is.EqualTo("?role=Authority&operation=ShipmentRead&outcome=Denied&shipmentCode=SH-1004"));
        Assert.That(page, Does.Contain("data-event=\"31\"").And.Contain("class=\"table-danger\" data-event=\"32\"").And.Contain("ruolo non autorizzato"));
        Assert.That(page, Does.Contain("text-bg-danger\">Rifiutata</span>").And.Contain($"{WebTestHost.DashboardUrl}/traces/detail/{TestData.TraceId}"));
        Assert.That(page, Does.Contain("<option value=\"Authority\" selected=\"selected\">"), "i filtri restano impostati");
    }

    [Test]
    public async Task Audit_WithoutDashboard_HasNoTraceLink()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/audit", new[] { TestData.Audit(31, Role.Authority, Operation.SubsetRead, AuditOutcome.Success) }));

        //SUT
        string page = await host.Client.GetStringAsync("/Audit", CancellationToken);

        Assert.That(page, Does.Contain("data-event=\"31\"").And.Not.Contain("/traces/detail/"));
    }

    [Test]
    public async Task Status_ShowsHealthAndSettings()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/health", TestData.Health()).Get("/api/settings", TestData.Settings()));

        //SUT
        string page = await host.Client.GetStringAsync("/", CancellationToken);

        Assert.That(page, Does.Contain("Connessione SQL").And.Contain("manca ApiKeys:Admin"));
        Assert.That(page, Does.Contain("<td>Authority</td><td>anthropic</td><td><code>claude-haiku-4-5</code></td>"));
        Assert.That(page, Does.Contain("<th>Soglia di revisione</th><td>0,80</td>").And.Contain("<th>Peso: coincide con la spedizione</th><td>0,30</td>"));
        Assert.That(page, Does.Contain("<td>spenta</td>").And.Contain("<strong>7200 minuti</strong>").And.Contain("(5 giorni)"));
    }

    [Test]
    public async Task Status_RoleWithoutSettings_SeesOnlyHealth()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/health", TestData.Health()).Get("/api/settings", TestData.Settings()));
        await host.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        string page = await host.Client.GetStringAsync("/", CancellationToken);

        Assert.That(page, Does.Contain("Connessione SQL").And.Contain("id=\"no-settings\"").And.Not.Contain("id=\"extraction\""));
        Assert.That(host.Api.CallsTo("/api/settings"), Is.Empty);
    }

    [Test]
    public async Task Demo_ListsTheSixOrders_WithShipmentAndCmr()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Get("/api/views/shipments", new[] { TestData.Summary("SH-1001", DatasetStatus.Valid), TestData.Summary("SH-1002") }));

        //SUT
        string page = await host.Client.GetStringAsync("/Demo", CancellationToken);

        foreach (DemoTransportOrder order in DemoCatalog.Orders)
        {
            Assert.That(page, Does.Contain($"data-order=\"{order.Code}\"").And.Contain(order.Trip.TractorPlate));
            Assert.That(page, Does.Contain($"href=\"/demo/cmr/CMR-{order.Code["TO-".Length..]}.pdf\""), $"CMR sintetico di {order.Code}");
        }

        Assert.That(page, Does.Contain("href=\"/Shipments/Details/SH-1001\">SH-1001</a>").And.Contain("non importata"));
        Assert.That(page, Does.Contain("href=\"/demo/cmr/CMR-1001-DIFF.pdf\"").And.Contain("href=\"/demo/cmr/CMR-1004-INJ.pdf\""));
    }

    /// <summary>Il valore di una riga del riepilogo, come compare nella pagina.</summary>
    private static string? SummaryValue(string summary, string label)
    {
        string[] parts = summary.Split($">{label}</th><td>");

        return parts.Length == 2 ? parts[1].Split("</td>")[0] : null;
    }

    [Test]
    public async Task DemoCmr_IsServedAsPdf_OnlyForKnownCodes()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi());

        //SUT
        HttpResponseMessage pdf = await host.Client.GetAsync("/demo/cmr/CMR-1003.pdf", CancellationToken);
        byte[] content = await pdf.Content.ReadAsByteArrayAsync(CancellationToken);
        HttpResponseMessage unknown = await host.Client.GetAsync("/demo/cmr/CMR-9999.pdf", CancellationToken);
        HttpResponseMessage traversal = await host.Client.GetAsync("/demo/cmr/..%2Fappsettings.pdf", CancellationToken);

        Assert.That(pdf.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/pdf"));
        Assert.That(content[..4], Is.EqualTo("%PDF"u8.ToArray()));
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(traversal.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}

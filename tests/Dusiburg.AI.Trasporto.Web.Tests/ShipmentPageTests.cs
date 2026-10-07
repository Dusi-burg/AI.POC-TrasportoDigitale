using System.Net;
using System.Net.Http.Headers;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Web.Agents;
using Dusiburg.AI.Trasporto.Web.Tests.Support;

namespace Dusiburg.AI.Trasporto.Web.Tests;

/// <summary>Elenco e pagina della spedizione (fase-8.md §3): ciò che si vede per ruolo e le azioni inoltrate a <c>Efti.Api</c>.</summary>
public class ShipmentPageTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task List_ShowsShipments_AndForwardsFilters()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Get("/api/views/shipments", new[] { TestData.Summary("SH-1002"), TestData.Summary("SH-1001", DatasetStatus.Valid) }));

        //SUT
        string page = await host.Client.GetStringAsync("/Shipments?Status=Incomplete&Customer=Brenta&From=2026-10-01&To=2026-10-31", CancellationToken);

        Assert.That(page, Does.Contain("href=\"/Shipments/Details/SH-1002\">SH-1002</a>").And.Contain("href=\"/Shipments/Details/SH-1001\""));
        Assert.That(page, Does.Contain("text-bg-warning\">Incompleto</span>").And.Contain("text-bg-success\">Valido</span>"));
        Assert.That(page, Does.Contain("Cartiera del Brenta S.p.A.").And.Contain("Papeteries du Rhône SAS").And.Contain("08/10/2026"));
        Assert.That(page, Does.Contain("id=\"import\""), "l'operatore può importare");
        Assert.That(host.Api.CallsTo("/api/views/shipments").Single().Query, Is.EqualTo("?status=Incomplete&customer=Brenta&from=2026-10-01&to=2026-10-31"));
    }

    [Test]
    public async Task List_OtherRoles_HaveNoImportButton()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Get("/api/views/shipments", Array.Empty<ShipmentSummaryView>()));
        await host.SelectRoleAsync(Role.ClaimsManager, CancellationToken);

        //SUT
        string page = await host.Client.GetStringAsync("/Shipments", CancellationToken);

        Assert.That(page, Does.Not.Contain("id=\"import\"").And.Contain("Nessuna spedizione"));
    }

    [Test]
    public async Task Import_PostsToApi_AndShowsOutcome()
    {
        //SETUP
        var imported = new ImportShipmentsResponse([new ImportedShipment("TO-1001", "SH-1001", DatasetStatus.Valid, 0, 0)], ["TO-1002"], []);
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Post("/api/shipments/import", imported)
            .Get("/api/views/shipments", new[] { TestData.Summary("SH-1001", DatasetStatus.Valid) }));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync("/Shipments?handler=Import", WebTestHost.TokenPage, CancellationToken);
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(host.Api.CallsTo("/api/shipments/import").Single().Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(page, Does.Contain("alert alert-success").And.Contain("1 spedizioni create, 1 già presenti, 0 ordini annullati saltati"));
    }

    [Test]
    public async Task Import_ApiUnreachable_IsAMessageNotAnError()
    {
        //SETUP
        // Il client delle operazioni lunghe non ha resilienza: l'errore di rete arriva subito alla pagina.
        await using WebTestHost host = WebTestHost.Start(new PreparedApi { UnreachablePath = "/api/shipments/import" }
            .Get("/api/views/shipments", Array.Empty<ShipmentSummaryView>()));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync("/Shipments?handler=Import", WebTestHost.TokenPage, CancellationToken);
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page, Does.Contain("alert alert-danger").And.Contain("Servizio non raggiungibile"));
    }

    [Test]
    public async Task Details_Dispatcher_SeesEverythingAndCanWork()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(DetailsApi("SH-1002"), dashboard: true);

        //SUT
        string page = await host.Client.GetStringAsync("/Shipments/Details/SH-1002", CancellationToken);

        Assert.That(page, Does.Contain("id=\"dataset-status\">Incompleto</span>").And.Contain("ordine TO-1002"));
        Assert.That(page, Does.Contain("<code>ADR-002</code>").And.Contain("Manca il numero ONU della merce pericolosa."));
        Assert.That(page, Does.Contain($"data-agent-stream=\"{AgentStreamEndpoints.MappingPath}\"").And.Contain("name=\"shipmentCode\" value=\"SH-1002\""));
        Assert.That(page, Does.Contain("id=\"proposals\"").And.Contain("Il numero ONU è scritto nelle istruzioni del mittente.").And.Contain("value=\"Accepted\""));
        Assert.That(page, Does.Contain("href=\"/Documents/Details/5\">CMR-1003.pdf</a>"));
        Assert.That(page, Does.Contain("Carica il CMR demo CMR-1002").And.Contain("href=\"/demo/cmr/CMR-1002.pdf\""));
        Assert.That(page, Does.Contain("id=\"authority-accesses\"").And.Contain("Pattuglia 12").And.Contain($"{WebTestHost.DashboardUrl}/traces/detail/{TestData.TraceId}"));
        Assert.That(page, Does.Contain("carrierAcceptanceLocation"), "il dataset in JSON");
    }

    [Test]
    public async Task Details_Admin_ReadsButCannotWork()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(DetailsApi("SH-1002"));
        await host.SelectRoleAsync(Role.Admin, CancellationToken);

        //SUT
        string page = await host.Client.GetStringAsync("/Shipments/Details/SH-1002", CancellationToken);

        Assert.That(page, Does.Contain("id=\"proposals\"").And.Contain("id=\"authority-accesses\""));
        Assert.That(page, Does.Not.Contain("data-agent-stream").And.Not.Contain("value=\"Accepted\"").And.Not.Contain("id=\"upload\""));
    }

    [Test]
    public async Task Details_ClaimsManager_SeesTheShipmentOnly()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(DetailsApi("SH-1002"));
        await host.SelectRoleAsync(Role.ClaimsManager, CancellationToken);

        //SUT
        string page = await host.Client.GetStringAsync("/Shipments/Details/SH-1002", CancellationToken);

        Assert.That(page, Does.Contain("id=\"dataset-status\"").And.Not.Contain("Proposte di completamento").And.Not.Contain("data-agent-stream"));
        Assert.That(host.Api.Calls.Select(call => call.Path), Has.None.Contain("proposals").And.None.Contain("documents").And.None.Contain("authority-access"),
            "non si chiede all'API ciò che verrebbe rifiutato");
    }

    [Test]
    public async Task Details_RefusedByApi_IsAMessageNotAnError()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Problem(
            HttpMethod.Get, "/api/views/shipments/SH-1002", HttpStatusCode.Forbidden, "Operazione non consentita al ruolo", "Il ruolo Authority non può leggere le spedizioni."));
        await host.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        HttpResponseMessage response = await host.Client.GetAsync("/Shipments/Details/SH-1002", CancellationToken);
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(page, Does.Contain("alert alert-warning").And.Contain("Il ruolo Authority non può fare questa operazione: Il ruolo Authority non può leggere le spedizioni."));
        Assert.That(page, Does.Not.Contain("id=\"dataset-status\""));
    }

    [TestCase("Accepted", "\"decision\":\"Accepted\"", "accettata: dataset alla versione 2")]
    [TestCase("Rejected", "\"decision\":\"Rejected\"", "rifiutata: il dataset non cambia")]
    public async Task Decide_PostsTheDecision_AndShowsOutcome(string decision, string expectedBody, string expectedNotice)
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(DetailsApi("SH-1002")
            .Post("/api/proposals/7/decision", TestData.ProposalDecision(Enum.Parse<ProposalStatus>(decision))));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync(
            "/Shipments/Details/SH-1002?handler=Decide", WebTestHost.TokenPage, CancellationToken, ("proposalId", "7"), ("decision", decision));
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        DownstreamCall call = host.Api.CallsTo("/api/proposals/7/decision").Single();

        Assert.That(call.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(call.Body, Does.Contain(expectedBody));
        Assert.That(call.ApiKey, Is.EqualTo(WebTestHost.KeyOf(Role.Dispatcher)));
        Assert.That(page, Does.Contain("alert alert-success").And.Contain(expectedNotice));
    }

    [Test]
    public async Task UploadDemo_SendsThePdf_AndOpensTheDocument()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Post("/api/shipments/SH-1003/documents", TestData.Document())
            .Get("/api/documents/5", TestData.Document()));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync(
            "/Shipments/Details/SH-1003?handler=UploadDemo", WebTestHost.TokenPage, CancellationToken, ("cmrCode", "CMR-1003"));
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        DownstreamCall call = host.Api.CallsTo("/api/shipments/SH-1003/documents").Single();

        Assert.That(call.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(call.Body, Does.Contain("filename=CMR-1003.pdf").Or.Contain("filename=\"CMR-1003.pdf\""));
        Assert.That(call.Body, Does.Contain("%PDF"), "il contenuto è il PDF del repository");
        Assert.That(response.RequestMessage?.RequestUri?.AbsolutePath, Is.EqualTo("/Documents/Details/5"));
        Assert.That(page, Does.Contain("3 campi estratti, 1 coincidono con la spedizione, 1 proposte, 1 in revisione"));
    }

    [Test]
    public async Task UploadDemo_OfAnotherShipment_IsNotSent()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(DetailsApi("SH-1002"));

        //SUT
        HttpResponseMessage response = await host.PostFormAsync(
            "/Shipments/Details/SH-1002?handler=UploadDemo", WebTestHost.TokenPage, CancellationToken, ("cmrCode", "CMR-1003"));
        string page = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(page, Does.Contain("alert alert-danger").And.Contain("non è di questa spedizione"));
        Assert.That(host.Api.Calls.Where(call => call.Method == HttpMethod.Post), Is.Empty);
    }

    [Test]
    public async Task Upload_SendsTheChosenFile()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Post("/api/shipments/SH-1003/documents", TestData.Document())
            .Get("/api/documents/5", TestData.Document()));
        string token = await host.TokenAsync(WebTestHost.TokenPage, CancellationToken);
        var file = new ByteArrayContent("%PDF-1.7 contenuto di prova"u8.ToArray());

        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var form = new MultipartFormDataContent { { file, "file", "lettera.pdf" }, { new StringContent(token), "__RequestVerificationToken" } };

        //SUT
        HttpResponseMessage response = await host.Client.PostAsync("/Shipments/Details/SH-1003?handler=Upload", form, CancellationToken);

        DownstreamCall call = host.Api.CallsTo("/api/shipments/SH-1003/documents").Single();

        Assert.That(call.Body, Does.Contain("lettera.pdf").And.Contain("%PDF-1.7 contenuto di prova"));
        Assert.That(response.RequestMessage?.RequestUri?.AbsolutePath, Is.EqualTo("/Documents/Details/5"));
    }

    [Test]
    public async Task Xml_DownloadsTheDatasetFromTheApi()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Text("/api/shipments/SH-1001/dataset.xml", "<eFTIDataset />", "application/xml", ("X-Efti-Dataset-Status", "Valid")));

        //SUT
        HttpResponseMessage response = await host.Client.GetAsync("/Shipments/Details/SH-1001?handler=Xml", CancellationToken);
        string xml = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/xml"));
        Assert.That(response.Content.Headers.ContentDisposition?.FileName, Is.EqualTo("SH-1001-efti-Valid.xml"));
        Assert.That(xml, Is.EqualTo("<eFTIDataset />"));
    }

    [Test]
    public async Task Details_ShowsTheEcmrNextToTheDataset_WithItsOwnStatusAndIssues()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(DetailsApi("SH-1002"));

        //SUT
        string page = await host.Client.GetStringAsync("/Shipments/Details/SH-1002", CancellationToken);

        Assert.That(page, Does.Contain("id=\"ecmr-status\"").And.Contain("eCMR: Bozza"));
        Assert.That(page, Does.Contain("id=\"ecmr-issues\"").And.Contain("E-NUMBER").And.Contain("ExchangedDocument/ID"));
        Assert.That(page, Does.Contain("id=\"download-ecmr-xml\"").And.Contain("handler=EcmrXml"));
        Assert.That(page, Does.Contain("id=\"ecmr-json\"").And.Contain("SpecifiedSupplyChainConsignment"));
        Assert.That(host.Api.CallsTo("/api/shipments/SH-1002/ecmr").Single().ApiKey, Is.EqualTo(WebTestHost.KeyOf(Role.Dispatcher)));
    }

    [Test]
    public async Task EcmrXml_DownloadsTheDocumentFromTheApi()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Text("/api/shipments/SH-1001/ecmr.xml", "<rsm:eCMR />", "application/xml", ("X-Ecmr-Status", "Complete")));

        //SUT
        HttpResponseMessage response = await host.Client.GetAsync("/Shipments/Details/SH-1001?handler=EcmrXml", CancellationToken);
        string xml = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/xml"));
        Assert.That(response.Content.Headers.ContentDisposition?.FileName, Is.EqualTo("SH-1001-ecmr-Complete.xml"));
        Assert.That(xml, Is.EqualTo("<rsm:eCMR />"));
    }

    /// <summary>Tutto ciò che la pagina di una spedizione chiede all'API.</summary>
    private static PreparedApi DetailsApi(string code) => new PreparedApi()
        .Get($"/api/views/shipments/{code}", TestData.Shipment(code))
        .Get($"/api/shipments/{code}/ecmr", TestData.Ecmr(code))
        .Get($"/api/shipments/{code}/proposals", new[] { TestData.Proposal() })
        .Get($"/api/shipments/{code}/documents", new[] { TestData.DocumentSummary(5, code) })
        .Get($"/api/shipments/{code}/authority-access", new[] { TestData.Audit(21, Role.Authority, Operation.SubsetRead, AuditOutcome.Success) });
}

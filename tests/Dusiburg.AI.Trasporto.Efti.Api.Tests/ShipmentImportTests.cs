using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Api.Tms;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>
/// Importazione dal TMS, viste ed esportazione XML: <c>Efti.Api</c> in-process su un database dedicato di
/// <c>(localdb)\localdev</c>, con un TMS finto che serve gli ordini del seed. Prima di ogni test lo schema efti è vuoto.
/// </summary>
public class ShipmentImportTests
{
    private static readonly DateOnly SeedDate = new(2026, 10, 5);

    private string _connectionString = null!;
    private FakeTmsHandler _tms = null!;
    private WebApplicationFactory<EftiApiEntryPoint> _factory = null!;
    private HttpClient _client = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabaseAndFactory()
    {
        _connectionString = TrasportoDatabaseInitializer.LocalConnectionStringFor($"Trasporto_Test_{Guid.NewGuid():N}");
        await TrasportoDatabaseInitializer.RecreateAsync(_connectionString, seed: false, cancellationToken: CancellationToken);

        _tms = new FakeTmsHandler(TmsSeedGenerator.Generate(TmsSeedGenerator.DefaultRandomSeed, SeedDate));

        _factory = new WebApplicationFactory<EftiApiEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:sql", _connectionString);
            builder.UseSetting(TmsApiClient.BaseAddressConfigurationKey, "http://tms.test");
            builder.UseTestApiKeys();

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<TmsApiClient>().ConfigurePrimaryHttpMessageHandler(() => _tms);

                // Un solo nuovo tentativo, subito: il test del TMS irraggiungibile non deve aspettare i ritardi di produzione.
                services.ConfigureAll<HttpStandardResilienceOptions>(options =>
                {
                    options.Retry.MaxRetryAttempts = 1;
                    options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                });

                // Gli health check non sono oggetto di questi test: nessuna chiamata a Ollama.
                services.RemoveAll<IHealthProbe>();
            });
        });

        // Importazione e viste sono operazioni dell'ufficio traffico: la matrice dei ruoli ha i suoi test in AccessControlTests.
        _client = _factory.CreateClient().ActingAs(Role.Dispatcher);
    }

    [SetUp]
    public async Task EmptyEftiSchema()
    {
        _tms.Unavailable = false;

        using HttpResponseMessage response = await _client.PostAsync("/dev/reset", content: null, CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _tms.Dispose();
        await TrasportoDatabaseInitializer.DropAsync(_connectionString);
    }

    [Test]
    public async Task Import_CompleteDemoOrder_CreatesAValidShipmentWithItsDataset()
    {
        //SUT
        ImportShipmentsResponse result = await ImportAsync("TO-1001");

        ShipmentDetailView shipment = (await _client.GetFromJsonAsync<ShipmentDetailView>("/api/views/shipments/SH-1001", TrasportoJson.Options, CancellationToken))!;
        Assert.That(result.Imported, Is.EqualTo(new[] { new ImportedShipment("TO-1001", "SH-1001", DatasetStatus.Valid, 0, 0) }));
        Assert.That((shipment.Shipment.TmsOrderCode, shipment.Shipment.Status, shipment.Shipment.DatasetVersion), Is.EqualTo(("TO-1001", DatasetStatus.Valid, 1)));
        Assert.That(shipment.PublicId, Is.Not.EqualTo(Guid.Empty));
        Assert.That((shipment.Shipment.ConsignorName, shipment.Shipment.ConsigneeName, shipment.Shipment.DeliveryCity, shipment.Shipment.VehiclePlateNumber),
            Is.EqualTo(("Officine Meccaniche Brambilla S.r.l.", "Maschinenbau Neckartal GmbH", "Stuttgart", "ZZ101AA")));
        Assert.That(shipment.Parties.Select(p => p.Role), Is.EqualTo(new[] { PartyRole.Consignor, PartyRole.Consignee, PartyRole.Carrier }));
        Assert.That(shipment.Items, Has.Count.EqualTo(2));
        Assert.That(shipment.Issues, Is.Empty);
        Assert.That(shipment.Dataset.SchemaVersion, Is.EqualTo(EftiSchemaCatalog.Default.Version));
        Assert.That(shipment.Dataset.Json.GetProperty("consignee").GetProperty("name")[0].GetString(), Is.EqualTo("Maschinenbau Neckartal GmbH"));
        Assert.That(shipment.Dataset.Json.GetProperty("carrier").GetProperty("name")[0].GetString(), Is.EqualTo(DemoCatalog.Carrier.Name));
    }

    [Test]
    public async Task Import_DefectiveDemoOrder_SavesAnIncompleteDatasetWithItsIssues()
    {
        //SUT
        ImportShipmentsResponse result = await ImportAsync("TO-1002");

        ShipmentDetailView shipment = (await _client.GetFromJsonAsync<ShipmentDetailView>("/api/views/shipments/SH-1002", TrasportoJson.Options, CancellationToken))!;
        Assert.That(result.Imported.Single(), Is.EqualTo(new ImportedShipment("TO-1002", "SH-1002", DatasetStatus.Incomplete, 4, 1)));
        Assert.That(shipment.Issues.Select(i => (i.RuleCode, i.Severity)), Is.EquivalentTo(new[]
        {
            ("M-CONSIGNEE-ADDRESS", IssueSeverity.Error),
            ("M-ADR-UN-NUMBER", IssueSeverity.Error),
            ("M-ADR-SHIPPING-NAME", IssueSeverity.Error),
            ("C-WEIGHT-SUM", IssueSeverity.Error),
            ("W-PARTY-VAT", IssueSeverity.Warning)
        }));
        Assert.That(shipment.Issues.Single(i => i.RuleCode == "M-CONSIGNEE-ADDRESS").FieldPath, Is.EqualTo("consignee/postalAddress/postcode"));
        Assert.That((shipment.Shipment.Errors, shipment.Shipment.Warnings), Is.EqualTo((4, 1)));
        Assert.That(shipment.SenderInstructions, Does.Contain("UN 2014"));
    }

    [Test]
    public async Task Import_SameOrderTwice_DoesNotTouchTheExistingShipment()
    {
        //SETUP
        await ImportAsync("TO-1001");
        ShipmentDetailView before = (await _client.GetFromJsonAsync<ShipmentDetailView>("/api/views/shipments/SH-1001", TrasportoJson.Options, CancellationToken))!;
        int detailRequests = _tms.DetailRequests;

        //SUT
        ImportShipmentsResponse again = await ImportAsync("TO-1001");

        ShipmentDetailView after = (await _client.GetFromJsonAsync<ShipmentDetailView>("/api/views/shipments/SH-1001", TrasportoJson.Options, CancellationToken))!;
        Assert.That(again.Imported, Is.Empty);
        Assert.That(again.AlreadyImported, Is.EqualTo(new[] { "TO-1001" }));
        Assert.That((after.PublicId, after.Shipment.ImportedAt, after.Shipment.DatasetVersion), Is.EqualTo((before.PublicId, before.Shipment.ImportedAt, 1)));
        Assert.That(_tms.DetailRequests, Is.EqualTo(detailRequests), "un ordine già importato non viene nemmeno riletto dal TMS");
        Assert.That(await CountAsync(db => db.Shipments.CountAsync(CancellationToken)), Is.EqualTo(1));
    }

    [Test]
    public async Task Import_UnknownOrCancelledOrder_IsRefused()
    {
        //SETUP
        string cancelled = _tms.Orders.Values.First(o => o.Order.Status == TransportOrderStatus.Cancelled).Order.Code;

        //SUT
        using HttpResponseMessage unknownResponse = await PostImportAsync("TO-9999");
        using HttpResponseMessage cancelledResponse = await PostImportAsync(cancelled);

        Assert.That((unknownResponse.StatusCode, await ReadProblemCodeAsync(unknownResponse)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
        Assert.That((cancelledResponse.StatusCode, await ReadProblemCodeAsync(cancelledResponse)), Is.EqualTo((HttpStatusCode.Conflict, ErrorCodes.Conflict)));
        Assert.That(await CountAsync(db => db.Shipments.CountAsync(CancellationToken)), Is.EqualTo(0));
    }

    [Test]
    public async Task Import_TmsNotReachable_ReturnsUpstreamUnavailable()
    {
        //SETUP
        _tms.Unavailable = true;

        //SUT
        using HttpResponseMessage response = await PostImportAsync("TO-1001");

        Assert.That((response.StatusCode, await ReadProblemCodeAsync(response)), Is.EqualTo((HttpStatusCode.ServiceUnavailable, ErrorCodes.UpstreamUnavailable)));
    }

    [Test]
    public async Task ImportAll_WholeTms_ImportsEveryOrderExceptTheCancelledOnes_AndIsIdempotent()
    {
        //SETUP
        string[] cancelled = [.. _tms.Orders.Values.Where(o => o.Order.Status == TransportOrderStatus.Cancelled).Select(o => o.Order.Code)];
        int importable = _tms.Orders.Count - cancelled.Length;
        await ImportAsync("TO-1004");

        //SUT
        ImportShipmentsResponse first = await ImportAsync(orderCode: null);
        ImportShipmentsResponse second = await ImportAsync(orderCode: null);

        List<ShipmentSummaryView> all = await ListAsync("/api/views/shipments");
        List<ShipmentSummaryView> valid = await ListAsync("/api/views/shipments?status=Valid");
        List<ShipmentSummaryView> incomplete = await ListAsync("/api/views/shipments?status=incomplete");
        Assert.That(first.Imported, Has.Count.EqualTo(importable - 1));
        Assert.That(first.AlreadyImported, Is.EqualTo(new[] { "TO-1004" }));
        Assert.That(first.Skipped, Is.EquivalentTo(cancelled));
        Assert.That((second.Imported.Count, second.AlreadyImported.Count, second.Skipped.Count), Is.EqualTo((0, importable, cancelled.Length)));
        Assert.That(all, Has.Count.EqualTo(importable));
        Assert.That(valid.Count + incomplete.Count, Is.EqualTo(importable));
        Assert.That(valid.Select(s => s.Status), Is.All.EqualTo(DatasetStatus.Valid));
        Assert.That(valid.Select(s => s.Errors), Is.All.EqualTo(0));
        Assert.That(incomplete.Select(s => s.Errors), Is.All.GreaterThan(0));
        Assert.That(incomplete.Select(s => s.TmsOrderCode), Does.Contain("TO-1002").And.Contain("TO-1003"));
        Assert.That(valid.Select(s => s.TmsOrderCode), Is.SupersetOf(new[] { "TO-1001", "TO-1004", "TO-1005", "TO-1006" }));
    }

    [Test]
    public async Task ImportAll_PostWithoutBody_ImportsEverything()
    {
        //SETUP
        int importable = _tms.Orders.Values.Count(o => o.Order.Status != TransportOrderStatus.Cancelled);

        //SUT
        using HttpResponseMessage response = await _client.PostAsync("/api/shipments/import", content: null, CancellationToken);
        ImportShipmentsResponse? result = await response.Content.ReadFromJsonAsync<ImportShipmentsResponse>(TrasportoJson.Options, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Imported, Has.Count.EqualTo(importable));
    }

    [Test]
    public async Task ImportAll_Carrier_IsASinglePartyForAllShipments()
    {
        //SETUP
        await ImportAsync(orderCode: null);

        //SUT
        int carrierParties = await CountAsync(db => db.ShipmentParties.Where(p => p.Role == PartyRole.Carrier).Select(p => p.PartyId).Distinct().CountAsync(CancellationToken));
        int carrierRoles = await CountAsync(db => db.ShipmentParties.CountAsync(p => p.Role == PartyRole.Carrier, CancellationToken));

        Assert.That(carrierParties, Is.EqualTo(1));
        Assert.That(carrierRoles, Is.EqualTo(await CountAsync(db => db.Shipments.CountAsync(CancellationToken))));
    }

    [Test]
    public async Task ExportDataset_ValidShipment_ReturnsXmlConformToTheSchema()
    {
        //SETUP
        await ImportAsync("TO-1004");
        var schemaValidator = new EftiXmlValidator(EftiSchemaCatalog.Default);

        //SUT
        using HttpResponseMessage response = await _client.GetAsync("/api/shipments/SH-1004/dataset.xml", CancellationToken);
        XDocument document = XDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        XNamespace efti = EftiSchemaCatalog.Default.TargetNamespace;
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/xml"));
        Assert.That(response.Headers.GetValues("X-Efti-Dataset-Status"), Is.EqualTo(new[] { "Valid" }));
        Assert.That(schemaValidator.Validate(document), Is.Empty);
        Assert.That(document.Descendants(efti + "uNDGID").Single().Value, Is.EqualTo("1263"));
        Assert.That(document.Root!.Element(efti + "grossWeight")!.Attribute("unitId")!.Value, Is.EqualTo("KGM"));
    }

    [Test]
    public async Task ExportDataset_IncompleteOrUnknownShipment_SaysSoInHeaderOrStatus()
    {
        //SETUP
        await ImportAsync("TO-1002");

        //SUT
        using HttpResponseMessage incomplete = await _client.GetAsync("/api/shipments/SH-1002/dataset.xml", CancellationToken);
        using HttpResponseMessage unknown = await _client.GetAsync("/api/shipments/SH-9999/dataset.xml", CancellationToken);

        Assert.That(incomplete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(incomplete.Headers.GetValues("X-Efti-Dataset-Status"), Is.EqualTo(new[] { "Incomplete" }));
        Assert.That((unknown.StatusCode, await ReadProblemCodeAsync(unknown)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
    }

    [Test]
    public async Task Import_CreatesTheEcmrNextToTheDataset_WithItsOwnStatus()
    {
        //SUT
        await ImportAsync("TO-1004");
        await ImportAsync("TO-1003");

        EcmrDocumentView complete = (await _client.GetFromJsonAsync<EcmrDocumentView>("/api/shipments/SH-1004/ecmr", TrasportoJson.Options, CancellationToken))!;
        EcmrDocumentView draft = (await _client.GetFromJsonAsync<EcmrDocumentView>("/api/shipments/SH-1003/ecmr", TrasportoJson.Options, CancellationToken))!;
        Assert.That((complete.Version, complete.Status, complete.SchemaVersion, complete.Errors), Is.EqualTo((1, EcmrStatus.Complete, "100.D25A", 0)));
        Assert.That(complete.Json.GetProperty("ExchangedDocument").GetProperty("ID").GetString(), Is.EqualTo("CMR-1004001"));
        Assert.That(complete.Json.GetProperty("SpecifiedSupplyChainConsignment").GetProperty("AssociatedReferencedDocument").GetArrayLength(), Is.EqualTo(2));
        Assert.That((draft.Version, draft.Status), Is.EqualTo((1, EcmrStatus.Draft)));
        Assert.That(draft.Issues.Select(i => i.RuleCode).Distinct(), Is.EquivalentTo(new[] { "E-PACKAGES", "E-GROSS-WEIGHT" }));
        Assert.That(draft.Issues.Select(i => i.FieldPath), Is.All.StartsWith("SpecifiedSupplyChainConsignment/"));
    }

    [Test]
    public async Task ExportEcmr_CompleteShipment_ReturnsXmlConformToTheUnCefactSchema()
    {
        //SETUP
        await ImportAsync("TO-1004");

        //SUT
        using HttpResponseMessage response = await _client.GetAsync("/api/shipments/SH-1004/ecmr.xml", CancellationToken);
        XDocument document = XDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        XNamespace rsm = EcmrSchema.Default.TargetNamespace;
        XNamespace ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/xml"));
        Assert.That(response.Headers.GetValues("X-Ecmr-Status"), Is.EqualTo(new[] { "Complete" }));
        Assert.That(EcmrSchema.Default.Validate(document), Is.Empty);
        Assert.That(document.Root!.Name, Is.EqualTo(rsm + "eCMR"));
        Assert.That(document.Descendants(ram + "UNDGIdentificationCode").Single().Value, Is.EqualTo("1263"));
        Assert.That(document.Descendants(ram + "Marking").Select(m => m.Value), Is.EqualTo(new[] { "TO-1004 1/2", "TO-1004 2/2" }));
    }

    [Test]
    public async Task ExportEcmr_DraftOrUnknownShipment_SaysSoInHeaderOrStatus()
    {
        //SETUP
        await ImportAsync("TO-1002");

        //SUT
        using HttpResponseMessage draft = await _client.GetAsync("/api/shipments/SH-1002/ecmr.xml", CancellationToken);
        using HttpResponseMessage unknownXml = await _client.GetAsync("/api/shipments/SH-9999/ecmr.xml", CancellationToken);
        using HttpResponseMessage unknownView = await _client.GetAsync("/api/shipments/SH-9999/ecmr", CancellationToken);

        Assert.That(draft.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(draft.Headers.GetValues("X-Ecmr-Status"), Is.EqualTo(new[] { "Draft" }));
        Assert.That((unknownXml.StatusCode, await ReadProblemCodeAsync(unknownXml)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
        Assert.That((unknownView.StatusCode, await ReadProblemCodeAsync(unknownView)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
    }

    [Test]
    public async Task ImportAll_EveryShipmentHasOneEcmr_NationalOnesIncluded()
    {
        //SUT
        await ImportAsync(orderCode: null);

        int shipments = await CountAsync(db => db.Shipments.CountAsync(CancellationToken));
        int documents = await CountAsync(db => db.EcmrDocuments.CountAsync(CancellationToken));
        int completeNational = await CountAsync(db => db.EcmrDocuments.CountAsync(
            d => d.Status == EcmrStatus.Complete && d.Shipment.Pickup.CountryCode == d.Shipment.Delivery.CountryCode, CancellationToken));
        int complete = await CountAsync(db => db.EcmrDocuments.CountAsync(d => d.Status == EcmrStatus.Complete, CancellationToken));
        int schemaFailures = await CountAsync(db => db.EcmrIssues.CountAsync(i => i.RuleCode == EcmrRules.SchemaRuleCode, CancellationToken));
        Assert.That(documents, Is.EqualTo(shipments));
        Assert.That(completeNational, Is.GreaterThan(20));
        Assert.That(complete, Is.GreaterThan(20));
        Assert.That(schemaFailures, Is.Zero);
    }

    [TestCase("/api/views/shipments/SH-9999", HttpStatusCode.NotFound, ErrorCodes.NotFound)]
    [TestCase("/api/views/shipments?status=Firmato", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    public async Task Views_UnknownShipmentOrInvalidFilter_ReturnProblem(string path, HttpStatusCode expectedStatus, string expectedCode)
    {
        //SUT
        using HttpResponseMessage response = await _client.GetAsync(path, CancellationToken);

        Assert.That((response.StatusCode, await ReadProblemCodeAsync(response)), Is.EqualTo((expectedStatus, expectedCode)));
    }

    private async Task<ImportShipmentsResponse> ImportAsync(string? orderCode)
    {
        using HttpResponseMessage response = await PostImportAsync(orderCode);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ImportShipmentsResponse>(TrasportoJson.Options, CancellationToken))!;
    }

    private Task<HttpResponseMessage> PostImportAsync(string? orderCode) =>
        _client.PostAsJsonAsync("/api/shipments/import", new ImportShipmentsRequest(orderCode), TrasportoJson.Options, CancellationToken);

    private async Task<List<ShipmentSummaryView>> ListAsync(string path) =>
        (await _client.GetFromJsonAsync<List<ShipmentSummaryView>>(path, TrasportoJson.Options, CancellationToken))!;

    private async Task<int> CountAsync(Func<EftiDbContext, Task<int>> count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        return await count(scope.ServiceProvider.GetRequiredService<EftiDbContext>());
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}

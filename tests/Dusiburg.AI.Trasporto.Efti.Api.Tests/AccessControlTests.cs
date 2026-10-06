using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Api.Tms;
using Dusiburg.AI.Trasporto.Efti.Core.Security;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>
/// Identità simulata, matrice dei ruoli e audit, via HTTP su un database dedicato: ogni ruolo su ogni operazione, chiave assente
/// e sconosciuta, flusso dell'autorità dal registro degli identificatori, accessi visibili all'operatore, audit append-only.
/// </summary>
public class AccessControlTests
{
    private static readonly DateOnly SeedDate = new(2026, 10, 5);

    /// <summary>L'API crede che sia mezzogiorno del giorno del seed: SH-1005 è consegnata da due giorni, SH-1006 da nove.</summary>
    private static readonly DateTimeOffset Now = new(SeedDate, new TimeOnly(12, 0), TimeSpan.Zero);

    private string _connectionString = null!;
    private FakeTmsHandler _tms = null!;
    private WebApplicationFactory<EftiApiEntryPoint> _factory = null!;
    private HttpClient _client = null!;

    /// <summary>UIL di SH-1001, ottenuto come lo ottiene l'autorità: cercando la targa.</summary>
    private string _uil = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    private static IEnumerable<TestCaseData> MatrixCells() =>
        from role in Enum.GetValues<Role>()
        from operation in Enum.GetValues<Operation>()
        select new TestCaseData(role, operation).SetArgDisplayNames(role.ToString(), operation.ToString());

    private static Operation[] Operations() => Enum.GetValues<Operation>();

    [OneTimeSetUp]
    public async Task CreateDatabaseAndImportDemoShipments()
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
                services.RemoveAll<IHealthProbe>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            });
        });

        _client = _factory.CreateClient();

        // SH-1001 e SH-1004 sono valide e in viaggio (sul gate); SH-1002 è incompleta (mai pubblicata); SH-1005 è consegnata
        // da due giorni (ancora sul gate); SH-1006 da nove (scaduta).
        foreach (string orderCode in new[] { "TO-1001", "TO-1002", "TO-1004", "TO-1005", "TO-1006" })
        {
            using HttpResponseMessage imported = await SendAsync(Role.Dispatcher, HttpMethod.Post, "/api/shipments/import", new ImportShipmentsRequest(orderCode));

            imported.EnsureSuccessStatusCode();
        }

        _uil = (await FindIdentifiersAsync("ZZ101AA")).Single().Uil;
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _tms.Dispose();
        await TrasportoDatabaseInitializer.DropAsync(_connectionString);
    }

    [TestCaseSource(nameof(MatrixCells))]
    public async Task Operation_ByRole_FollowsTheMatrixAndIsAudited(Role role, Operation operation)
    {
        //SETUP
        bool allowed = AuthorizationPolicy.IsAllowed(role, operation);

        //SUT
        using HttpResponseMessage response = await SendAsync(TestApiKeys.For(role), operation);

        AuditEvent audited = await LastAuditEventAsync();
        Assert.That(response.StatusCode, allowed ? Is.EqualTo(HttpStatusCode.OK) : Is.EqualTo(HttpStatusCode.Forbidden), $"{role} → {operation}");
        Assert.That((audited.Role, audited.Operation, audited.Outcome, audited.ActorId),
            Is.EqualTo(((Role?)role, operation, allowed ? AuditOutcome.Success : AuditOutcome.Denied, DemoActors.For(role).Id)));

        if (!allowed)
        {
            Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ErrorCodes.Unauthorized));
            Assert.That(audited.Reason, Does.Contain(role.ToString()).And.Contain(operation.ToString()));
        }
    }

    [TestCaseSource(nameof(Operations))]
    public async Task Operation_WithoutOrWithUnknownKey_IsUnauthorizedAndAudited(Operation operation)
    {
        //SUT
        using HttpResponseMessage withoutKey = await SendAsync(apiKey: null, operation);
        AuditEvent withoutKeyAudit = await LastAuditEventAsync();
        using HttpResponseMessage unknownKey = await SendAsync("chiave-inventata", operation);
        AuditEvent unknownKeyAudit = await LastAuditEventAsync();

        Assert.That((withoutKey.StatusCode, await ReadProblemCodeAsync(withoutKey)), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)));
        Assert.That((unknownKey.StatusCode, await ReadProblemCodeAsync(unknownKey)), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)));
        Assert.That((withoutKeyAudit.Role, withoutKeyAudit.ActorId, withoutKeyAudit.Operation, withoutKeyAudit.Outcome),
            Is.EqualTo(((Role?)null, (string?)null, operation, AuditOutcome.Denied)));
        Assert.That(withoutKeyAudit.Reason, Does.Contain("assente"));
        Assert.That(unknownKeyAudit.Reason, Does.Contain("non riconosciuta").And.Not.Contain("chiave-inventata"));
    }

    [Test]
    public void Endpoints_UnderApi_AllDeclareTheirOperation_AsDocumentedInTheMatrix()
    {
        //SETUP
        RouteEndpoint[] endpoints = [.. _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api", StringComparison.Ordinal))];

        //SUT
        var declared = endpoints
            .Select(e => (Route: $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText}", Operation: e.Metadata.GetMetadata<OperationMetadata>()?.Operation))
            .ToList();

        // /api/health è il solo endpoint pubblico: dice come stanno le dipendenze, non espone dati di spedizione.
        Assert.That(declared.Where(d => d.Operation is null).Select(d => d.Route), Is.EqualTo(new[] { "GET /api/health" }));
        Assert.That(declared.Where(d => d.Operation is not null).Select(d => d.Operation!.Value), Is.EquivalentTo(Enum.GetValues<Operation>()), "un endpoint per operazione");
        Assert.That(
            declared.Where(d => d.Operation is not null).Select(d => (d.Operation!.Value, d.Route)),
            Is.EquivalentTo(AuthorizationPolicy.Operations.Select(o => (o.Operation, o.Endpoint.Split('?')[0]))),
            "gli endpoint scritti nella matrice sono quelli veri");
    }

    [Test]
    public async Task Me_AsAuthority_ReturnsTheDemoActorAndWhatItCanDo()
    {
        //SUT
        using HttpResponseMessage response = await SendAsync(Role.Authority, HttpMethod.Get, "/api/me");
        ActorView? actor = await response.Content.ReadFromJsonAsync<ActorView>(TrasportoJson.Options, CancellationToken);

        Assert.That((actor!.Id, actor.Name, actor.Role), Is.EqualTo((DemoActors.For(Role.Authority).Id, DemoActors.For(Role.Authority).Name, Role.Authority)));
        Assert.That(actor.AllowedOperations, Is.EqualTo(new[] { Operation.IdentityRead, Operation.IdentifierSearch, Operation.SubsetRead }));
    }

    [Test]
    public async Task Authority_SearchesAPlate_GetsUilAndIdentifiersOnly()
    {
        //SUT
        IReadOnlyList<ConsignmentIdentifiersView> found = await FindIdentifiersAsync("zz-101 aa");
        IReadOnlyList<ConsignmentIdentifiersView> byTrailer = await FindIdentifiersAsync("XZ101AA");
        IReadOnlyList<ConsignmentIdentifiersView> incompleteShipment = await FindIdentifiersAsync("ZZ102AA");
        IReadOnlyList<ConsignmentIdentifiersView> unknownPlate = await FindIdentifiersAsync("ZZ999ZZ");

        JsonElement identifiers = found.Single().Identifiers;
        Assert.That(found.Single().Uil, Does.Match("^it-gate-sim:dusiburg-trasporto:[0-9a-f-]{36}$"));
        Assert.That(byTrailer.Select(f => f.Uil), Is.EqualTo(found.Select(f => f.Uil)), "la ricerca vale anche per la targa del rimorchio");
        Assert.That(identifiers.EnumerateObject().Select(p => p.Name),
            Is.EquivalentTo(new[] { "carrierAcceptanceDateTime", "mainCarriageTransportMovement", "usedTransportEquipment" }));
        Assert.That(identifiers.GetProperty("mainCarriageTransportMovement")[0].GetProperty("usedTransportMeans").GetProperty("id").GetString(), Is.EqualTo("ZZ101AA"));
        Assert.That(identifiers.GetRawText(), Does.Not.Contain("Brambilla").And.Not.Contain("Stuttgart").And.Not.Contain("SH-1001"));
        Assert.That(incompleteShipment, Is.Empty, "una spedizione con dataset incompleto non è sul gate");
        Assert.That(unknownPlate, Is.Empty);
    }

    [Test]
    public async Task Authority_ReadsASubsetByUil_GetsOnlyTheElementsOfThatSubset()
    {
        //SETUP
        string dangerousGoodsUil = (await FindIdentifiersAsync("ZZ104AA")).Single().Uil;

        //SUT
        AuthoritySubsetView general = await GetSubsetAsync(_uil, "EU03");
        AuthoritySubsetView dangerousGoods = await GetSubsetAsync(dangerousGoodsUil, "EU05a");

        string generalJson = general.Dataset.GetRawText();
        Assert.That((general.Uil, general.SubsetId, general.SchemaVersion), Is.EqualTo((_uil, "EU03", "0.8")));
        Assert.That(general.Dataset.GetProperty("consignor").GetProperty("name")[0].GetString(), Is.EqualTo("Officine Meccaniche Brambilla S.r.l."));
        Assert.That(general.Dataset.GetProperty("consignee").GetProperty("postalAddress").GetProperty("cityName")[0].GetString(), Is.EqualTo("Stuttgart"));
        Assert.That(general.Dataset.GetProperty("mainCarriageTransportMovement")[0].GetProperty("usedTransportMeans").GetProperty("typeCode").GetString(), Is.EqualTo("33"));
        Assert.That(generalJson, Does.Not.Contain("taxRegistration").And.Not.Contain("IT01000010007"), "la partita IVA non è nel sottoinsieme");
        Assert.That(generalJson, Does.Not.Contain("applicableServiceCharge").And.Not.Contain("transportContractDocument").And.Not.Contain("paymentArrangementCode"), "nessun dato contrattuale");
        Assert.That(dangerousGoods.Dataset.GetProperty("includedConsignmentItem")[0].GetProperty("transportDangerousGoods")[0].GetProperty("uNDGID").GetString(), Is.EqualTo("1263"));
        Assert.That(dangerousGoods.Dataset.GetProperty("mainCarriageTransportMovement")[0].GetProperty("modeCode").GetString(), Is.EqualTo("3"));
    }

    [TestCase("uil=non-un-uil&subsetId=EU03", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("uil=it-gate-sim:dusiburg-trasporto:00000000-0000-0000-0000-000000000001&subsetId=EU03", HttpStatusCode.NotFound, ErrorCodes.NotFound)]
    [TestCase("uil={uil}&subsetId=EU01", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    [TestCase("uil={uil}", HttpStatusCode.BadRequest, ErrorCodes.ValidationError)]
    public async Task Authority_SubsetRequestThatCannotBeServed_IsRefusedAndAuditedAsFailed(string query, HttpStatusCode expectedStatus, string expectedCode)
    {
        //SUT
        using HttpResponseMessage response = await SendAsync(Role.Authority, HttpMethod.Get, $"/api/gate/subset?{query.Replace("{uil}", _uil, StringComparison.Ordinal)}");

        AuditEvent audited = await LastAuditEventAsync();
        Assert.That((response.StatusCode, await ReadProblemCodeAsync(response)), Is.EqualTo((expectedStatus, expectedCode)));
        Assert.That((audited.Operation, audited.Outcome), Is.EqualTo((Operation.SubsetRead, AuditOutcome.Failed)));
        Assert.That(audited.Reason, Does.StartWith(((int)expectedStatus).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task Authority_DeliveredShipments_StayOnTheGateForFiveDays()
    {
        //SUT
        IReadOnlyList<ConsignmentIdentifiersView> deliveredTwoDaysAgo = await FindIdentifiersAsync("ZZ105AA");
        IReadOnlyList<ConsignmentIdentifiersView> deliveredNineDaysAgo = await FindIdentifiersAsync("ZZ106AA");
        AuthoritySubsetView subset = await GetSubsetAsync(deliveredTwoDaysAgo.Single().Uil, "EU03");

        JsonElement delivery = deliveredTwoDaysAgo.Single().Identifiers.GetProperty("deliveryEvent").GetProperty("actualOccurrenceDateTime");
        Assert.That((delivery.GetProperty("value").GetString(), delivery.GetProperty("formatId").GetString()), Is.EqualTo(("202610031015+0100", "205")));
        Assert.That(subset.Dataset.GetProperty("consignee").GetProperty("name")[0].GetString(), Is.EqualTo("Textilhaus Donau GmbH"));
        Assert.That(deliveredNineDaysAgo, Is.Empty, "SH-1006 è consegnata da più di cinque giorni: non è più sul gate");
    }

    [Test]
    public async Task Authority_GateRetention_FollowsTheSetting()
    {
        //SETUP
        // Stesso database e stesso orologio, cambia solo l'impostazione: con 15 giorni torna raggiungibile anche SH-1006, con
        // 15 minuti non lo è più nemmeno SH-1005.
        await using WebApplicationFactory<EftiApiEntryPoint> fifteenDays = _factory.WithWebHostBuilder(
            builder => builder.UseSetting("Efti:GateRetentionMinutes", (15 * 24 * 60).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await using WebApplicationFactory<EftiApiEntryPoint> fifteenMinutes = _factory.WithWebHostBuilder(
            builder => builder.UseSetting("Efti:GateRetentionMinutes", "15"));
        using HttpClient fifteenDaysClient = fifteenDays.CreateClient().ActingAs(Role.Authority);
        using HttpClient fifteenMinutesClient = fifteenMinutes.CreateClient().ActingAs(Role.Authority);

        //SUT
        List<ConsignmentIdentifiersView> expiredByDefault = await FindAsync(fifteenDaysClient, "ZZ106AA");
        List<ConsignmentIdentifiersView> reachableByDefault = await FindAsync(fifteenMinutesClient, "ZZ105AA");
        List<ConsignmentIdentifiersView> notDelivered = await FindAsync(fifteenMinutesClient, "ZZ101AA");
        string uil = expiredByDefault.Single().Uil;
        using HttpResponseMessage subsetWithFifteenDays = await fifteenDaysClient.GetAsync($"/api/gate/subset?uil={uil}&subsetId=EU03", CancellationToken);
        using HttpResponseMessage subsetWithDefault = await SendAsync(Role.Authority, HttpMethod.Get, $"/api/gate/subset?uil={uil}&subsetId=EU03");

        Assert.That(expiredByDefault, Has.Count.EqualTo(1), "con 15 giorni SH-1006 è ancora sul gate");
        Assert.That(reachableByDefault, Is.Empty, "con 15 minuti SH-1005 è già scaduta");
        Assert.That(notDelivered, Has.Count.EqualTo(1), "una spedizione non consegnata non scade, qualunque sia l'impostazione");
        Assert.That(subsetWithFifteenDays.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((subsetWithDefault.StatusCode, await ReadProblemCodeAsync(subsetWithDefault)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)),
            "un UIL scaduto risponde come uno sconosciuto");
    }

    [Test]
    public async Task AuthorityAccess_AfterAnAuthorityLookedAtAShipment_ShowsItToTheOperator()
    {
        //SETUP
        await FindIdentifiersAsync("ZZ101AA");
        await GetSubsetAsync(_uil, "EU03");
        using HttpResponseMessage refused = await SendAsync(Role.Authority, HttpMethod.Get, "/api/views/shipments/SH-1001");

        //SUT
        List<AuditEventView> accesses = await GetAsync<List<AuditEventView>>(Role.Dispatcher, "/api/shipments/SH-1001/authority-access");
        List<AuditEventView> otherShipment = await GetAsync<List<AuditEventView>>(Role.Dispatcher, "/api/shipments/SH-1002/authority-access");
        using HttpResponseMessage unknown = await SendAsync(Role.Dispatcher, HttpMethod.Get, "/api/shipments/SH-9999/authority-access");

        AuditEventView subsetRead = accesses.First(a => a.Operation == Operation.SubsetRead);
        AuditEventView search = accesses.First(a => a.Operation == Operation.IdentifierSearch);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(accesses.Select(a => a.Role), Is.All.EqualTo(Role.Authority));
        Assert.That(accesses.Select(a => a.Outcome), Is.All.EqualTo(AuditOutcome.Success), "i tentativi rifiutati stanno nell'audit, non fra gli accessi");
        Assert.That((subsetRead.ShipmentCode, subsetRead.Resource, subsetRead.Detail), Is.EqualTo(("SH-1001", _uil, "EU03")));
        Assert.That((search.Resource, search.Detail), Is.EqualTo(("ZZ101AA", "1 risultato")));
        Assert.That(accesses.Select(a => a.ActorName), Is.All.EqualTo(DemoActors.For(Role.Authority).Name));
        Assert.That(otherShipment, Is.Empty, "SH-1002 non è sul gate: nessuna autorità può averla vista");
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Audit_FailedOperation_IsRecordedWithReasonAndTraceId()
    {
        //SUT
        using HttpResponseMessage response = await SendAsync(Role.Dispatcher, HttpMethod.Get, "/api/views/shipments/SH-9999");

        AuditEvent audited = await LastAuditEventAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((audited.Operation, audited.Resource, audited.Outcome, audited.Role), Is.EqualTo((Operation.ShipmentRead, "SH-9999", AuditOutcome.Failed, (Role?)Role.Dispatcher)));
        Assert.That(audited.Reason, Does.StartWith("404").And.Contain("SH-9999"));
        Assert.That(audited.TraceId, Does.Match("^[0-9a-f]{32}$"), "trace id W3C, apribile nel dashboard");
        Assert.That(audited.OccurredAt, Is.EqualTo(Now));
    }

    [Test]
    public async Task Audit_AsAdmin_CanBeFilteredByRoleOperationAndOutcome()
    {
        //SETUP
        using HttpResponseMessage refused = await SendAsync(Role.Authority, HttpMethod.Get, "/api/views/shipments");

        //SUT
        List<AuditEventView> refusedToAuthority = await GetAsync<List<AuditEventView>>(Role.Admin, "/api/audit?role=Authority&outcome=denied&operation=ShipmentList");
        List<AuditEventView> ofShipment = await GetAsync<List<AuditEventView>>(Role.Admin, "/api/audit?shipmentCode=SH-1001");
        using HttpResponseMessage invalidFilter = await SendAsync(Role.Admin, HttpMethod.Get, "/api/audit?outcome=Boh");

        Assert.That(refusedToAuthority, Is.Not.Empty);
        Assert.That(refusedToAuthority.Select(e => (e.Role, e.Operation, e.Outcome)), Is.All.EqualTo(((Role?)Role.Authority, Operation.ShipmentList, AuditOutcome.Denied)));
        Assert.That(refusedToAuthority.Select(e => e.Id), Is.Ordered.Descending, "dal più recente");
        Assert.That(ofShipment, Is.Not.Empty);
        Assert.That(ofShipment.Select(e => e.ShipmentCode), Is.All.EqualTo("SH-1001"));
        Assert.That((invalidFilter.StatusCode, await ReadProblemCodeAsync(invalidFilter)), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationError)));
    }

    [Test]
    public async Task AuditEvent_UpdateOrDelete_IsRefusedByTheDatabase()
    {
        //SETUP
        await using AuditDbContext audit = TrasportoDatabaseInitializer.CreateAudit(_connectionString);
        int before = await audit.Events.CountAsync(CancellationToken);
        AuditEvent tracked = await audit.Events.OrderBy(e => e.Id).FirstAsync(CancellationToken);
        tracked.Reason = "riscritto";

        //SUT
        Assert.That(async () => await audit.SaveChangesAsync(CancellationToken), Throws.TypeOf<DbUpdateException>().With.InnerException.Message.Contains("append-only"));
        Assert.That(async () => await audit.Events.ExecuteDeleteAsync(CancellationToken), Throws.TypeOf<SqlException>().With.Message.Contains("append-only"));
        Assert.That(
            async () => await audit.Database.ExecuteSqlRawAsync("UPDATE audit.AuditEvent SET AuditOutcomeId = 1", CancellationToken),
            Throws.TypeOf<SqlException>().With.Message.Contains("append-only"));

        await using AuditDbContext fresh = TrasportoDatabaseInitializer.CreateAudit(_connectionString);
        Assert.That(before, Is.GreaterThan(0));
        Assert.That(await fresh.Events.CountAsync(CancellationToken), Is.EqualTo(before));
        Assert.That(await fresh.Events.CountAsync(e => e.Reason == "riscritto", CancellationToken), Is.EqualTo(0));
    }

    /// <summary>La richiesta ben formata che esegue ciascuna operazione: è il modo in cui il test percorre la matrice.</summary>
    private Task<HttpResponseMessage> SendAsync(string? apiKey, Operation operation) => operation switch
    {
        Operation.IdentityRead => SendAsync(apiKey, HttpMethod.Get, "/api/me"),
        Operation.ShipmentList => SendAsync(apiKey, HttpMethod.Get, "/api/views/shipments"),
        Operation.ShipmentRead => SendAsync(apiKey, HttpMethod.Get, "/api/views/shipments/SH-1001"),
        Operation.ShipmentImport => SendAsync(apiKey, HttpMethod.Post, "/api/shipments/import", new ImportShipmentsRequest("TO-1001")),
        Operation.DatasetExport => SendAsync(apiKey, HttpMethod.Get, "/api/shipments/SH-1001/dataset.xml"),
        Operation.IdentifierSearch => SendAsync(apiKey, HttpMethod.Get, "/api/gate/identifiers?plate=ZZ101AA"),
        Operation.SubsetRead => SendAsync(apiKey, HttpMethod.Get, $"/api/gate/subset?uil={_uil}&subsetId=EU03"),
        Operation.AuthorityAccessRead => SendAsync(apiKey, HttpMethod.Get, "/api/shipments/SH-1001/authority-access"),
        Operation.AuditRead => SendAsync(apiKey, HttpMethod.Get, "/api/audit"),
        Operation.TmsOrderRead => SendAsync(apiKey, HttpMethod.Get, "/api/shipments/SH-1002/tms-order"),
        Operation.FieldProposalCreate => SendAsync(apiKey, HttpMethod.Post, "/api/shipments/SH-1002/proposals", PostcodeProposal),
        Operation.FieldProposalRead => SendAsync(apiKey, HttpMethod.Get, "/api/shipments/SH-1002/proposals"),
        Operation.FieldProposalDecide => DecideAFreshProposalAsync(apiKey),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Operazione senza richiesta di prova: aggiungerla alla matrice dei test.")
    };

    private static readonly ProposeFieldRequest PostcodeProposal =
        new("consignee/postalAddress/postcode", "69007", "TmsOrder", "TO-1002: senderInstructions", "Il CAP è nelle istruzioni del mittente.");

    /// <summary>
    /// Una decisione ha bisogno di una proposta in attesa: la lascia l'operatore, poi decide (rifiutando, così SH-1002 resta
    /// com'è) chi ha la chiave indicata. Se la decisione viene negata la proposta resta in attesa e la volta dopo si riusa.
    /// </summary>
    private async Task<HttpResponseMessage> DecideAFreshProposalAsync(string? apiKey)
    {
        var proposal = new ProposeFieldRequest("consignee/taxRegistration/id", "FR99999000022", "TmsOrder", "TO-1002: senderInstructions", "TVA nelle istruzioni del mittente.");
        using HttpResponseMessage proposed = await SendAsync(Role.Dispatcher, HttpMethod.Post, "/api/shipments/SH-1002/proposals", proposal);
        ProposeFieldResponse? response = await proposed.Content.ReadFromJsonAsync<ProposeFieldResponse>(TrasportoJson.Options, CancellationToken);

        return await SendAsync(apiKey, HttpMethod.Post, $"/api/proposals/{response!.Proposal.Id}/decision", new ProposalDecisionRequest(ProposalStatus.Rejected, "Prova della matrice."));
    }

    private Task<HttpResponseMessage> SendAsync(Role role, HttpMethod method, string path, object? body = null) =>
        SendAsync(TestApiKeys.For(role), method, path, body);

    private async Task<HttpResponseMessage> SendAsync(string? apiKey, HttpMethod method, string path, object? body = null)
    {
        using HttpRequestMessage request = new HttpRequestMessage(method, path).WithApiKey(apiKey);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: TrasportoJson.Options);
        }

        return await _client.SendAsync(request, CancellationToken);
    }

    private async Task<T> GetAsync<T>(Role role, string path)
    {
        using HttpResponseMessage response = await SendAsync(role, HttpMethod.Get, path);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(TrasportoJson.Options, CancellationToken))!;
    }

    private async Task<IReadOnlyList<ConsignmentIdentifiersView>> FindIdentifiersAsync(string plate) =>
        await GetAsync<List<ConsignmentIdentifiersView>>(Role.Authority, $"/api/gate/identifiers?plate={Uri.EscapeDataString(plate)}");

    private static async Task<List<ConsignmentIdentifiersView>> FindAsync(HttpClient authorityClient, string plate) =>
        (await authorityClient.GetFromJsonAsync<List<ConsignmentIdentifiersView>>($"/api/gate/identifiers?plate={plate}", TrasportoJson.Options, CancellationToken))!;

    private Task<AuthoritySubsetView> GetSubsetAsync(string uil, string subsetId) =>
        GetAsync<AuthoritySubsetView>(Role.Authority, $"/api/gate/subset?uil={uil}&subsetId={subsetId}");

    private async Task<AuditEvent> LastAuditEventAsync()
    {
        await using AuditDbContext audit = TrasportoDatabaseInitializer.CreateAudit(_connectionString);

        return await audit.Events.AsNoTracking().OrderByDescending(e => e.Id).FirstAsync(CancellationToken);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}

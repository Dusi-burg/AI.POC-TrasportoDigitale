using System.Diagnostics;
using System.Net;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Security;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Efti.Mcp.Efti;
using Dusiburg.AI.Trasporto.Efti.Mcp.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Mcp.Tools;
using Dusiburg.AI.Trasporto.Mcp.Hosting;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tools;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Role = Dusiburg.AI.Trasporto.Shared.Security.Role;

namespace Dusiburg.AI.Trasporto.Efti.Mcp.Tests;

/// <summary>
/// I tool di <c>efti-mcp</c> sopra una <c>Efti.Api</c> vera in-process, su un database dedicato: elenco per ruolo, ogni ruolo su
/// ogni tool con l'audit dei rifiuti, dati demo, errori strutturati. Nessuna proposta viene accettata qui: i dataset non
/// cambiano mai.
/// </summary>
public class EftiMcpToolTests
{
    private static readonly string[] ImportedOrders = ["TO-1001", "TO-1002", "TO-1004"];

    /// <summary>
    /// Tool, operazione e ruoli che lo vedono: la tabella normativa di <c>fase-5.md</c>, ripetuta a mano. Cambiare un tool o un
    /// permesso richiede di cambiarlo anche qui.
    /// </summary>
    private static readonly (string Tool, Operation Operation, Role[] Roles)[] Contract =
    [
        (EftiToolNames.SearchShipments, Operation.ShipmentList, [Role.Dispatcher, Role.ClaimsManager, Role.Admin]),
        (EftiToolNames.GetShipmentDataset, Operation.ShipmentRead, [Role.Dispatcher, Role.ClaimsManager, Role.Admin]),
        (EftiToolNames.ListValidationIssues, Operation.ShipmentRead, [Role.Dispatcher, Role.ClaimsManager, Role.Admin]),
        (EftiToolNames.GetTmsOrder, Operation.TmsOrderRead, [Role.Dispatcher]),
        (EftiToolNames.ProposeFieldCompletion, Operation.FieldProposalCreate, [Role.Dispatcher]),
        (EftiToolNames.FindConsignmentsByVehicle, Operation.IdentifierSearch, [Role.Authority]),
        (EftiToolNames.GetAuthoritySubset, Operation.SubsetRead, [Role.Authority])
    ];

    private EftiApiTestHost _api = null!;
    private UpstreamHandler _upstream = null!;
    private WebApplicationFactory<EftiTools> _mcp = null!;

    /// <summary>UIL di SH-1004 (merci pericolose), ottenuto come lo ottiene l'autorità.</summary>
    private string _uil = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    private static IEnumerable<TestCaseData> RolesAndTools() =>
        from role in Enum.GetValues<Role>()
        from tool in Contract
        select new TestCaseData(role, tool.Tool).SetArgDisplayNames(role.ToString(), tool.Tool);

    [OneTimeSetUp]
    public async Task StartApiAndMcpServer()
    {
        _api = await EftiApiTestHost.StartAsync(CancellationToken, ImportedOrders);
        _upstream = new UpstreamHandler(_api.Factory.Server.CreateHandler());

        _mcp = new WebApplicationFactory<EftiTools>().WithWebHostBuilder(builder =>
        {
            builder.UseTestApiKeys();
            builder.UseSetting(EftiApiClient.BaseAddressConfigurationKey, "http://efti-api.test");

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<EftiApiClient>().ConfigurePrimaryHttpMessageHandler(() => _upstream);

                // Stessa pipeline di resilienza di ServiceDefaults, con attese brevi: un'API ferma non deve rallentare i test.
                services.ConfigureAll<HttpStandardResilienceOptions>(options =>
                {
                    options.Retry.MaxRetryAttempts = 1;
                    options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                });
            });
        });

        _uil = (await _api.GetAsync<List<ConsignmentIdentifiersView>>(Role.Authority, "/api/gate/identifiers?plate=ZZ104AA", CancellationToken)).Single().Uil;
    }

    [SetUp]
    public void ApiIsUp() => _upstream.Unavailable = false;

    [OneTimeTearDown]
    public async Task StopEverything()
    {
        await _mcp.DisposeAsync();
        _upstream.Dispose();
        await _api.DisposeAsync();
    }

    [TestCase(Role.Dispatcher)]
    [TestCase(Role.ClaimsManager)]
    [TestCase(Role.Authority)]
    [TestCase(Role.Admin)]
    public async Task ListTools_ByRole_ShowsOnlyTheToolsOfThatRole(Role role)
    {
        //SETUP
        string[] expected = [.. Contract.Where(t => t.Roles.Contains(role)).Select(t => t.Tool)];

        //SUT
        IReadOnlyDictionary<string, McpClientTool> tools = await McpTestClient.ListToolsAsync(_mcp, TestApiKeys.For(role), CancellationToken);

        Assert.That(tools.Keys, Is.EquivalentTo(expected));
        Assert.That(expected, Is.Not.Empty, "ogni ruolo ha almeno un tool");
    }

    [Test]
    public void Contract_EveryToolMapsToItsOperation_AndItsRolesAreThoseOfTheMatrix()
    {
        //SUT
        IReadOnlyDictionary<string, Operation> declared = ToolOperationMap.From(typeof(EftiTools)).Operations;

        Assert.That(declared.Select(d => (d.Key, d.Value)), Is.EquivalentTo(Contract.Select(t => (t.Tool, t.Operation))));

        foreach ((string tool, Operation operation, Role[] roles) in Contract)
        {
            Assert.That(Enum.GetValues<Role>().Where(role => AuthorizationPolicy.IsAllowed(role, operation)), Is.EquivalentTo(roles), tool);
        }
    }

    [Test]
    public async Task ListTools_DeclareParametersOutputSchemaAndTheOnlyWrite()
    {
        //SUT
        var tools = new Dictionary<string, McpClientTool>(await McpTestClient.ListToolsAsync(_mcp, TestApiKeys.For(Role.Dispatcher), CancellationToken));

        foreach (KeyValuePair<string, McpClientTool> authorityTool in await McpTestClient.ListToolsAsync(_mcp, TestApiKeys.For(Role.Authority), CancellationToken))
        {
            tools.Add(authorityTool.Key, authorityTool.Value);
        }

        Assert.That(tools.Keys, Is.EquivalentTo(Contract.Select(t => t.Tool)));
        Assert.That(tools[EftiToolNames.SearchShipments].RequiredParameters(), Is.Empty);
        Assert.That(tools[EftiToolNames.GetShipmentDataset].RequiredParameters(), Is.EqualTo(new[] { "shipmentCode" }));
        Assert.That(tools[EftiToolNames.ListValidationIssues].RequiredParameters(), Is.EqualTo(new[] { "shipmentCode" }));
        Assert.That(tools[EftiToolNames.GetTmsOrder].RequiredParameters(), Is.EqualTo(new[] { "shipmentCode" }));
        Assert.That(tools[EftiToolNames.ProposeFieldCompletion].RequiredParameters(), Is.EquivalentTo(new[] { "shipmentCode", "fieldPath", "value", "source", "rationale" }));
        Assert.That(tools[EftiToolNames.FindConsignmentsByVehicle].RequiredParameters(), Is.EqualTo(new[] { "plate" }));
        Assert.That(tools[EftiToolNames.GetAuthoritySubset].RequiredParameters(), Is.EquivalentTo(new[] { "uil", "subsetId" }));
        Assert.That(tools.Values.Select(t => t.ProtocolTool.OutputSchema), Is.All.Not.Null);
        Assert.That(tools.Values.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint != true).Select(t => t.Name),
            Is.EqualTo(new[] { EftiToolNames.ProposeFieldCompletion }), "un solo tool scrive, e scrive una proposta");
        Assert.That(tools.Keys, Has.None.Contains("decision").And.None.Contains("accept").And.None.Contains("import"), "decidere e importare non sono tool");
    }

    [Test]
    public async Task Mcp_WithoutOrWithUnknownKey_IsRefusedBeforeAnyTool()
    {
        //SUT
        (HttpStatusCode Status, string? Code) withoutKey = await McpTestClient.PostToolsListAsync(_mcp, apiKey: null, CancellationToken);
        (HttpStatusCode Status, string? Code) unknownKey = await McpTestClient.PostToolsListAsync(_mcp, "chiave-inventata", CancellationToken);

        Assert.That(withoutKey, Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)));
        Assert.That(unknownKey, Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)));
    }

    [TestCaseSource(nameof(RolesAndTools))]
    public async Task Tool_CalledByRole_FollowsTheMatrixAndARefusalIsAudited(Role role, string toolName)
    {
        //SETUP
        (_, Operation operation, Role[] roles) = Contract.Single(t => t.Tool == toolName);
        bool allowed = roles.Contains(role);

        //SUT
        CallToolResult result = await CallAsync(role, toolName, WellFormedArguments(toolName));

        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        Assert.That((audited.Role, audited.ActorId, audited.Operation, audited.Outcome),
            Is.EqualTo(((Role?)role, DemoActors.For(role).Id, operation, allowed ? AuditOutcome.Success : AuditOutcome.Denied)),
            "l'API a valle vede il ruolo di chi ha chiamato il tool, non quello del server MCP");

        if (allowed)
        {
            Assert.That(result.IsError, Is.Not.True, () => result.Text());
            Assert.That(result.StructuredContent, Is.Not.Null);
        }
        else
        {
            ToolError error = result.ReadError();

            Assert.That(error.Code, Is.EqualTo(ErrorCodes.Unauthorized));
            Assert.That(error.Message, Does.Contain(role.ToString()).And.Contain(operation.ToString()));
        }
    }

    [Test]
    public async Task SearchShipments_ByStatusAndCustomer_ReturnsTheMatchingShipments()
    {
        //SUT
        SearchShipmentsResult all = (await CallAsync(Role.Dispatcher, EftiToolNames.SearchShipments, new())).ReadStructured<SearchShipmentsResult>();
        SearchShipmentsResult incomplete = (await CallAsync(Role.Admin, EftiToolNames.SearchShipments, new() { ["status"] = "Incomplete" })).ReadStructured<SearchShipmentsResult>();
        SearchShipmentsResult byCustomer = (await CallAsync(Role.ClaimsManager, EftiToolNames.SearchShipments,
            new() { ["customer"] = "Tirreno", ["fromDate"] = "2026-10-01", ["toDate"] = "2026-10-05" })).ReadStructured<SearchShipmentsResult>();
        ToolError invalidStatus = (await CallAsync(Role.Dispatcher, EftiToolNames.SearchShipments, new() { ["status"] = "Boh" })).ReadError();

        Assert.That(all.Shipments.Select(s => s.Code), Is.EquivalentTo(new[] { "SH-1001", "SH-1002", "SH-1004" }));
        Assert.That(incomplete.Shipments.Select(s => (s.Code, s.Status)), Is.EqualTo(new[] { ("SH-1002", DatasetStatus.Incomplete) }));
        Assert.That(incomplete.Shipments.Single().Errors, Is.EqualTo(4));
        Assert.That(byCustomer.Shipments.Select(s => s.Code), Is.EqualTo(new[] { "SH-1004" }));
        Assert.That(invalidStatus.Code, Is.EqualTo(ErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetShipmentDataset_ReturnsTheLatestDatasetWithSchemaNames()
    {
        //SUT
        ShipmentDatasetResult dataset = (await CallAsync(Role.Dispatcher, EftiToolNames.GetShipmentDataset, new() { ["shipmentCode"] = "SH-1001" }))
            .ReadStructured<ShipmentDatasetResult>();
        ToolError unknown = (await CallAsync(Role.Dispatcher, EftiToolNames.GetShipmentDataset, new() { ["shipmentCode"] = "SH-9999" })).ReadError();

        Assert.That((dataset.ShipmentCode, dataset.Version, dataset.Status, dataset.SchemaVersion), Is.EqualTo(("SH-1001", 1, DatasetStatus.Valid, "0.8")));
        Assert.That(dataset.Dataset.GetProperty("consignor").GetProperty("name")[0].GetString(), Is.EqualTo("Officine Meccaniche Brambilla S.r.l."));
        Assert.That(dataset.Dataset.GetProperty("consignor").GetProperty("taxRegistration")[0].GetProperty("id").GetString(), Is.EqualTo("IT01000010007"),
            "l'operatore vede il dataset completo, dati contrattuali compresi");
        Assert.That((unknown.Code, unknown.Message), Is.EqualTo((ErrorCodes.NotFound, "Spedizione SH-9999 non trovata.")));
    }

    [Test]
    public async Task ListValidationIssues_OfScenarioTwo_ReturnsTheFieldsToComplete()
    {
        //SUT
        ValidationIssuesResult issues = (await CallAsync(Role.Dispatcher, EftiToolNames.ListValidationIssues, new() { ["shipmentCode"] = "SH-1002" }))
            .ReadStructured<ValidationIssuesResult>();

        Assert.That((issues.ShipmentCode, issues.DatasetVersion, issues.Status), Is.EqualTo(("SH-1002", 1, DatasetStatus.Incomplete)));
        Assert.That(issues.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => (i.RuleCode, i.FieldPath)), Is.EquivalentTo(new[]
        {
            ("M-CONSIGNEE-ADDRESS", (string?)"consignee/postalAddress/postcode"),
            ("M-ADR-UN-NUMBER", "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID"),
            ("M-ADR-SHIPPING-NAME", "includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName"),
            ("C-WEIGHT-SUM", "grossWeight")
        }));
    }

    [Test]
    public async Task GetTmsOrder_ReturnsTheSourceOrderWithTheSenderInstructions()
    {
        //SUT
        TmsOrderResult order = (await CallAsync(Role.Dispatcher, EftiToolNames.GetTmsOrder, new() { ["shipmentCode"] = "SH-1002" })).ReadStructured<TmsOrderResult>();

        Assert.That((order.ShipmentCode, order.Order.Order.Code), Is.EqualTo(("SH-1002", "TO-1002")));
        Assert.That(order.Order.SenderInstructions, Does.Contain("UN 2014").And.Contain("TVA FR99999000022"));
        Assert.That(order.Order.Consignee.PostalCode, Is.Null, "il TMS non ha il CAP: è il motivo della proposta");
    }

    [Test]
    public async Task ProposeFieldCompletion_RecordsAPendingProposal_OnceAndWithoutTouchingTheDataset()
    {
        //SETUP
        Dictionary<string, object?> arguments = new()
        {
            ["shipmentCode"] = "SH-1002",
            ["fieldPath"] = "includedConsignmentItem[0]/transportDangerousGoods/uNDGID",
            ["value"] = "2014",
            ["source"] = "TmsOrder",
            ["sourceReference"] = "TO-1002: senderInstructions",
            ["rationale"] = "Le istruzioni del mittente riportano UN 2014."
        };

        //SUT
        FieldProposalResult first = (await CallAsync(Role.Dispatcher, EftiToolNames.ProposeFieldCompletion, arguments)).ReadStructured<FieldProposalResult>();
        FieldProposalResult second = (await CallAsync(Role.Dispatcher, EftiToolNames.ProposeFieldCompletion, arguments)).ReadStructured<FieldProposalResult>();

        List<FieldProposalView> proposals = await _api.GetAsync<List<FieldProposalView>>(Role.Dispatcher, "/api/shipments/SH-1002/proposals", CancellationToken);
        FieldProposalView stored = proposals.Single(p => p.Id == first.ProposalId);
        Assert.That((first.Created, first.Status, first.ShipmentCode, first.FieldPath, first.ProposedValue, first.CurrentValue),
            Is.EqualTo((true, ProposalStatus.Pending, "SH-1002", "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "2014", (string?)null)));
        Assert.That((second.Created, second.ProposalId), Is.EqualTo((false, first.ProposalId)));
        Assert.That((stored.Source, stored.SourceReference, stored.ProposedById, stored.Status),
            Is.EqualTo((ProposalSource.TmsOrder, "TO-1002: senderInstructions", DemoActors.For(Role.Dispatcher).Id, ProposalStatus.Pending)));
        Assert.That(await _api.DatasetsAsync("SH-1002", CancellationToken), Has.Count.EqualTo(1));
    }

    [TestCase("SH-1002", "carrier/name", "Altro Vettore S.r.l.", "TmsOrder", ErrorCodes.ValidationError, "Campi completabili")]
    [TestCase("SH-1002", "consignee/postalAddress/countryCode", "Francia", "TmsOrder", ErrorCodes.ValidationError, "ISO")]
    [TestCase("SH-1002", "consignee/postalAddress/postcode", "69007", "Sentito dire", ErrorCodes.ValidationError, "TmsOrder")]
    [TestCase("SH-1002", "consignee/postalAddress/cityName", "Lyon", "TmsOrder", ErrorCodes.Conflict, "ha già il valore")]
    [TestCase("SH-9999", "consignee/postalAddress/postcode", "69007", "TmsOrder", ErrorCodes.NotFound, "SH-9999")]
    public async Task ProposeFieldCompletion_ThatCannotBecomeAProposal_ReturnsAStructuredErrorThatExplainsWhy(
        string shipmentCode, string fieldPath, string value, string source, string expectedCode, string expectedInMessage)
    {
        //SUT
        CallToolResult result = await CallAsync(Role.Dispatcher, EftiToolNames.ProposeFieldCompletion, new()
        {
            ["shipmentCode"] = shipmentCode,
            ["fieldPath"] = fieldPath,
            ["value"] = value,
            ["source"] = source,
            ["rationale"] = "Prova."
        });

        ToolError error = result.ReadError();
        Assert.That(error.Code, Is.EqualTo(expectedCode));
        Assert.That(error.Message, Does.Contain(expectedInMessage));
    }

    [Test]
    public async Task Authority_FromAPlateToASubset_SeesOnlyIdentifiersAndTheSubset()
    {
        //SUT
        ConsignmentsByVehicleResult found = (await CallAsync(Role.Authority, EftiToolNames.FindConsignmentsByVehicle, new() { ["plate"] = "zz-104 aa" }))
            .ReadStructured<ConsignmentsByVehicleResult>();
        ConsignmentsByVehicleResult incomplete = (await CallAsync(Role.Authority, EftiToolNames.FindConsignmentsByVehicle, new() { ["plate"] = "ZZ102AA" }))
            .ReadStructured<ConsignmentsByVehicleResult>();
        string uil = found.Consignments.Single().Uil;
        AuthoritySubsetView dangerousGoods = (await CallAsync(Role.Authority, EftiToolNames.GetAuthoritySubset, new() { ["uil"] = uil, ["subsetId"] = "EU05a" }))
            .ReadStructured<AuthoritySubsetView>();
        ToolError otherSubset = (await CallAsync(Role.Authority, EftiToolNames.GetAuthoritySubset, new() { ["uil"] = uil, ["subsetId"] = "EU01" })).ReadError();
        ToolError unknownUil = (await CallAsync(Role.Authority, EftiToolNames.GetAuthoritySubset,
            new() { ["uil"] = "it-gate-sim:dusiburg-trasporto:00000000-0000-0000-0000-000000000001", ["subsetId"] = "EU03" })).ReadError();

        string identifiers = found.Consignments.Single().Identifiers.GetRawText();
        Assert.That(uil, Is.EqualTo(_uil));
        Assert.That(identifiers, Does.Contain("ZZ104AA").And.Not.Contain("Tirreno").And.Not.Contain("SH-1004").And.Not.Contain("1263"));
        Assert.That(incomplete.Consignments, Is.Empty, "una spedizione incompleta non è sul gate, e non trovarla non è un errore");
        Assert.That((dangerousGoods.Uil, dangerousGoods.SubsetId), Is.EqualTo((uil, "EU05a")));
        Assert.That(dangerousGoods.Dataset.GetProperty("includedConsignmentItem")[0].GetProperty("transportDangerousGoods")[0].GetProperty("uNDGID").GetString(), Is.EqualTo("1263"));
        Assert.That(dangerousGoods.Dataset.GetRawText(), Does.Not.Contain("taxRegistration").And.Not.Contain("applicableServiceCharge"));
        Assert.That(otherSubset.Code, Is.EqualTo(ErrorCodes.ValidationError));
        Assert.That(unknownUil.Code, Is.EqualTo(ErrorCodes.NotFound));
    }

    [Test]
    public async Task Tool_WithoutARequiredArgument_ReturnsAValidationError()
    {
        //SUT
        CallToolResult result = await CallAsync(Role.Dispatcher, EftiToolNames.GetShipmentDataset, new());

        Assert.That(result.ReadError().Code, Is.EqualTo(ErrorCodes.ValidationError));
    }

    [Test]
    public async Task Tool_WhenTheApiIsDown_ReturnsUpstreamUnavailableWithoutDetails()
    {
        //SETUP
        _upstream.Unavailable = true;

        //SUT
        CallToolResult result = await CallAsync(Role.Dispatcher, EftiToolNames.GetShipmentDataset, new() { ["shipmentCode"] = "SH-1001" });

        ToolError error = result.ReadError();
        Assert.That(error.Code, Is.EqualTo(ErrorCodes.UpstreamUnavailable));
        Assert.That(error.Message, Does.Not.Contain("finta"), "il messaggio per il modello non porta dettagli interni");
    }

    [Test]
    public async Task ToolCall_OpensASpanWithToolRoleAndOutcome()
    {
        //SETUP
        List<Activity> spans = [];
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TrasportoTelemetry.Sources.McpEfti,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (spans)
                {
                    spans.Add(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        //SUT
        await CallAsync(Role.Admin, EftiToolNames.GetShipmentDataset, new() { ["shipmentCode"] = "SH-1001" });
        await CallAsync(Role.Admin, EftiToolNames.GetTmsOrder, new() { ["shipmentCode"] = "SH-1001" });

        var tags = spans
            .Where(s => (string?)s.GetTagItem(TrasportoTelemetry.Attributes.ActorRole) == nameof(Role.Admin))
            .Select(s => (s.DisplayName, Tool: (string?)s.GetTagItem(TrasportoTelemetry.Attributes.ToolName), Outcome: (string?)s.GetTagItem(TrasportoTelemetry.Attributes.ToolOutcome)))
            .ToList();
        Assert.That(tags, Does.Contain(($"mcp.tool {EftiToolNames.GetShipmentDataset}", EftiToolNames.GetShipmentDataset, "ok")));
        Assert.That(tags, Does.Contain(($"mcp.tool {EftiToolNames.GetTmsOrder}", EftiToolNames.GetTmsOrder, $"error:{ErrorCodes.Unauthorized}")));
    }

    [Test]
    public async Task EveryTool_CalledByWhoMayCallIt_LeavesEveryDatasetAtItsFirstVersion()
    {
        //SUT
        foreach ((string tool, _, Role[] roles) in Contract)
        {
            CallToolResult result = await CallAsync(roles[0], tool, WellFormedArguments(tool));

            Assert.That(result.IsError, Is.Not.True, () => $"{tool}: {result.Text()}");
        }

        foreach (string orderCode in ImportedOrders)
        {
            string shipmentCode = orderCode.Replace("TO-", "SH-", StringComparison.Ordinal);

            Assert.That((await _api.DatasetsAsync(shipmentCode, CancellationToken)).Select(d => d.Version), Is.EqualTo(new[] { 1 }), shipmentCode);
        }
    }

    /// <summary>Gli argomenti con cui ciascun tool riesce, se chi chiama ne ha il permesso: è il modo in cui i test percorrono la matrice.</summary>
    private Dictionary<string, object?> WellFormedArguments(string toolName) => toolName switch
    {
        EftiToolNames.SearchShipments => new() { ["status"] = "Valid" },
        EftiToolNames.GetShipmentDataset or EftiToolNames.ListValidationIssues or EftiToolNames.GetTmsOrder => new() { ["shipmentCode"] = "SH-1002" },
        EftiToolNames.ProposeFieldCompletion => new()
        {
            ["shipmentCode"] = "SH-1002",
            ["fieldPath"] = "consignee/postalAddress/postcode",
            ["value"] = "69007",
            ["source"] = "TmsOrder",
            ["rationale"] = "Il CAP è nelle istruzioni del mittente."
        },
        EftiToolNames.FindConsignmentsByVehicle => new() { ["plate"] = "ZZ104AA" },
        EftiToolNames.GetAuthoritySubset => new() { ["uil"] = _uil, ["subsetId"] = "EU03" },
        _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, "Tool senza argomenti di prova: aggiungerlo alla matrice dei test.")
    };

    private Task<CallToolResult> CallAsync(Role role, string toolName, Dictionary<string, object?> arguments) =>
        McpTestClient.CallToolAsync(_mcp, TestApiKeys.For(role), toolName, arguments, CancellationToken);
}

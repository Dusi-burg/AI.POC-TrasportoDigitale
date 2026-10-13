using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Agents.Tests.Support;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Efti.Mcp.Efti;
using Dusiburg.AI.Trasporto.Efti.Mcp.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Mcp.Tools;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tools;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Dusiburg.AI.Trasporto.Agents.Tests.Support.ScriptedChatClient;
using Operation = Dusiburg.AI.Trasporto.Shared.Security.Operation;
using Role = Dusiburg.AI.Trasporto.Shared.Security.Role;

namespace Dusiburg.AI.Trasporto.Agents.Tests;

/// <summary>
/// Un giro completo: il servizio <c>Agents</c> con il client MCP vero, sopra <c>efti-mcp</c> ed <c>Efti.Api</c> vere in-process
/// e un database dedicato. Solo il modello è a copione. Verifica ciò che i doppi non possono dire: la chiave di chi avvia
/// l'agente arriva fino all'audit, le proposte sono nel database, il dataset non cambia.
/// </summary>
public class AgentsEndToEndTests
{
    private EftiApiTestHost _api = null!;
    private UpstreamHandler _toApi = null!;
    private WebApplicationFactory<EftiTools> _mcp = null!;
    private UpstreamHandler _toMcp = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task StartApiAndMcpServer()
    {
        _api = await EftiApiTestHost.StartAsync(CancellationToken, "TO-1002", "TO-1004");
        _toApi = new UpstreamHandler(_api.Factory.Server.CreateHandler());

        _mcp = new WebApplicationFactory<EftiTools>().WithWebHostBuilder(builder =>
        {
            builder.UseTestApiKeys();
            builder.UseSetting(EftiApiClient.BaseAddressConfigurationKey, "http://efti-api.test");
            builder.ConfigureTestServices(services => services.AddHttpClient<EftiApiClient>().ConfigurePrimaryHttpMessageHandler(() => _toApi));
        });

        _toMcp = new UpstreamHandler(_mcp.Server.CreateHandler());
    }

    [OneTimeTearDown]
    public async Task StopEverything()
    {
        _toMcp.Dispose();
        await _mcp.DisposeAsync();
        _toApi.Dispose();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task MappingRun_AsDispatcher_LeavesPendingProposalsInTheDatabaseAndDoesNotTouchTheDataset()
    {
        //SETUP
        await using WebApplicationFactory<AgentsEntryPoint> agents = Agents(
            Call(EftiToolNames.ListValidationIssues, new()),
            Call(EftiToolNames.GetTmsOrder, new()),
            Propose("consignee/postalAddress/postcode", "69007"),
            Propose("includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "2014"),
            Propose("grossWeight", "8.840"),
            Propose("grossWeight", "8840"),
            Propose("carrier/name", "Papeteries du Rhône SAS"),
            Text("Proposti CAP, numero ONU e peso lordo."));
        using HttpClient client = agents.CreateClient().ActingAs(Role.Dispatcher);

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/agents/mapping/run", new MappingRunRequest("SH-1002"), CancellationToken);
        MappingRunResult? result = await response.Content.ReadFromJsonAsync<MappingRunResult>(TrasportoJson.Options, CancellationToken);

        List<FieldProposalView> stored = await _api.GetAsync<List<FieldProposalView>>(Role.Dispatcher, "/api/shipments/SH-1002/proposals", CancellationToken);
        AuditEvent lastAudit = await LastAuditOfAsync(Operation.FieldProposalCreate);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.DatasetStatus, Is.EqualTo(DatasetStatus.Incomplete));
        Assert.That(result.Proposals.Select(p => (p.FieldPath, p.ProposedValue, p.Created)), Is.EqualTo(new[]
        {
            ("consignee/postalAddress/postcode", "69007", true),
            ("includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "2014", true),
            ("grossWeight", "8840", true)
        }));
        Assert.That(result.Discarded.Select(d => (d.FieldPath, d.Value)), Is.EqualTo(new[] { ((string?)"grossWeight", (string?)"8.840"), ("carrier/name", "Papeteries du Rhône SAS") }));
        Assert.That(result.Discarded[0].Reason, Does.Contain("8840"), "scartata dal codice: non è nell'ordine");
        Assert.That(result.Discarded[1].Reason, Does.StartWith(ErrorCodes.ValidationError).And.Contain("non è completabile"), "scartata dall'API: il valore è nell'ordine, ma il campo non si propone");
        Assert.That(stored.Select(p => (p.FieldPath, p.ProposedValue, p.Status, p.Source, p.ProposedById)), Is.EquivalentTo(result.Proposals.Select(p =>
            (p.FieldPath, p.ProposedValue, ProposalStatus.Pending, ProposalSource.TmsOrder, Dusiburg.AI.Trasporto.Shared.Security.DemoActors.For(Role.Dispatcher).Id))));
        Assert.That((lastAudit.Role, lastAudit.ShipmentCode), Is.EqualTo(((Role?)Role.Dispatcher, "SH-1002")), "nell'audit c'è l'operatore, non l'agente");
        Assert.That((await _api.DatasetsAsync("SH-1002", CancellationToken)).Select(d => d.Version), Is.EqualTo(new[] { 1 }), "nessuna versione nuova del dataset");
    }

    [Test]
    public async Task AuthorityAsk_AsAuthority_AnswersFromTheRealSubsetAndLeavesTheAccessInTheAudit()
    {
        //SETUP
        string uil = (await _api.GetAsync<List<ConsignmentIdentifiersView>>(Role.Authority, "/api/gate/identifiers?plate=ZZ104AA", CancellationToken)).Single().Uil;
        await using WebApplicationFactory<AgentsEntryPoint> agents = Agents(
            Call(EftiToolNames.FindConsignmentsByVehicle, new()),
            Call(EftiToolNames.GetAuthoritySubset, new() { ["uil"] = uil, ["subsetId"] = "EU05a" }),
            Text("A bordo UN 1263 PITTURE."),
            // Il modello ha letto un solo sottoinsieme: l'altro lo legge il codice dal server vero, e la domanda si ripropone.
            Text("A bordo UN 1263 PITTURE."),
            Text("""{"answer":"Sì: UN 1263 PITTURE, classe 3. La partita IVA del mittente è IT01000040004.","dataAvailable":true,"quotedValues":["1263","PITTURE","IT01000040004"]}"""));
        using HttpClient client = agents.CreateClient().ActingAs(Role.Authority);

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/agents/authority/ask", new AuthorityAskRequest("zz-104 aa", "Ci sono merci pericolose a bordo?"), CancellationToken);
        AuthorityAnswerResult? result = await response.Content.ReadFromJsonAsync<AuthorityAnswerResult>(TrasportoJson.Options, CancellationToken);

        List<AuditEventView> accesses = await _api.GetAsync<List<AuditEventView>>(Role.Dispatcher, "/api/shipments/SH-1004/authority-access", CancellationToken);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result!.Uils, Is.EqualTo(new[] { uil }));
        Assert.That(result.SubsetsRead, Is.EqualTo(new[] { new SubsetReading(uil, "EU05a"), new SubsetReading(uil, "EU03") }), "il secondo l'ha letto il codice");
        Assert.That(result.UnsupportedValues, Is.EqualTo(new[] { "IT01000040004" }), "la partita IVA non è nel sottoinsieme: il modello l'ha inventata, e il codice lo dice");
        Assert.That(result.DataAvailable, Is.False);
        Assert.That(accesses.Select(a => (a.Operation, a.Role)), Does.Contain((Operation.SubsetRead, (Role?)Role.Authority)), "l'operatore vede che l'autorità ha consultato la spedizione");
    }

    [Test]
    public async Task MappingRun_AsAuthority_IsForbiddenBecauseTheRealServerShowsItNoMappingTool()
    {
        //SETUP
        var chat = new ScriptedChatClient(Text("Non dovrei essere chiamato."));
        await using WebApplicationFactory<AgentsEntryPoint> agents = Agents(chat);
        using HttpClient client = agents.CreateClient().ActingAs(Role.Authority);

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/agents/mapping/run", new MappingRunRequest("SH-1002"), CancellationToken);

        Assert.That((response.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(response, CancellationToken)), Is.EqualTo((HttpStatusCode.Forbidden, ErrorCodes.Unauthorized)));
        Assert.That(chat.RequestCount, Is.EqualTo(0));
    }

    [Test]
    public async Task MappingRun_WhenTheToolServerIsDown_ReturnsUpstreamUnavailable()
    {
        //SETUP
        await using WebApplicationFactory<AgentsEntryPoint> agents = Agents(Text("Non dovrei essere chiamato."));
        using HttpClient client = agents.CreateClient().ActingAs(Role.Dispatcher);
        _toMcp.Unavailable = true;

        try
        {
            //SUT
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/agents/mapping/run", new MappingRunRequest("SH-1002"), CancellationToken);

            Assert.That((response.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(response, CancellationToken)),
                Is.EqualTo((HttpStatusCode.ServiceUnavailable, ErrorCodes.UpstreamUnavailable)));
        }
        finally
        {
            _toMcp.Unavailable = false;
        }
    }

    private WebApplicationFactory<AgentsEntryPoint> Agents(params Func<IReadOnlyList<ChatMessage>, ChatResponse>[] script) => Agents(new ScriptedChatClient(script));

    /// <summary>Il servizio vero con il catalogo MCP vero: il suo client HTTP arriva al server MCP in-process.</summary>
    private WebApplicationFactory<AgentsEntryPoint> Agents(ScriptedChatClient chat) =>
        new WebApplicationFactory<AgentsEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseTestApiKeys();
            builder.UseSetting(McpToolCatalog.UrlConfigurationKey, "http://efti-mcp.test/mcp");

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(McpToolCatalog.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _toMcp);
                services.RemoveAll<IChatClientFactory>();
                services.AddSingleton<IChatClientFactory>(new ScriptedChatClientFactory(chat));

                // Stessa pipeline di resilienza di ServiceDefaults, con attese brevi: un server fermo non deve rallentare i test.
                services.ConfigureAll<Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions>(options =>
                {
                    options.Retry.MaxRetryAttempts = 1;
                    options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                });
            });
        });

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse> Propose(string fieldPath, string value) =>
        Call(EftiToolNames.ProposeFieldCompletion, new() { ["fieldPath"] = fieldPath, ["value"] = value, ["rationale"] = "Valore riportato nell'ordine del TMS." });

    private async Task<AuditEvent> LastAuditOfAsync(Operation operation)
    {
        await using Dusiburg.AI.Trasporto.Efti.Data.AuditDbContext audit = Dusiburg.AI.Trasporto.DbInit.TrasportoDatabaseInitializer.CreateAudit(_api.ConnectionString);

        return audit.Events.Where(e => e.Operation == operation && e.Outcome == Dusiburg.AI.Trasporto.Shared.Security.AuditOutcome.Success).OrderByDescending(e => e.Id).First();
    }
}

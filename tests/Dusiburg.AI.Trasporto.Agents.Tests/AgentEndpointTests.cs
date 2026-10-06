using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Agents.Specs;
using Dusiburg.AI.Trasporto.Agents.Tests.Support;
using Dusiburg.AI.Trasporto.Agents.Tools;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tools;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Dusiburg.AI.Trasporto.Agents.Tests.Support.ScriptedChatClient;

namespace Dusiburg.AI.Trasporto.Agents.Tests;

/// <summary>
/// Endpoint del servizio <c>Agents</c> con modello a copione e server MCP finto: chiave di chi chiama, ruolo senza i tool,
/// risposta unica e stream SSE con i passi.
/// </summary>
public class AgentEndpointTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task MappingRun_AsDispatcher_ReturnsTheRunResult()
    {
        //SETUP
        await using WebApplicationFactory<AgentsEntryPoint> factory = Factory(MappingScript(), out FakeEftiTools tools);
        using HttpClient client = factory.CreateClient().ActingAs(Role.Dispatcher);

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/agents/mapping/run", new MappingRunRequest(" SH-1002 "), CancellationToken);
        MappingRunResult? result = await response.Content.ReadFromJsonAsync<MappingRunResult>(TrasportoJson.Options, CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((result!.ShipmentCode, result.Proposals.Count, result.Model.Provider), Is.EqualTo(("SH-1002", 1, "stub")));
        Assert.That(tools.OpenedWithKeys, Is.EqualTo(new[] { TestApiKeys.For(Role.Dispatcher) }));
    }

    [Test]
    public async Task Agents_WithoutOrWithUnknownKey_AreRefusedBeforeAnyModelCall()
    {
        //SETUP
        var chat = new ScriptedChatClient(MappingScript());
        await using WebApplicationFactory<AgentsEntryPoint> factory = Factory(chat, out FakeEftiTools tools);
        using HttpClient client = factory.CreateClient();
        using var unknownKey = new HttpRequestMessage(HttpMethod.Post, "/api/agents/authority/ask/stream") { Content = JsonContent.Create(new AuthorityAskRequest("ZZ104AA", "Merci pericolose?")) };
        unknownKey.WithApiKey("chiave-inventata");

        //SUT
        using HttpResponseMessage withoutKey = await client.PostAsJsonAsync("/api/agents/mapping/run", new MappingRunRequest("SH-1002"), CancellationToken);
        using HttpResponseMessage withUnknownKey = await client.SendAsync(unknownKey, CancellationToken);

        Assert.That((withoutKey.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(withoutKey, CancellationToken)), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)));
        Assert.That((withUnknownKey.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(withUnknownKey, CancellationToken)), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)));
        Assert.That((chat.RequestCount, tools.OpenedWithKeys.Count), Is.EqualTo((0, 0)));
    }

    [Test]
    public async Task Agents_StartedByARoleWithoutTheirTools_AreForbidden()
    {
        //SETUP
        var chat = new ScriptedChatClient(MappingScript());
        await using WebApplicationFactory<AgentsEntryPoint> factory = Factory(chat, out _);
        using HttpClient authority = factory.CreateClient().ActingAs(Role.Authority);
        using HttpClient dispatcher = factory.CreateClient().ActingAs(Role.Dispatcher);

        //SUT
        using HttpResponseMessage mappingByAuthority = await authority.PostAsJsonAsync("/api/agents/mapping/run", new MappingRunRequest("SH-1002"), CancellationToken);
        using HttpResponseMessage authorityByDispatcher = await dispatcher.PostAsJsonAsync("/api/agents/authority/ask", new AuthorityAskRequest("ZZ104AA", "Merci pericolose?"), CancellationToken);

        Assert.That((mappingByAuthority.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(mappingByAuthority, CancellationToken)), Is.EqualTo((HttpStatusCode.Forbidden, ErrorCodes.Unauthorized)));
        Assert.That((authorityByDispatcher.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(authorityByDispatcher, CancellationToken)), Is.EqualTo((HttpStatusCode.Forbidden, ErrorCodes.Unauthorized)));
        Assert.That(chat.RequestCount, Is.EqualTo(0));
    }

    [TestCase("/api/agents/mapping/run", """{}""")]
    [TestCase("/api/agents/mapping/run/stream", """{"shipmentCode":"  "}""")]
    [TestCase("/api/agents/authority/ask", """{"plate":"ZZ104AA"}""")]
    [TestCase("/api/agents/authority/ask/stream", """{"question":"Merci pericolose?"}""")]
    public async Task Agents_RequestWithoutWhatTheAgentNeeds_IsAValidationError(string path, string body)
    {
        //SETUP
        await using WebApplicationFactory<AgentsEntryPoint> factory = Factory(MappingScript(), out _);
        using HttpClient client = factory.CreateClient().ActingAs(Role.Dispatcher);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        //SUT
        using HttpResponseMessage response = await client.PostAsync(path, content, CancellationToken);

        Assert.That((response.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(response, CancellationToken)), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationError)));
    }

    [Test]
    public async Task MappingStream_SendsOneEventPerStepAndThenTheResult()
    {
        //SETUP
        await using WebApplicationFactory<AgentsEntryPoint> factory = Factory(MappingScript(), out _);
        using HttpClient client = factory.CreateClient().ActingAs(Role.Dispatcher);

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/agents/mapping/run/stream", new MappingRunRequest("SH-1002"), CancellationToken);
        List<(string Event, JsonElement Data)> events = ParseEvents(await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        Assert.That(events.Select(e => e.Event), Is.EqualTo(new[] { AgentEvents.Step, AgentEvents.Step, AgentEvents.Step, AgentEvents.Step, AgentEvents.Result }));
        Assert.That(events.Take(4).Select(e => (e.Data.GetProperty("kind").GetString(), e.Data.GetProperty("tool").GetString())), Is.EqualTo(new[]
        {
            (AgentStepKinds.Model, (string?)null),
            (AgentStepKinds.Tool, EftiToolNames.ListValidationIssues),
            (AgentStepKinds.Tool, EftiToolNames.GetTmsOrder),
            (AgentStepKinds.Tool, EftiToolNames.ProposeFieldCompletion)
        }));
        Assert.That(events[^1].Data.GetProperty("proposals")[0].GetProperty("proposedValue").GetString(), Is.EqualTo("69007"));
        Assert.That(events[^1].Data.GetProperty("datasetStatus").GetString(), Is.EqualTo("Incomplete"), "enum come stringhe anche nello stream");
    }

    [Test]
    public async Task AuthorityStream_WhenTheRoleLacksTheTools_SendsTheProblemAsAnErrorEvent()
    {
        //SETUP
        await using WebApplicationFactory<AgentsEntryPoint> factory = Factory(MappingScript(), out _);
        using HttpClient client = factory.CreateClient().ActingAs(Role.Dispatcher);

        //SUT
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/agents/authority/ask/stream", new AuthorityAskRequest("ZZ104AA", "Merci pericolose?"), CancellationToken);
        List<(string Event, JsonElement Data)> events = ParseEvents(await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "lo stream è già partito: l'errore viaggia come evento");
        Assert.That(events.Select(e => e.Event), Is.EqualTo(new[] { AgentEvents.Error }));
        Assert.That((events[0].Data.GetProperty("status").GetInt32(), events[0].Data.GetProperty("code").GetString()), Is.EqualTo((403, ErrorCodes.Unauthorized)));
    }

    [Test]
    public void Specs_OfBothAgents_HaveTheSectionsTheCodeUses()
    {
        //SUT
        IReadOnlyList<AgentSpec> specs = AgentSpec.LoadAll();

        AgentSpec authority = specs.Single(s => s.Name == "AuthorityAgent");
        AgentSpec mapping = specs.Single(s => s.Name == "MappingAgent");
        Assert.That(specs, Has.Count.EqualTo(2));
        Assert.That(mapping.Section("Task"), Does.Contain("{shipmentCode}"));
        Assert.That(authority.Section("Task"), Does.Contain("{plate}").And.Contain("{question}"));
        Assert.That(authority.Section("Outcome request"), Does.Contain("quotedValues"));
        Assert.That(authority.Section("Outcome retry"), Is.Not.Empty);
        Assert.That(mapping.Instructions + authority.Instructions, Does.Not.Contain("non inviate al modello").And.Not.Contain("Il codice non si fida"),
            "le note per chi legge non entrano nel prompt");
    }

    private static Func<IReadOnlyList<ChatMessage>, ChatResponse>[] MappingScript() =>
    [
        Call(EftiToolNames.ListValidationIssues, new()),
        Call(EftiToolNames.GetTmsOrder, new()),
        Call(EftiToolNames.ProposeFieldCompletion, new() { ["fieldPath"] = "consignee/postalAddress/postcode", ["value"] = "69007", ["rationale"] = "Nelle istruzioni." }),
        Text("Ho proposto il CAP.")
    ];

    private static WebApplicationFactory<AgentsEntryPoint> Factory(
        Func<IReadOnlyList<ChatMessage>, ChatResponse>[] script, out FakeEftiTools tools) =>
        Factory(new ScriptedChatClient(script), out tools);

    /// <summary>Il servizio vero, con il modello a copione e il server MCP finto al posto di Ollama e di efti-mcp.</summary>
    private static WebApplicationFactory<AgentsEntryPoint> Factory(ScriptedChatClient chat, out FakeEftiTools tools)
    {
        FakeEftiTools fake = tools = new FakeEftiTools();

        return new WebApplicationFactory<AgentsEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseTestApiKeys();

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAgentToolCatalog>();
                services.AddSingleton<IAgentToolCatalog>(fake);
                services.RemoveAll<IChatClientFactory>();
                services.AddSingleton<IChatClientFactory>(new ScriptedChatClientFactory(chat));
            });
        });
    }

    /// <summary>Eventi di uno stream SSE: nome e dati JSON, nell'ordine di arrivo.</summary>
    internal static List<(string Event, JsonElement Data)> ParseEvents(string stream)
    {
        List<(string, JsonElement)> events = [];

        foreach (string block in stream.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string[] lines = block.Split('\n');
            string name = lines.Single(l => l.StartsWith("event:", StringComparison.Ordinal))["event:".Length..].Trim();
            string data = string.Concat(lines.Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l["data:".Length..].TrimStart()));

            using JsonDocument document = JsonDocument.Parse(data);

            events.Add((name, document.RootElement.Clone()));
        }

        return events;
    }
}

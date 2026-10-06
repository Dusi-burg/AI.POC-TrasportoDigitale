using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Web.Agents;
using Dusiburg.AI.Trasporto.Web.Api;
using Dusiburg.AI.Trasporto.Web.Tests.Support;

namespace Dusiburg.AI.Trasporto.Web.Tests;

/// <summary>
/// Gli stream degli agenti inoltrati dalla Web (fase-8.md §5): eventi inalterati, chiave del ruolo scelto, token antiforgery
/// richiesto, errori a monte restituiti con il loro codice.
/// </summary>
public class AgentStreamTests
{
    private const string MappingUpstream = "/" + AgentsApiClient.MappingStreamPath;
    private const string AuthorityUpstream = "/" + AgentsApiClient.AuthorityStreamPath;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [Test]
    public async Task Mapping_ForwardsEventsUnchanged_WithRoleKey()
    {
        //SETUP
        string sse = TestData.Sse(AgentEvents.Step, new AgentStep(AgentStepKinds.Tool, "get_validation_issues", "ok", "Letti i problemi di SH-1002", 35))
            + TestData.Sse(AgentEvents.Result, TestData.MappingResult());
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Stream(MappingUpstream, sse));

        //SUT
        HttpResponseMessage response = await PostAsync(host, AgentStreamEndpoints.MappingPath, new MappingRunRequest("SH-1002"), withToken: true);
        string body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        Assert.That(body, Is.EqualTo(sse), "lo stream arriva al browser così com'è");

        DownstreamCall call = host.Api.CallsTo(MappingUpstream).Single();

        Assert.That(call.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(call.ApiKey, Is.EqualTo(WebTestHost.KeyOf(Role.Dispatcher)));
        Assert.That(call.Body, Is.EqualTo("{\"shipmentCode\":\"SH-1002\"}"));
    }

    [Test]
    public async Task Authority_ForwardsQuestion_WithAuthorityKey()
    {
        //SETUP
        string sse = TestData.Sse(AgentEvents.Error, new { title = "Servizio a valle non disponibile", detail = "Il modello non risponde.", status = 503 });
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Stream(AuthorityUpstream, sse));
        await host.SelectRoleAsync(Role.Authority, CancellationToken);

        //SUT
        HttpResponseMessage response = await PostAsync(host, AgentStreamEndpoints.AuthorityPath, new AuthorityAskRequest("ZZ104AA", "Trasporta merci pericolose?"), withToken: true);
        string body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(body, Is.EqualTo(sse), "anche l'evento di errore passa inalterato");

        DownstreamCall call = host.Api.CallsTo(AuthorityUpstream).Single();

        Assert.That(call.ApiKey, Is.EqualTo(WebTestHost.KeyOf(Role.Authority)));
        Assert.That(call.Body, Does.Contain("\"plate\":\"ZZ104AA\"").And.Contain("\"question\":\"Trasporta merci pericolose?\""));
    }

    [TestCase(AgentStreamEndpoints.MappingPath)]
    [TestCase(AgentStreamEndpoints.AuthorityPath)]
    public async Task WithoutAntiforgeryToken_NoAgentStarts(string path)
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi().Stream(MappingUpstream, "x").Stream(AuthorityUpstream, "x"));

        //SUT
        HttpResponseMessage response = await PostAsync(host, path, new { shipmentCode = "SH-1002", plate = "ZZ104AA", question = "?" }, withToken: false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(host.Api.Calls.Where(call => call.Method == HttpMethod.Post), Is.Empty, "senza token la Web non chiama il servizio degli agenti");
    }

    [Test]
    public async Task UpstreamRefusal_KeepsStatusAndProblem()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi()
            .Problem(HttpMethod.Post, MappingUpstream, HttpStatusCode.Forbidden, "Operazione non consentita al ruolo", "Il ruolo ClaimsManager non ha i tool di questo agente."));
        await host.SelectRoleAsync(Role.ClaimsManager, CancellationToken);

        //SUT
        HttpResponseMessage response = await PostAsync(host, AgentStreamEndpoints.MappingPath, new MappingRunRequest("SH-1002"), withToken: true);
        string body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(body, Does.Contain("Il ruolo ClaimsManager non ha i tool di questo agente."));
    }

    [Test]
    public async Task AgentsUnreachable_Returns503Problem()
    {
        //SETUP
        await using WebTestHost host = WebTestHost.Start(new PreparedApi { UnreachablePath = MappingUpstream });

        //SUT
        HttpResponseMessage response = await PostAsync(host, AgentStreamEndpoints.MappingPath, new MappingRunRequest("SH-1002"), withToken: true);
        string body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(body, Does.Contain("Il servizio degli agenti non risponde"));
    }

    /// <summary>La stessa richiesta dello script della pagina: JSON, con il token nell'intestazione.</summary>
    private static async Task<HttpResponseMessage> PostAsync(WebTestHost host, string path, object request, bool withToken)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request, request.GetType(), options: TrasportoJson.Options) };

        if (withToken)
        {
            message.Headers.Add("RequestVerificationToken", await host.TokenAsync(WebTestHost.TokenPage, CancellationToken));
        }

        return await host.Client.SendAsync(message, CancellationToken);
    }
}

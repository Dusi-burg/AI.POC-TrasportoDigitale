using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Shared.Health;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>Endpoint della Fase 1 con probe finti: nessun SQL, nessun Ollama.</summary>
public class HealthEndpointTests
{
    private WebApplicationFactory<EftiApiEntryPoint> _factory = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public void CreateFactory()
    {
        _factory = new WebApplicationFactory<EftiApiEntryPoint>().WithWebHostBuilder(builder =>
        {
            // I DbContext si registrano ma nessun endpoint di questi test li usa: la connection string non viene mai aperta.
            builder.UseSetting("ConnectionStrings:sql", @"Server=(localdb)\non-usato;Database=Trasporto_Test");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHealthProbe>();
                services.AddSingleton<IHealthProbe>(new StubHealthProbe(
                    new ProbeResult(5, "Ollama raggiungibile", ProbeStatus.Ok, "finto", TimeSpan.FromMilliseconds(12)),
                    new ProbeResult(10, "Posizionamento modelli", ProbeStatus.Info, "finto", TimeSpan.Zero)));
                services.AddSingleton<IHealthProbe>(new StubHealthProbe(
                    new ProbeResult(1, "Connessione SQL", ProbeStatus.Ok, "finto", TimeSpan.Zero),
                    new ProbeResult(4, "Database applicativo", ProbeStatus.Warning, "finto", TimeSpan.Zero)));
            });
        });
    }

    [OneTimeTearDown]
    public async Task DisposeFactory() => await _factory.DisposeAsync();

    [Test]
    public async Task GetRoot_ReturnsServiceName()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync("/", CancellationToken);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body.RootElement.GetProperty("service").GetString(), Is.EqualTo("Dusiburg.AI.Trasporto.Efti.Api"));
    }

    [Test]
    public async Task GetApiHealth_ReturnsAllChecksOrderedByNumber()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        ProbeReport? report = await client.GetFromJsonAsync<ProbeReport>("/api/health", TrasportoJson.Options, CancellationToken);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.Checks.Select(c => c.Number), Is.EqualTo(new[] { 1, 4, 5, 10 }));
        Assert.That(report.Checks[2].Duration, Is.EqualTo(TimeSpan.FromMilliseconds(12)));
        Assert.That((report.HasErrors, report.HasWarnings), Is.EqualTo((false, true)));
    }

    [Test]
    public async Task GetApiHealth_SerializesStatusAsString()
    {
        //SETUP
        using HttpClient client = _factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync("/api/health", CancellationToken);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        JsonElement first = body.RootElement.GetProperty("checks")[0];
        Assert.That(first.GetProperty("status").GetString(), Is.EqualTo("Ok"));
        Assert.That(first.GetProperty("name").GetString(), Is.EqualTo("Connessione SQL"));
    }
}

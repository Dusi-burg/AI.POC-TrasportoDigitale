using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Efti.Api.Tms;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
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

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;

/// <summary>
/// <c>Efti.Api</c> in-process su un database dedicato, con il TMS finto, le chiavi di ruolo dei test e l'orologio fermo a
/// mezzogiorno del giorno del seed. Lo usano le fixture che partono da spedizioni demo già importate, comprese quelle del
/// server MCP, che gli sta sopra.
/// </summary>
internal sealed class EftiApiTestHost : IAsyncDisposable
{
    public static readonly DateOnly SeedDate = new(2026, 10, 5);

    public static readonly DateTimeOffset Now = new(SeedDate, new TimeOnly(12, 0), TimeSpan.Zero);

    private EftiApiTestHost(string connectionString, FakeTmsHandler tms, WebApplicationFactory<EftiApiEntryPoint> factory)
    {
        ConnectionString = connectionString;
        Tms = tms;
        Factory = factory;
        Client = factory.CreateClient();
    }

    public string ConnectionString { get; }

    public FakeTmsHandler Tms { get; }

    public WebApplicationFactory<EftiApiEntryPoint> Factory { get; }

    public HttpClient Client { get; }

    /// <summary>Crea il database, avvia l'API e importa come operatore gli ordini indicati.</summary>
    public static async Task<EftiApiTestHost> StartAsync(CancellationToken cancellationToken, params string[] orderCodes)
    {
        string connectionString = TrasportoDatabaseInitializer.LocalConnectionStringFor($"Trasporto_Test_{Guid.NewGuid():N}");

        await TrasportoDatabaseInitializer.RecreateAsync(connectionString, seed: false, cancellationToken: cancellationToken);

        var tms = new FakeTmsHandler(TmsSeedGenerator.Generate(TmsSeedGenerator.DefaultRandomSeed, SeedDate));

        WebApplicationFactory<EftiApiEntryPoint> factory = new WebApplicationFactory<EftiApiEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:sql", connectionString);
            builder.UseSetting(TmsApiClient.BaseAddressConfigurationKey, "http://tms.test");
            builder.UseTestApiKeys();

            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<TmsApiClient>().ConfigurePrimaryHttpMessageHandler(() => tms);
                services.RemoveAll<IHealthProbe>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            });
        });

        var host = new EftiApiTestHost(connectionString, tms, factory);

        foreach (string orderCode in orderCodes)
        {
            using HttpResponseMessage imported = await host.SendAsync(
                TestApiKeys.For(Role.Dispatcher), HttpMethod.Post, "/api/shipments/import", new ImportShipmentsRequest(orderCode), cancellationToken);

            imported.EnsureSuccessStatusCode();
        }

        return host;
    }

    public async Task<HttpResponseMessage> SendAsync(string? apiKey, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(method, path).WithApiKey(apiKey);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: TrasportoJson.Options);
        }

        return await Client.SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> SendAsync(Role role, HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        SendAsync(TestApiKeys.For(role), method, path, body, cancellationToken);

    /// <summary>Una lettura che deve riuscire.</summary>
    public async Task<T> GetAsync<T>(Role role, string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(role, HttpMethod.Get, path, body: null, cancellationToken);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<T>(TrasportoJson.Options, cancellationToken))!;
    }

    /// <summary>Una scrittura che deve riuscire.</summary>
    public async Task<T> PostAsync<T>(Role role, string path, object body, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(role, HttpMethod.Post, path, body, cancellationToken);

        Assert.That(response.IsSuccessStatusCode, Is.True, () => $"POST {path}: {(int)response.StatusCode} {response.Content.ReadAsStringAsync(cancellationToken).Result}");

        return (await response.Content.ReadFromJsonAsync<T>(TrasportoJson.Options, cancellationToken))!;
    }

    public async Task<AuditEvent> LastAuditEventAsync(CancellationToken cancellationToken)
    {
        await using AuditDbContext audit = TrasportoDatabaseInitializer.CreateAudit(ConnectionString);

        return await audit.Events.AsNoTracking().OrderByDescending(e => e.Id).FirstAsync(cancellationToken);
    }

    /// <summary>Versioni del dataset di una spedizione, dalla prima.</summary>
    public async Task<List<EftiDataset>> DatasetsAsync(string shipmentCode, CancellationToken cancellationToken)
    {
        await using EftiDbContext efti = TrasportoDatabaseInitializer.CreateEfti(ConnectionString);

        return await efti.Datasets.AsNoTracking().Where(d => d.Shipment.Code == shipmentCode).OrderBy(d => d.Version).ToListAsync(cancellationToken);
    }

    public static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        Tms.Dispose();
        await TrasportoDatabaseInitializer.DropAsync(ConnectionString);
    }
}

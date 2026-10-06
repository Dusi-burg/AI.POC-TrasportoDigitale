using System.Text.Json;
using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Tms.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dusiburg.AI.Trasporto.Tms.Api.Tests;

/// <summary>
/// Un database dedicato per classe di test su <c>(localdb)\localdev</c> (<c>Trasporto_Test_&lt;guid&gt;</c>), creato dal modello con
/// i dati del seed a una data fissa e cancellato a fine classe.
/// </summary>
public abstract class TmsApiTestBase
{
    protected static readonly DateOnly SeedDate = new(2026, 10, 5);

    private string _connectionString = null!;

    protected WebApplicationFactory<TmsApiEntryPoint> Factory { get; private set; } = null!;

    /// <summary>I dati inseriti dal seed, per confrontare ciò che l'API restituisce.</summary>
    protected TmsSeedData Seed { get; private set; } = null!;

    protected static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabaseAndFactory()
    {
        _connectionString = TrasportoDatabaseInitializer.LocalConnectionStringFor($"Trasporto_Test_{Guid.NewGuid():N}");

        Seed = (await TrasportoDatabaseInitializer.RecreateAsync(_connectionString, seed: true, today: SeedDate, cancellationToken: CancellationToken))!;

        Factory = new WebApplicationFactory<TmsApiEntryPoint>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:sql", _connectionString);

            // Per l'API è sempre il giorno del seed: POST /dev/reset rigenera i dati con le date relative a "oggi", e con
            // l'orologio vero i test darebbero un esito diverso a seconda del giorno in cui si eseguono.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new SeedDayTimeProvider());
            });
        });
    }

    private sealed class SeedDayTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(SeedDate, new TimeOnly(12, 0), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        await Factory.DisposeAsync();
        await TrasportoDatabaseInitializer.DropAsync(_connectionString);
    }

    protected async Task<T> WithDbAsync<T>(Func<TmsDbContext, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();

        return await action(scope.ServiceProvider.GetRequiredService<TmsDbContext>());
    }

    /// <summary>Estensione <c>code</c> di un ProblemDetails.</summary>
    protected static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        return document.RootElement.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;
    }
}

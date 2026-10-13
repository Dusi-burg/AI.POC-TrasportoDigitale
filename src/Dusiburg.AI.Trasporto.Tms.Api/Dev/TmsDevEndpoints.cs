using Dusiburg.AI.Trasporto.Tms.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Tms.Api.Dev;

/// <summary>Endpoint di supporto alla demo, mappati solo in Development.</summary>
internal static class TmsDevEndpoints
{
    public static IEndpointRouteBuilder MapTmsDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev");

        // Riporta il TMS ai dati del seed: stessi ordini demo e stessi dati sintetici, con le date relative a oggi.
        dev.MapPost("/reset", async (TmsDbContext db, IConfiguration configuration, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        {
            int randomSeed = configuration.GetValue(TmsSeeder.RandomSeedConfigurationKey, TmsSeedGenerator.DefaultRandomSeed);
            DateOnly today = DateOnly.FromDateTime(timeProvider.GetLocalNow().Date);

            await TmsSeeder.ResetAsync(db, randomSeed, today, cancellationToken);

            return TypedResults.NoContent();
        });

        return app;
    }
}

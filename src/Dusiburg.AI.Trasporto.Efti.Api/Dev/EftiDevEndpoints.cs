using Dusiburg.AI.Trasporto.Efti.Data;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Api.Dev;

/// <summary>Endpoint di supporto alla demo, mappati solo in Development.</summary>
internal static class EftiDevEndpoints
{
    public static IEndpointRouteBuilder MapEftiDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev");

        // Svuota lo schema efti per ripetere l'importazione dal TMS: spedizioni, dataset, problemi, soggetti e registro degli
        // identificatori. L'audit non si tocca: è append-only anche per la demo.
        dev.MapPost("/reset", async (EftiDbContext db, CancellationToken cancellationToken) =>
        {
            var strategy = db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

                await db.FieldProposals.ExecuteDeleteAsync(cancellationToken);
                await db.IdentifierRegistry.ExecuteDeleteAsync(cancellationToken);
                await db.ValidationIssues.ExecuteDeleteAsync(cancellationToken);
                await db.Datasets.ExecuteDeleteAsync(cancellationToken);
                await db.ConsignmentItems.ExecuteDeleteAsync(cancellationToken);
                await db.ShipmentParties.ExecuteDeleteAsync(cancellationToken);
                await db.Shipments.ExecuteDeleteAsync(cancellationToken);
                await db.Parties.ExecuteDeleteAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            });

            return TypedResults.NoContent();
        });

        return app;
    }
}

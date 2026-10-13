using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Tms.Data.Seed;

/// <summary>Dati del TMS: ordini demo e dati sintetici di <see cref="TmsSeedGenerator"/>.</summary>
public static class TmsSeeder
{
    /// <summary>Chiave di configurazione del seed casuale (<c>Seed__RandomSeed</c> nelle variabili d'ambiente).</summary>
    public const string RandomSeedConfigurationKey = "Seed:RandomSeed";

    /// <summary>Inserisce i dati solo se il TMS è vuoto; restituisce ciò che ha inserito, <c>null</c> se non ha fatto nulla.</summary>
    public static async Task<TmsSeedData?> SeedAsync(
        TmsDbContext db, int randomSeed, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (await db.Customers.AnyAsync(cancellationToken) || await db.TransportOrders.AnyAsync(cancellationToken))
        {
            return null;
        }

        TmsSeedData data = TmsSeedGenerator.Generate(randomSeed, today);

        db.Customers.AddRange(data.Customers);
        db.Vehicles.AddRange(data.Vehicles);
        db.Drivers.AddRange(data.Drivers);
        db.Trips.AddRange(data.Trips);
        db.TransportOrders.AddRange(data.Orders);

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        return data;
    }

    /// <summary>
    /// Riporta il TMS allo stato del seed per ripetere la demo: cancella ordini, viaggi, flotta, autisti e clienti e reinserisce
    /// i dati, in un'unica transazione.
    /// </summary>
    public static async Task<TmsSeedData> ResetAsync(
        TmsDbContext db, int randomSeed, DateOnly today, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await db.TransportOrderDocuments.ExecuteDeleteAsync(cancellationToken);
            await db.TransportOrderLines.ExecuteDeleteAsync(cancellationToken);
            await db.TransportOrders.ExecuteDeleteAsync(cancellationToken);
            await db.Trips.ExecuteDeleteAsync(cancellationToken);
            await db.Vehicles.ExecuteDeleteAsync(cancellationToken);
            await db.Drivers.ExecuteDeleteAsync(cancellationToken);
            await db.Customers.ExecuteDeleteAsync(cancellationToken);

            TmsSeedData data = await SeedAsync(db, randomSeed, today, cancellationToken)
                ?? throw new InvalidOperationException("Il TMS non è vuoto dopo la cancellazione.");

            await transaction.CommitAsync(cancellationToken);

            return data;
        });
    }
}

using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Tms.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dusiburg.AI.Trasporto.DbInit;

/// <summary>
/// Crea il database Trasporto da zero dal modello EF di tutti i DbContext del POC (G9): nessuna migration, nessuno step intermedio.
/// Usato dal tool e dalle fixture dei test, con un database dedicato.
/// </summary>
public static class TrasportoDatabaseInitializer
{
    public const string LocalConnectionString = @"Server=(localdb)\localdev;Database=Trasporto;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>Connection string di <c>(localdb)\localdev</c> per un altro database (es. quelli dei test).</summary>
    public static string LocalConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder(LocalConnectionString) { InitialCatalog = database }.ConnectionString;

    /// <summary>
    /// Cancella il database se esiste e lo ricrea con schemi e tabelle. Il parametro <paramref name="seed"/> è già nel contratto
    /// del tool, ma lookup e dati demo arrivano con la Fase 2: per ora il database nasce vuoto in entrambi i casi.
    /// </summary>
    public static async Task RecreateAsync(string connectionString, bool seed = true, CancellationToken cancellationToken = default)
    {
        _ = seed;

        await using TmsDbContext tms = CreateTms(connectionString);

        await tms.Database.EnsureDeletedAsync(cancellationToken);

        // Crea il database e le tabelle del primo contesto.
        await tms.Database.EnsureCreatedAsync(cancellationToken);

        // EnsureCreated non fa nulla su un database che ha già tabelle: gli altri contesti creano solo le proprie.
        await using EftiDbContext efti = CreateEfti(connectionString);

        await efti.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);

        await using AuditDbContext audit = CreateAudit(connectionString);

        await audit.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);
    }

    /// <summary>Cancella il database se esiste.</summary>
    public static async Task DropAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using TmsDbContext tms = CreateTms(connectionString);

        await tms.Database.EnsureDeletedAsync(cancellationToken);
    }

    private static TmsDbContext CreateTms(string connectionString) =>
        new(new DbContextOptionsBuilder<TmsDbContext>().UseSqlServer(connectionString).Options);

    private static EftiDbContext CreateEfti(string connectionString) =>
        new(new DbContextOptionsBuilder<EftiDbContext>().UseSqlServer(connectionString).Options);

    private static AuditDbContext CreateAudit(string connectionString) =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(connectionString).Options);
}

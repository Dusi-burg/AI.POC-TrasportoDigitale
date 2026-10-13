using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Tms.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;
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
    /// Cancella il database se esiste e lo ricrea con schemi, tabelle e righe delle tabelle di lookup; con <paramref name="seed"/>
    /// inserisce anche i dati del TMS (ordini demo e dati sintetici). Lo schema efti resta vuoto: lo riempie l'importazione.
    /// </summary>
    /// <param name="today">Giorno a cui sono relative le date dei dati; <c>null</c> = oggi.</param>
    /// <returns>I dati inseriti nel TMS, <c>null</c> senza seed.</returns>
    public static async Task<TmsSeedData?> RecreateAsync(
        string connectionString,
        bool seed = true,
        int randomSeed = TmsSeedGenerator.DefaultRandomSeed,
        DateOnly? today = null,
        CancellationToken cancellationToken = default)
    {
        await using TmsDbContext tms = CreateTms(connectionString);

        await tms.Database.EnsureDeletedAsync(cancellationToken);

        // Crea il database e le tabelle del primo contesto.
        await tms.Database.EnsureCreatedAsync(cancellationToken);

        // EnsureCreated non fa nulla su un database che ha già tabelle: gli altri contesti creano solo le proprie.
        await using EftiDbContext efti = CreateEfti(connectionString);

        await efti.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);

        await using AuditDbContext audit = CreateAudit(connectionString);

        await audit.Database.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(cancellationToken);

        // L'audit è append-only per vincolo del database, non per disciplina del codice: il trigger non fa parte del modello EF.
        await audit.Database.ExecuteSqlRawAsync(AuditDbContext.AppendOnlyTriggerSql, cancellationToken);

        return seed
            ? await TmsSeeder.SeedAsync(tms, randomSeed, today ?? DateOnly.FromDateTime(DateTime.Today), cancellationToken)
            : null;
    }

    /// <summary>Cancella il database se esiste.</summary>
    public static async Task DropAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using TmsDbContext tms = CreateTms(connectionString);

        await tms.Database.EnsureDeletedAsync(cancellationToken);
    }

    public static TmsDbContext CreateTms(string connectionString) =>
        new(new DbContextOptionsBuilder<TmsDbContext>().UseSqlServer(connectionString).Options);

    public static EftiDbContext CreateEfti(string connectionString) =>
        new(new DbContextOptionsBuilder<EftiDbContext>().UseSqlServer(connectionString).Options);

    public static AuditDbContext CreateAudit(string connectionString) =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseSqlServer(connectionString).Options);
}

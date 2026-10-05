using System.Globalization;
using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Tms.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;
using Microsoft.Data.SqlClient;

// Uso: dotnet run --project tools/Dusiburg.AI.Trasporto.DbInit [-- "<connection string>"] [--no-seed] [--allow-non-local]
// Senza connection string usa ConnectionStrings__sql dall'ambiente o, in mancanza, (localdb)\localdev database Trasporto.
// Il seed dei dati sintetici usa Seed__RandomSeed dall'ambiente, altrimenti TmsSeedGenerator.DefaultRandomSeed.
const string AllowNonLocal = "--allow-non-local";
const string NoSeed = "--no-seed";
const string RandomSeedVariable = "Seed__RandomSeed";

var connectionString = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__sql")
    ?? TrasportoDatabaseInitializer.LocalConnectionString;

var target = new SqlConnectionStringBuilder(connectionString);

// Il tool cancella il database: fuori da LocalDB serve una conferma esplicita.
if (!target.DataSource.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase) && !args.Contains(AllowNonLocal))
{
    Console.Error.WriteLine($"Il server '{target.DataSource}' non è LocalDB: il database verrebbe cancellato. Aggiungi {AllowNonLocal} per procedere.");
    return 1;
}

int randomSeed = TmsSeedGenerator.DefaultRandomSeed;

if (Environment.GetEnvironmentVariable(RandomSeedVariable) is { Length: > 0 } configuredSeed
    && !int.TryParse(configuredSeed, NumberStyles.Integer, CultureInfo.InvariantCulture, out randomSeed))
{
    Console.Error.WriteLine($"{RandomSeedVariable} '{configuredSeed}' non valido: serve un numero intero.");
    return 1;
}

var seed = !args.Contains(NoSeed);

Console.WriteLine($"Ricreo il database '{target.InitialCatalog}' su '{target.DataSource}'...");

TmsSeedData? data = await TrasportoDatabaseInitializer.RecreateAsync(connectionString, seed, randomSeed);

if (data is null)
{
    Console.WriteLine("Fatto: schemi tms ed efti creati, lookup popolate dagli enum, nessun dato demo.");
    return 0;
}

await using (TmsDbContext tms = TrasportoDatabaseInitializer.CreateTms(connectionString))
{
    await SeedReport.WriteAsync(tms, data, Console.Out);
}

Console.WriteLine();
Console.WriteLine("Fatto: schemi tms ed efti creati, lookup popolate dagli enum, dati del TMS inseriti. Lo schema efti si riempie con l'importazione.");

return 0;

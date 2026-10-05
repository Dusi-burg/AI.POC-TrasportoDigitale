using Dusiburg.AI.Trasporto.DbInit;
using Microsoft.Data.SqlClient;

// Uso: dotnet run --project tools/Dusiburg.AI.Trasporto.DbInit [-- "<connection string>"] [--no-seed] [--allow-non-local]
// Senza connection string usa ConnectionStrings__sql dall'ambiente o, in mancanza, (localdb)\localdev database Trasporto.
const string AllowNonLocal = "--allow-non-local";
const string NoSeed = "--no-seed";

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

var seed = !args.Contains(NoSeed);

Console.WriteLine($"Ricreo il database '{target.InitialCatalog}' su '{target.DataSource}'...");

await TrasportoDatabaseInitializer.RecreateAsync(connectionString, seed);

// Fase 1: i DbContext non hanno ancora entità, quindi il database è vuoto con o senza --no-seed.
Console.WriteLine("Fatto: database creato. Tabelle, lookup e dati demo arrivano con la Fase 2.");

return 0;

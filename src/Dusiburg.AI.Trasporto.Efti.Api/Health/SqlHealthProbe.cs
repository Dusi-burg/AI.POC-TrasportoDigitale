using Dusiburg.AI.Trasporto.Shared.Health;
using Microsoft.Data.SqlClient;

namespace Dusiburg.AI.Trasporto.Efti.Api.Health;

/// <summary>
/// Controlli 1–4 di <c>/api/health</c>: connessione, versione ≥ 17 (SQL Server 2025), tipo <c>VECTOR</c>, database applicativo.
/// Server e versione si leggono da <c>master</c>, così il controllo risponde anche prima che <c>DbInit</c> abbia creato il database.
/// </summary>
public sealed class SqlHealthProbe(IConfiguration configuration) : IHealthProbe
{
    public const string ConnectionStringName = "sql";

    private const string ConnectionName = "Connessione SQL";
    private const string VersionName = "Versione SQL Server";
    private const string VectorName = "Supporto VECTOR";
    private const string DatabaseName = "Database applicativo";

    private const int MinimumMajorVersion = 17;

    public async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken)
    {
        string? connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            ProbeResult missing = new(
                1, ConnectionName, ProbeStatus.Error,
                $"ConnectionStrings:{ConnectionStringName} non configurata: impostarla negli user-secrets dell'AppHost.", TimeSpan.Zero);

            return [missing, .. SkipAfterConnection("connection string mancante")];
        }

        var target = new SqlConnectionStringBuilder(connectionString);
        string database = target.InitialCatalog;
        target.InitialCatalog = "master";

        await using var connection = new SqlConnection(target.ConnectionString);

        ProbeResult connected = await ProbeResult.MeasureAsync(1, ConnectionName, async () =>
        {
            await connection.OpenAsync(cancellationToken);

            return (ProbeStatus.Ok, target.DataSource);
        });

        if (connected.Status == ProbeStatus.Error)
        {
            return [connected, .. SkipAfterConnection("connessione non riuscita")];
        }

        return
        [
            connected,
            await ProbeResult.MeasureAsync(2, VersionName, () => CheckVersionAsync(connection, cancellationToken)),
            await ProbeResult.MeasureAsync(3, VectorName, () => CheckVectorAsync(connection, cancellationToken)),
            await ProbeResult.MeasureAsync(4, DatabaseName, () => CheckDatabaseAsync(connection, database, cancellationToken))
        ];
    }

    private static async Task<(ProbeStatus, string)> CheckVersionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int),
                   CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('Edition') AS nvarchar(128))
            """;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        int major = reader.GetInt32(0);
        string detail = $"{reader.GetString(1)} ({reader.GetString(2)})";

        return major >= MinimumMajorVersion
            ? (ProbeStatus.Ok, detail)
            : (ProbeStatus.Error, $"{detail}: serve SQL Server 2025 (versione {MinimumMajorVersion}.x) per il tipo VECTOR");
    }

    private static async Task<(ProbeStatus, string)> CheckVectorAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT VECTOR_DISTANCE('cosine', CAST('[1,0]' AS VECTOR(2)), CAST('[0,1]' AS VECTOR(2)))";

        double distance = Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken));

        return (ProbeStatus.Ok, $"VECTOR_DISTANCE disponibile (coseno di vettori ortogonali = {distance:0.###})");
    }

    private static async Task<(ProbeStatus, string)> CheckDatabaseAsync(SqlConnection connection, string database, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return (ProbeStatus.Error, "la connection string non indica il database (Database=Trasporto)");
        }

        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT DB_ID(@database)";
        command.Parameters.AddWithValue("@database", database);

        object? databaseId = await command.ExecuteScalarAsync(cancellationToken);

        return databaseId is null or DBNull
            ? (ProbeStatus.Warning, $"il database '{database}' non esiste ancora: crearlo con dotnet run --project tools/Dusiburg.AI.Trasporto.DbInit")
            : (ProbeStatus.Ok, $"'{database}' presente");
    }

    private static IEnumerable<ProbeResult> SkipAfterConnection(string reason) =>
    [
        ProbeResult.Skipped(2, VersionName, reason),
        ProbeResult.Skipped(3, VectorName, reason),
        ProbeResult.Skipped(4, DatabaseName, reason)
    ];
}

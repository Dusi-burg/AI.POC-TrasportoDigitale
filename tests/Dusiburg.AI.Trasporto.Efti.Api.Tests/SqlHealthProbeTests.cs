using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Efti.Api.Health;
using Dusiburg.AI.Trasporto.Shared.Health;
using Microsoft.Extensions.Configuration;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>
/// Controlli SQL veri su <c>(localdb)\localdev</c>, con un database dedicato (<c>Trasporto_Test_&lt;guid&gt;</c>) creato da
/// <c>DbInit</c> e cancellato a fine classe.
/// </summary>
public class SqlHealthProbeTests
{
    /// <summary>Il controllo 3 usa il tipo <c>VECTOR</c>: serve LocalDB 17 (SQL Server 2025), che la CI può non avere.</summary>
    private const string RequiresSql2025 = "RequiresSql2025";

    private string _connectionString = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        _connectionString = TrasportoDatabaseInitializer.LocalConnectionStringFor($"Trasporto_Test_{Guid.NewGuid():N}");

        await TrasportoDatabaseInitializer.RecreateAsync(_connectionString, seed: false, cancellationToken: CancellationToken);
    }

    [OneTimeTearDown]
    public async Task DropDatabase() => await TrasportoDatabaseInitializer.DropAsync(_connectionString);

    [Test]
    [Category(RequiresSql2025)]
    public async Task RunAsync_DatabaseCreatedByDbInit_AllChecksOk()
    {
        //SETUP
        var probe = new SqlHealthProbe(Configuration(_connectionString));

        //SUT
        IReadOnlyList<ProbeResult> results = await probe.RunAsync(CancellationToken);

        Assert.That(results.Select(r => r.Number), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(results.Select(r => r.Status), Is.All.EqualTo(ProbeStatus.Ok), string.Join(" | ", results.Select(r => r.Detail)));
    }

    [Test]
    public async Task RunAsync_DatabaseNotCreatedYet_WarningPointingToDbInit()
    {
        //SETUP
        string missingDatabase = TrasportoDatabaseInitializer.LocalConnectionStringFor("Trasporto_Test_Inesistente");
        var probe = new SqlHealthProbe(Configuration(missingDatabase));

        //SUT
        IReadOnlyList<ProbeResult> results = await probe.RunAsync(CancellationToken);

        ProbeResult database = results.Single(r => r.Number == 4);
        Assert.That(results.Single(r => r.Number == 1).Status, Is.EqualTo(ProbeStatus.Ok));
        Assert.That(database.Status, Is.EqualTo(ProbeStatus.Warning));
        Assert.That(database.Detail, Does.Contain("DbInit"));
    }

    [Test]
    public async Task RunAsync_ConnectionStringMissing_ErrorAndOtherChecksSkipped()
    {
        //SETUP
        var probe = new SqlHealthProbe(new ConfigurationBuilder().Build());

        //SUT
        IReadOnlyList<ProbeResult> results = await probe.RunAsync(CancellationToken);

        Assert.That(results.Select(r => r.Number), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(results.Select(r => r.Status), Is.All.EqualTo(ProbeStatus.Error));
        Assert.That(results[0].Detail, Does.Contain("ConnectionStrings:sql"));
    }

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:sql"] = connectionString })
            .Build();
}

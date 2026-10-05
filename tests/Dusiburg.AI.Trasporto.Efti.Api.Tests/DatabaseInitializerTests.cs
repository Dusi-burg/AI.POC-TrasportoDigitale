using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Tms.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>
/// <c>DbInit</c> su un database dedicato di <c>(localdb)\localdev</c>: schemi, lookup, seed ripetibile e vincoli dello schema efti.
/// </summary>
public class DatabaseInitializerTests
{
    private static readonly DateOnly SeedDate = new(2026, 10, 5);

    private string _connectionString = null!;
    private TmsSeedData _seed = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        _connectionString = TrasportoDatabaseInitializer.LocalConnectionStringFor($"Trasporto_Test_{Guid.NewGuid():N}");
        _seed = (await TrasportoDatabaseInitializer.RecreateAsync(_connectionString, seed: true, today: SeedDate, cancellationToken: CancellationToken))!;
    }

    [OneTimeTearDown]
    public async Task DropDatabase() => await TrasportoDatabaseInitializer.DropAsync(_connectionString);

    [Test]
    public async Task Recreate_WithSeed_FillsTmsAndLeavesEftiEmpty()
    {
        //SETUP
        await using TmsDbContext tms = TrasportoDatabaseInitializer.CreateTms(_connectionString);
        await using EftiDbContext efti = TrasportoDatabaseInitializer.CreateEfti(_connectionString);

        //SUT
        int orders = await tms.TransportOrders.CountAsync(CancellationToken);
        int lines = await tms.TransportOrderLines.CountAsync(CancellationToken);
        int shipments = await efti.Shipments.CountAsync(CancellationToken);
        int parties = await efti.Parties.CountAsync(CancellationToken);

        Assert.That(orders, Is.EqualTo(_seed.Orders.Count));
        Assert.That(lines, Is.EqualTo(_seed.Orders.Sum(o => o.Lines.Count)));
        Assert.That((shipments, parties), Is.EqualTo((0, 0)), "lo schema efti si riempie con l'importazione");
    }

    [Test]
    public async Task Recreate_EftiLookups_HoldExactlyTheEnumMembers()
    {
        //SETUP
        await using EftiDbContext efti = TrasportoDatabaseInitializer.CreateEfti(_connectionString);

        //SUT
        string[] roles = await efti.PartyRoles.OrderBy(l => l.Id).Select(l => l.Name).ToArrayAsync(CancellationToken);
        string[] statuses = await efti.DatasetStatuses.OrderBy(l => l.Id).Select(l => l.Name).ToArrayAsync(CancellationToken);
        string[] severities = await efti.IssueSeverities.OrderBy(l => l.Id).Select(l => l.Name).ToArrayAsync(CancellationToken);
        string[] packageTypes = await efti.PackageTypes.OrderBy(l => l.Id).Select(l => l.Name).ToArrayAsync(CancellationToken);

        Assert.That(roles, Is.EqualTo(Enum.GetNames<PartyRole>()));
        Assert.That(statuses, Is.EqualTo(Enum.GetNames<DatasetStatus>()));
        Assert.That(severities, Is.EqualTo(Enum.GetNames<IssueSeverity>()));
        Assert.That(packageTypes, Is.EqualTo(Enum.GetNames<PackageType>()));
    }

    [Test]
    public async Task Recreate_Tables_LiveInTheSchemaOfTheirOwner()
    {
        //SETUP
        await using TmsDbContext tms = TrasportoDatabaseInitializer.CreateTms(_connectionString);

        //SUT
        List<string> tables = await tms.Database
            .SqlQuery<string>($"SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync(CancellationToken);

        Assert.That(tables, Is.SupersetOf(new[]
        {
            "tms.Customer", "tms.Vehicle", "tms.Driver", "tms.Trip", "tms.TransportOrder", "tms.TransportOrderLine",
            "tms.TransportOrderStatus", "tms.VehicleType", "tms.PackageType", "tms.FreightPayment",
            "efti.Shipment", "efti.Party", "efti.ShipmentParty", "efti.ConsignmentItem", "efti.EftiDataset", "efti.ValidationIssue",
            "efti.PartyRole", "efti.DatasetStatus", "efti.IssueSeverity", "efti.PackageType", "efti.FreightPayment"
        }));
        Assert.That(tables, Has.None.StartsWith("dbo."));
    }

    [Test]
    public async Task Shipment_Saved_GetsPublicIdAndKeepsItsGraph()
    {
        //SETUP
        await using EftiDbContext efti = TrasportoDatabaseInitializer.CreateEfti(_connectionString);
        Shipment shipment = NewShipment("SH-TEST-1", "TO-TEST-1");
        shipment.Parties.Add(new ShipmentParty { Role = PartyRole.Consignor, Party = new Party { Name = "Mittente di prova", CountryCode = "IT" } });
        shipment.Items.Add(new ConsignmentItem { SequenceNumber = 1, Description = "Merce di prova", PackageCount = 3, PackageType = PackageType.Pallet, GrossWeightKg = 120.5m });
        shipment.Datasets.Add(new EftiDataset
        {
            Version = 1, Status = DatasetStatus.Incomplete, SchemaVersion = "0.7", Json = "{}", CreatedAt = DateTimeOffset.UtcNow,
            Issues = { new ValidationIssue { RuleCode = "TEST", Severity = IssueSeverity.Error, Message = "Problema di prova" } }
        });

        //SUT
        efti.Shipments.Add(shipment);
        await efti.SaveChangesAsync(CancellationToken);

        efti.ChangeTracker.Clear();
        Shipment saved = await efti.Shipments
            .Include(s => s.Parties).ThenInclude(p => p.Party)
            .Include(s => s.Items)
            .Include(s => s.Datasets).ThenInclude(d => d.Issues)
            .SingleAsync(s => s.Code == "SH-TEST-1", CancellationToken);

        Assert.That(saved.PublicId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(saved.Pickup, Is.Not.Null, "tipo posseduto con tutte le colonne vuote");
        Assert.That(saved.Parties.Single().Party.Name, Is.EqualTo("Mittente di prova"));
        Assert.That((saved.Items.Single().PackageType, saved.Items.Single().GrossWeightKg), Is.EqualTo((PackageType.Pallet, 120.5m)));
        Assert.That(saved.Datasets.Single().Issues.Single().Severity, Is.EqualTo(IssueSeverity.Error));
    }

    [Test]
    public async Task Shipment_SameTmsOrderTwice_IsRejectedByTheDatabase()
    {
        //SETUP
        await using EftiDbContext efti = TrasportoDatabaseInitializer.CreateEfti(_connectionString);
        efti.Shipments.Add(NewShipment("SH-TEST-2", "TO-TEST-2"));
        await efti.SaveChangesAsync(CancellationToken);
        efti.Shipments.Add(NewShipment("SH-TEST-3", "TO-TEST-2"));

        //SUT
        Assert.That(async () => await efti.SaveChangesAsync(CancellationToken), Throws.TypeOf<DbUpdateException>());
    }

    private static Shipment NewShipment(string code, string tmsOrderCode) =>
        new() { Code = code, TmsOrderCode = tmsOrderCode, ImportedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
}

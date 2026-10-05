using Dusiburg.AI.Trasporto.Efti.Data.Configurations;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Data;

/// <summary>
/// Nucleo eFTI: schema <c>efti</c> del database Trasporto (P1). Lo schema si crea da zero dal modello, senza migration (G9).
/// </summary>
public sealed class EftiDbContext(DbContextOptions<EftiDbContext> options) : DbContext(options)
{
    public const string Schema = "efti";

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<ShipmentParty> ShipmentParties => Set<ShipmentParty>();

    public DbSet<ConsignmentItem> ConsignmentItems => Set<ConsignmentItem>();

    public DbSet<EftiDataset> Datasets => Set<EftiDataset>();

    public DbSet<ValidationIssue> ValidationIssues => Set<ValidationIssue>();

    public DbSet<EnumLookup<PartyRole>> PartyRoles => Set<EnumLookup<PartyRole>>();

    public DbSet<EnumLookup<DatasetStatus>> DatasetStatuses => Set<EnumLookup<DatasetStatus>>();

    public DbSet<EnumLookup<IssueSeverity>> IssueSeverities => Set<EnumLookup<IssueSeverity>>();

    public DbSet<EnumLookup<PackageType>> PackageTypes => Set<EnumLookup<PackageType>>();

    public DbSet<EnumLookup<FreightPayment>> FreightPayments => Set<EnumLookup<FreightPayment>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // L'assembly ospita anche AuditDbContext: qui si applicano solo le configurazioni dello schema efti.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(EftiDbContext).Assembly,
            type => type.Namespace == typeof(ShipmentConfiguration).Namespace);
    }
}

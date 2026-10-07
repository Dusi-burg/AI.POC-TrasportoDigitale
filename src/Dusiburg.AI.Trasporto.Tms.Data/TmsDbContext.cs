using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Tms.Data;

/// <summary>
/// TMS simulato: schema <c>tms</c> del database Trasporto (P1). Lo schema si crea da zero dal modello, senza migration (G9).
/// </summary>
public sealed class TmsDbContext(DbContextOptions<TmsDbContext> options) : DbContext(options)
{
    public const string Schema = "tms";

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Driver> Drivers => Set<Driver>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<TransportOrder> TransportOrders => Set<TransportOrder>();

    public DbSet<TransportOrderLine> TransportOrderLines => Set<TransportOrderLine>();

    public DbSet<TransportOrderDocument> TransportOrderDocuments => Set<TransportOrderDocument>();

    public DbSet<EnumLookup<TransportOrderStatus>> TransportOrderStatuses => Set<EnumLookup<TransportOrderStatus>>();

    public DbSet<EnumLookup<VehicleType>> VehicleTypes => Set<EnumLookup<VehicleType>>();

    public DbSet<EnumLookup<PackageType>> PackageTypes => Set<EnumLookup<PackageType>>();

    public DbSet<EnumLookup<FreightPayment>> FreightPayments => Set<EnumLookup<FreightPayment>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TmsDbContext).Assembly);
    }
}

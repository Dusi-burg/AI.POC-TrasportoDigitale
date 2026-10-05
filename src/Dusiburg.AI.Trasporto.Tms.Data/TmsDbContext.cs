using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Tms.Data;

/// <summary>
/// TMS simulato: schema <c>tms</c> del database Trasporto (P1). Lo schema si crea da zero dal modello, senza migration (G9).
/// Le entità arrivano con la Fase 2.
/// </summary>
public sealed class TmsDbContext(DbContextOptions<TmsDbContext> options) : DbContext(options)
{
    public const string Schema = "tms";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TmsDbContext).Assembly);
    }
}

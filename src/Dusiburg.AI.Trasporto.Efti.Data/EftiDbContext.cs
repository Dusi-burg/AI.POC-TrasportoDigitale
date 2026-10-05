using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Data;

/// <summary>
/// Nucleo eFTI: schema <c>efti</c> del database Trasporto (P1). Lo schema si crea da zero dal modello, senza migration (G9).
/// Le entità arrivano con la Fase 2.
/// </summary>
public sealed class EftiDbContext(DbContextOptions<EftiDbContext> options) : DbContext(options)
{
    public const string Schema = "efti";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
    }
}

using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Data;

/// <summary>
/// Audit trail append-only: schema <c>audit</c> del database Trasporto (P1), di proprietà di <c>Efti.Api</c>.
/// Le entità arrivano con la Fase 4.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
    }
}

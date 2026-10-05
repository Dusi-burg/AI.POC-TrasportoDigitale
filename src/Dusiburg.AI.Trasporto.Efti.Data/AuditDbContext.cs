using Dusiburg.AI.Trasporto.Efti.Data.AuditConfigurations;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Data;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Data;

/// <summary>
/// Audit trail append-only: schema <c>audit</c> del database Trasporto (P1), di proprietà di <c>Efti.Api</c>.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public const string AppendOnlyTriggerName = "TR_AuditEvent_AppendOnly";

    /// <summary>
    /// Il trigger che rende <c>audit.AuditEvent</c> append-only. EF non descrive i trigger nel modello: lo esegue
    /// <c>DbInit</c> subito dopo aver creato le tabelle.
    /// </summary>
    public const string AppendOnlyTriggerSql =
        $"""
        CREATE TRIGGER [{Schema}].[{AppendOnlyTriggerName}] ON [{Schema}].[AuditEvent]
        INSTEAD OF UPDATE, DELETE
        AS
        BEGIN
            SET NOCOUNT ON;
            THROW 51000, 'audit.AuditEvent e'' append-only: UPDATE e DELETE non sono ammessi.', 1;
        END
        """;

    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    public DbSet<EnumLookup<Role>> Roles => Set<EnumLookup<Role>>();

    public DbSet<EnumLookup<Operation>> Operations => Set<EnumLookup<Operation>>();

    public DbSet<EnumLookup<AuditOutcome>> Outcomes => Set<EnumLookup<AuditOutcome>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // L'assembly ospita anche EftiDbContext: qui si applicano solo le configurazioni dello schema audit.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AuditDbContext).Assembly,
            type => type.Namespace == typeof(AuditEventConfiguration).Namespace);
    }
}

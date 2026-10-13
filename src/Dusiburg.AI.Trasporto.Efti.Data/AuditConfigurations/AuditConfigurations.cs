using Dusiburg.AI.Trasporto.Efti.Data.Configurations;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Data;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dusiburg.AI.Trasporto.Efti.Data.AuditConfigurations;

// Schema audit: stesse convenzioni dello schema efti. Le configurazioni stanno in un namespace a parte perché l'assembly
// ospita due DbContext, e ciascuno applica solo le proprie.

internal sealed class RoleConfiguration() : EnumLookupConfiguration<Role>("Role");

internal sealed class OperationConfiguration() : EnumLookupConfiguration<Operation>("Operation");

internal sealed class AuditOutcomeConfiguration() : EnumLookupConfiguration<AuditOutcome>("AuditOutcome");

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        // Dichiarare il trigger serve a EF per non usare la clausola OUTPUT negli INSERT, che con un trigger non è ammessa.
        builder.ToTable("AuditEvent", t => t.HasTrigger(AuditDbContext.AppendOnlyTriggerName));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActorId).HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.ActorName).HasMaxLength(200);
        builder.Property(e => e.Role).HasColumnName("RoleId").HasConversion<byte>();
        builder.Property(e => e.Operation).HasColumnName("OperationId").HasConversion<byte>();
        builder.Property(e => e.Resource).HasMaxLength(200);
        builder.Property(e => e.ShipmentCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.Outcome).HasColumnName("AuditOutcomeId").HasConversion<byte>();
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Detail).HasMaxLength(200);
        builder.Property(e => e.TraceId).HasMaxLength(32).IsUnicode(false);

        // Lettura dell'audit dal più recente; accessi dell'autorità per spedizione e per targa.
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => e.ShipmentCode);
        builder.HasIndex(e => new { e.Operation, e.Resource });

        builder.HasOne<EnumLookup<Role>>()
            .WithMany()
            .HasForeignKey(e => e.Role)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<Operation>>()
            .WithMany()
            .HasForeignKey(e => e.Operation)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<AuditOutcome>>()
            .WithMany()
            .HasForeignKey(e => e.Outcome)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

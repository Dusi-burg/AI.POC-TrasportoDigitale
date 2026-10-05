using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dusiburg.AI.Trasporto.Efti.Data.Configurations;

// Convenzioni (skill db-operations): tabelle al singolare, PK numerica "Id", FK qualificate (<Entity>Id), niente PK GUID
// (colonna dedicata univoca), chiavi di business stringa come colonne univoche, enum come FK verso tabelle di lookup tinyint
// generate dal codice. Codici e chiavi in varchar, testi liberi in nvarchar.

/// <summary>Tabella di lookup di un enum: PK tinyint = valore esplicito del membro, righe da <c>Enum.GetValues</c>.</summary>
internal abstract class EnumLookupConfiguration<TEnum>(string tableName) : IEntityTypeConfiguration<EnumLookup<TEnum>>
    where TEnum : struct, Enum
{
    public void Configure(EntityTypeBuilder<EnumLookup<TEnum>> builder)
    {
        builder.ToTable(tableName);

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).HasConversion<byte>().ValueGeneratedNever();
        builder.Property(l => l.Name).HasMaxLength(50).IsUnicode(false);

        builder.HasIndex(l => l.Name).IsUnique();

        builder.HasData(Enum.GetValues<TEnum>().Select(value => new EnumLookup<TEnum> { Id = value, Name = value.ToString() }));
    }
}

internal sealed class PartyRoleConfiguration() : EnumLookupConfiguration<PartyRole>("PartyRole");

internal sealed class DatasetStatusConfiguration() : EnumLookupConfiguration<DatasetStatus>("DatasetStatus");

internal sealed class IssueSeverityConfiguration() : EnumLookupConfiguration<IssueSeverity>("IssueSeverity");

internal sealed class PackageTypeConfiguration() : EnumLookupConfiguration<PackageType>("PackageType");

internal sealed class FreightPaymentConfiguration() : EnumLookupConfiguration<FreightPayment>("FreightPayment");

internal static class ColumnSizes
{
    public const int Code = 20;
    public const int Name = 200;
    public const int VatNumber = 20;
    public const int Street = 200;
    public const int PostalCode = 12;
    public const int City = 100;

    /// <summary>Più largo di un codice ISO a due lettere: il dato importato può essere sbagliato, e lo dice la validazione.</summary>
    public const int CountryCode = 3;

    public const int PlateNumber = 12;
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipment");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.PublicId).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(s => s.Code).HasMaxLength(ColumnSizes.Code).IsUnicode(false);
        builder.Property(s => s.TmsOrderCode).HasMaxLength(ColumnSizes.Code).IsUnicode(false);
        builder.Property(s => s.TotalGrossWeightKg).HasPrecision(12, 3);
        builder.Property(s => s.TotalVolumeM3).HasPrecision(10, 3);
        builder.Property(s => s.VehiclePlateNumber).HasMaxLength(ColumnSizes.PlateNumber).IsUnicode(false);
        builder.Property(s => s.VehicleCountryCode).HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
        builder.Property(s => s.TrailerPlateNumber).HasMaxLength(ColumnSizes.PlateNumber).IsUnicode(false);
        builder.Property(s => s.TrailerCountryCode).HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
        builder.Property(s => s.CmrNumber).HasMaxLength(35).IsUnicode(false);
        builder.Property(s => s.FreightAmount).HasPrecision(18, 2);
        builder.Property(s => s.FreightPayment).HasColumnName("FreightPaymentId").HasConversion<byte>();
        builder.Property(s => s.CurrencyCode).HasMaxLength(3).IsUnicode(false);
        builder.Property(s => s.CodAmount).HasPrecision(18, 2);
        builder.Property(s => s.DeclaredValue).HasPrecision(18, 2);
        builder.Property(s => s.SenderInstructions).HasMaxLength(1000);

        builder.OwnsOne(s => s.Pickup, location => ConfigureLocation(location, "Pickup"));
        builder.OwnsOne(s => s.Delivery, location => ConfigureLocation(location, "Delivery"));

        // Tutte le colonne dei tipi posseduti sono annullabili: senza "required" EF leggerebbe null l'intero oggetto.
        builder.Navigation(s => s.Pickup).IsRequired();
        builder.Navigation(s => s.Delivery).IsRequired();

        builder.HasIndex(s => s.PublicId).IsUnique();
        builder.HasIndex(s => s.Code).IsUnique();
        builder.HasIndex(s => s.TmsOrderCode).IsUnique();

        // Ricerca dell'autorità per targa (Fase 4): il registro degli identificatori parte da qui.
        builder.HasIndex(s => s.VehiclePlateNumber);

        builder.HasOne<EnumLookup<FreightPayment>>()
            .WithMany()
            .HasForeignKey(s => s.FreightPayment)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureLocation(OwnedNavigationBuilder<Shipment, Location> location, string prefix)
    {
        location.Property(l => l.Street).HasColumnName(prefix + "Street").HasMaxLength(ColumnSizes.Street);
        location.Property(l => l.PostalCode).HasColumnName(prefix + "PostalCode").HasMaxLength(ColumnSizes.PostalCode).IsUnicode(false);
        location.Property(l => l.City).HasColumnName(prefix + "City").HasMaxLength(ColumnSizes.City);
        location.Property(l => l.CountryCode).HasColumnName(prefix + "CountryCode").HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
    }
}

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("Party");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(ColumnSizes.Name);
        builder.Property(p => p.VatNumber).HasMaxLength(ColumnSizes.VatNumber).IsUnicode(false);
        builder.Property(p => p.Street).HasMaxLength(ColumnSizes.Street);
        builder.Property(p => p.PostalCode).HasMaxLength(ColumnSizes.PostalCode).IsUnicode(false);
        builder.Property(p => p.City).HasMaxLength(ColumnSizes.City);
        builder.Property(p => p.CountryCode).HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);

        builder.HasIndex(p => p.VatNumber);
    }
}

internal sealed class ShipmentPartyConfiguration : IEntityTypeConfiguration<ShipmentParty>
{
    public void Configure(EntityTypeBuilder<ShipmentParty> builder)
    {
        builder.ToTable("ShipmentParty", t => t.HasCheckConstraint("CK_ShipmentParty_Sequence", "[Sequence] >= 1"));

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Role).HasColumnName("PartyRoleId").HasConversion<byte>();

        builder.HasIndex(p => new { p.ShipmentId, p.Role, p.Sequence }).IsUnique();

        builder.HasOne(p => p.Shipment)
            .WithMany(s => s.Parties)
            .HasForeignKey(p => p.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Party)
            .WithMany()
            .HasForeignKey(p => p.PartyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<PartyRole>>()
            .WithMany()
            .HasForeignKey(p => p.Role)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConsignmentItemConfiguration : IEntityTypeConfiguration<ConsignmentItem>
{
    public void Configure(EntityTypeBuilder<ConsignmentItem> builder)
    {
        builder.ToTable("ConsignmentItem");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Description).HasMaxLength(300);
        builder.Property(i => i.PackageType).HasColumnName("PackageTypeId").HasConversion<byte>();
        builder.Property(i => i.GrossWeightKg).HasPrecision(12, 3);
        builder.Property(i => i.VolumeM3).HasPrecision(10, 3);
        builder.Property(i => i.UnNumber).HasMaxLength(4).IsUnicode(false);
        builder.Property(i => i.ProperShippingName).HasMaxLength(200);
        builder.Property(i => i.AdrClass).HasMaxLength(10).IsUnicode(false);
        builder.Property(i => i.PackingGroup).HasMaxLength(3).IsUnicode(false);

        builder.HasIndex(i => new { i.ShipmentId, i.SequenceNumber }).IsUnique();

        builder.HasOne(i => i.Shipment)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<PackageType>>()
            .WithMany()
            .HasForeignKey(i => i.PackageType)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EftiDatasetConfiguration : IEntityTypeConfiguration<EftiDataset>
{
    public void Configure(EntityTypeBuilder<EftiDataset> builder)
    {
        builder.ToTable("EftiDataset", t => t.HasCheckConstraint("CK_EftiDataset_Version", "[Version] >= 1"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Status).HasColumnName("DatasetStatusId").HasConversion<byte>();
        builder.Property(d => d.SchemaVersion).HasMaxLength(20).IsUnicode(false);

        builder.HasIndex(d => new { d.ShipmentId, d.Version }).IsUnique();

        builder.HasOne(d => d.Shipment)
            .WithMany(s => s.Datasets)
            .HasForeignKey(d => d.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<DatasetStatus>>()
            .WithMany()
            .HasForeignKey(d => d.Status)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ValidationIssueConfiguration : IEntityTypeConfiguration<ValidationIssue>
{
    public void Configure(EntityTypeBuilder<ValidationIssue> builder)
    {
        builder.ToTable("ValidationIssue");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.RuleCode).HasMaxLength(50).IsUnicode(false);
        builder.Property(i => i.Severity).HasColumnName("IssueSeverityId").HasConversion<byte>();
        builder.Property(i => i.FieldPath).HasMaxLength(200).IsUnicode(false);
        builder.Property(i => i.Message).HasMaxLength(500);

        builder.HasIndex(i => i.EftiDatasetId);

        builder.HasOne(i => i.EftiDataset)
            .WithMany(d => d.Issues)
            .HasForeignKey(i => i.EftiDatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<IssueSeverity>>()
            .WithMany()
            .HasForeignKey(i => i.Severity)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

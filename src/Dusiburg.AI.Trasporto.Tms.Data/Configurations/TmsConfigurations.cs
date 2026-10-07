using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dusiburg.AI.Trasporto.Tms.Data.Configurations;

// Convenzioni (skill db-operations): tabelle al singolare, PK numerica "Id", FK qualificate (<Entity>Id), chiavi di business
// stringa come colonne univoche, enum come FK verso tabelle di lookup tinyint generate dal codice.
// Codici e chiavi in varchar, testi liberi in nvarchar.

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

internal sealed class TransportOrderStatusConfiguration() : EnumLookupConfiguration<TransportOrderStatus>("TransportOrderStatus");

internal sealed class VehicleTypeConfiguration() : EnumLookupConfiguration<VehicleType>("VehicleType");

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

    /// <summary>Più largo di un codice ISO a due lettere: il TMS accetta anche valori sbagliati (<c>ITA</c>).</summary>
    public const int CountryCode = 3;

    public const int PlateNumber = 12;
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customer");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(ColumnSizes.Code).IsUnicode(false);
        builder.Property(c => c.Name).HasMaxLength(ColumnSizes.Name);
        builder.Property(c => c.VatNumber).HasMaxLength(ColumnSizes.VatNumber).IsUnicode(false);
        builder.Property(c => c.Street).HasMaxLength(ColumnSizes.Street);
        builder.Property(c => c.PostalCode).HasMaxLength(ColumnSizes.PostalCode).IsUnicode(false);
        builder.Property(c => c.City).HasMaxLength(ColumnSizes.City);
        builder.Property(c => c.CountryCode).HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
        builder.Property(c => c.Email).HasMaxLength(254).IsUnicode(false);

        builder.HasIndex(c => c.Code).IsUnique();
        builder.HasIndex(c => c.VatNumber).IsUnique().HasFilter("[VatNumber] IS NOT NULL");
    }
}

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicle");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.PlateNumber).HasMaxLength(ColumnSizes.PlateNumber).IsUnicode(false);
        builder.Property(v => v.CountryCode).HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
        builder.Property(v => v.Type).HasColumnName("VehicleTypeId").HasConversion<byte>();

        builder.HasIndex(v => v.PlateNumber).IsUnique();

        builder.HasOne<EnumLookup<VehicleType>>()
            .WithMany()
            .HasForeignKey(v => v.Type)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Driver");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Code).HasMaxLength(ColumnSizes.Code).IsUnicode(false);
        builder.Property(d => d.FirstName).HasMaxLength(100);
        builder.Property(d => d.LastName).HasMaxLength(100);

        builder.HasIndex(d => d.Code).IsUnique();
    }
}

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.ToTable("Trip");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code).HasMaxLength(ColumnSizes.Code).IsUnicode(false);
        builder.Property(t => t.SubcontractorName).HasMaxLength(ColumnSizes.Name);
        builder.Property(t => t.SubcontractorVatNumber).HasMaxLength(ColumnSizes.VatNumber).IsUnicode(false);
        builder.Property(t => t.SubcontractorCountryCode).HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);

        builder.HasIndex(t => t.Code).IsUnique();

        builder.HasOne(t => t.Vehicle)
            .WithMany()
            .HasForeignKey(t => t.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Trailer)
            .WithMany()
            .HasForeignKey(t => t.TrailerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Driver)
            .WithMany()
            .HasForeignKey(t => t.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransportOrderConfiguration : IEntityTypeConfiguration<TransportOrder>
{
    public void Configure(EntityTypeBuilder<TransportOrder> builder)
    {
        builder.ToTable("TransportOrder", t =>
        {
            t.HasCheckConstraint("CK_TransportOrder_TotalPackages", "[TotalPackages] >= 0");
            t.HasCheckConstraint("CK_TransportOrder_TotalGrossWeightKg", "[TotalGrossWeightKg] >= 0");
        });

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Code).HasMaxLength(ColumnSizes.Code).IsUnicode(false);
        builder.Property(o => o.Status).HasColumnName("TransportOrderStatusId").HasConversion<byte>();
        builder.Property(o => o.TotalGrossWeightKg).HasPrecision(12, 3);
        builder.Property(o => o.CmrNumber).HasMaxLength(35).IsUnicode(false);
        builder.Property(o => o.FreightAmount).HasPrecision(18, 2);
        builder.Property(o => o.FreightPayment).HasColumnName("FreightPaymentId").HasConversion<byte>();
        builder.Property(o => o.CurrencyCode).HasMaxLength(3).IsUnicode(false);
        builder.Property(o => o.CodAmount).HasPrecision(18, 2);
        builder.Property(o => o.DeclaredValue).HasPrecision(18, 2);
        builder.Property(o => o.SenderInstructions).HasMaxLength(1000);
        builder.Property(o => o.CmrIssuePlace).HasMaxLength(ColumnSizes.City);
        builder.Property(o => o.CustomsInstructions).HasMaxLength(500);

        builder.OwnsOne(o => o.Consignor, party => ConfigureParty(party, "Consignor"));
        builder.OwnsOne(o => o.Consignee, party => ConfigureParty(party, "Consignee"));
        builder.OwnsOne(o => o.Pickup, place => ConfigurePlace(place, "Pickup"));
        builder.OwnsOne(o => o.Delivery, place => ConfigurePlace(place, "Delivery"));

        // Tutte le colonne dei tipi posseduti sono annullabili: senza "required" EF leggerebbe null l'intero oggetto.
        builder.Navigation(o => o.Consignor).IsRequired();
        builder.Navigation(o => o.Consignee).IsRequired();
        builder.Navigation(o => o.Pickup).IsRequired();
        builder.Navigation(o => o.Delivery).IsRequired();

        builder.HasIndex(o => o.Code).IsUnique();
        builder.HasIndex(o => o.OrderDate);

        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Trip)
            .WithMany(t => t.Orders)
            .HasForeignKey(o => o.TripId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<TransportOrderStatus>>()
            .WithMany()
            .HasForeignKey(o => o.Status)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<FreightPayment>>()
            .WithMany()
            .HasForeignKey(o => o.FreightPayment)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureParty(OwnedNavigationBuilder<TransportOrder, OrderParty> party, string prefix)
    {
        party.Property(p => p.Name).HasColumnName(prefix + "Name").HasMaxLength(ColumnSizes.Name);
        party.Property(p => p.VatNumber).HasColumnName(prefix + "VatNumber").HasMaxLength(ColumnSizes.VatNumber).IsUnicode(false);
        party.Property(p => p.Street).HasColumnName(prefix + "Street").HasMaxLength(ColumnSizes.Street);
        party.Property(p => p.PostalCode).HasColumnName(prefix + "PostalCode").HasMaxLength(ColumnSizes.PostalCode).IsUnicode(false);
        party.Property(p => p.City).HasColumnName(prefix + "City").HasMaxLength(ColumnSizes.City);
        party.Property(p => p.CountryCode).HasColumnName(prefix + "CountryCode").HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
    }

    private static void ConfigurePlace(OwnedNavigationBuilder<TransportOrder, OrderPlace> place, string prefix)
    {
        place.Property(p => p.Street).HasColumnName(prefix + "Street").HasMaxLength(ColumnSizes.Street);
        place.Property(p => p.PostalCode).HasColumnName(prefix + "PostalCode").HasMaxLength(ColumnSizes.PostalCode).IsUnicode(false);
        place.Property(p => p.City).HasColumnName(prefix + "City").HasMaxLength(ColumnSizes.City);
        place.Property(p => p.CountryCode).HasColumnName(prefix + "CountryCode").HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
    }
}

internal sealed class TransportOrderLineConfiguration : IEntityTypeConfiguration<TransportOrderLine>
{
    public void Configure(EntityTypeBuilder<TransportOrderLine> builder)
    {
        builder.ToTable("TransportOrderLine", t =>
        {
            t.HasCheckConstraint("CK_TransportOrderLine_PackageCount", "[PackageCount] >= 0");
            t.HasCheckConstraint("CK_TransportOrderLine_GrossWeightKg", "[GrossWeightKg] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Description).HasMaxLength(300);
        builder.Property(l => l.PackageType).HasColumnName("PackageTypeId").HasConversion<byte>();
        builder.Property(l => l.GrossWeightKg).HasPrecision(12, 3);
        builder.Property(l => l.VolumeM3).HasPrecision(10, 3);
        builder.Property(l => l.UnNumber).HasMaxLength(4).IsUnicode(false);
        builder.Property(l => l.ProperShippingName).HasMaxLength(200);
        builder.Property(l => l.AdrClass).HasMaxLength(10).IsUnicode(false);
        builder.Property(l => l.PackingGroup).HasMaxLength(3).IsUnicode(false);
        builder.Property(l => l.Marks).HasMaxLength(100);

        builder.HasIndex(l => new { l.TransportOrderId, l.LineNumber }).IsUnique();

        builder.HasOne(l => l.TransportOrder)
            .WithMany(o => o.Lines)
            .HasForeignKey(l => l.TransportOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<PackageType>>()
            .WithMany()
            .HasForeignKey(l => l.PackageType)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransportOrderDocumentConfiguration : IEntityTypeConfiguration<TransportOrderDocument>
{
    public void Configure(EntityTypeBuilder<TransportOrderDocument> builder)
    {
        builder.ToTable("TransportOrderDocument", t => t.HasCheckConstraint("CK_TransportOrderDocument_Sequence", "[Sequence] >= 1"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.TypeCode).HasMaxLength(3).IsUnicode(false);
        builder.Property(d => d.Number).HasMaxLength(35);

        builder.HasIndex(d => new { d.TransportOrderId, d.Sequence }).IsUnique();

        builder.HasOne(d => d.TransportOrder)
            .WithMany(o => o.Documents)
            .HasForeignKey(d => d.TransportOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

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

internal sealed class EcmrStatusConfiguration() : EnumLookupConfiguration<EcmrStatus>("EcmrStatus");

internal sealed class ProposalStatusConfiguration() : EnumLookupConfiguration<ProposalStatus>("ProposalStatus");

internal sealed class ProposalSourceConfiguration() : EnumLookupConfiguration<ProposalSource>("ProposalSource");

/// <summary>Lunghezze delle colonne: le usa anche chi controlla un valore prima di scriverlo (<c>CompletableFields</c>).</summary>
public static class ColumnSizes
{
    public const int FieldPath = 200;
    public const int FieldValue = 200;

    /// <summary>Un valore estratto si salva anche se è più lungo di quanto il campo ammetta: lo dirà il controllo di formato.</summary>
    public const int ExtractedValue = 500;
    public const int SourceReference = 200;
    public const int Rationale = 1000;
    public const int CmrNumber = 35;
    public const int ProperShippingName = 200;
    public const int AdrClass = 10;
    public const int Marks = 100;
    public const int CustomsInstructions = 500;
    public const int DocumentNumber = 35;
    public const int RuleCode = 50;
    public const int IssueMessage = 500;

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
        builder.Property(s => s.VehicleTypeCode).HasMaxLength(3).IsUnicode(false);
        builder.Property(s => s.TrailerPlateNumber).HasMaxLength(ColumnSizes.PlateNumber).IsUnicode(false);
        builder.Property(s => s.TrailerCountryCode).HasMaxLength(ColumnSizes.CountryCode).IsUnicode(false);
        builder.Property(s => s.CmrNumber).HasMaxLength(ColumnSizes.CmrNumber).IsUnicode(false);
        builder.Property(s => s.FreightAmount).HasPrecision(18, 2);
        builder.Property(s => s.FreightPayment).HasColumnName("FreightPaymentId").HasConversion<byte>();
        builder.Property(s => s.CurrencyCode).HasMaxLength(3).IsUnicode(false);
        builder.Property(s => s.CodAmount).HasPrecision(18, 2);
        builder.Property(s => s.DeclaredValue).HasPrecision(18, 2);
        builder.Property(s => s.SenderInstructions).HasMaxLength(1000);
        builder.Property(s => s.CmrIssuePlace).HasMaxLength(ColumnSizes.City);
        builder.Property(s => s.CustomsInstructions).HasMaxLength(ColumnSizes.CustomsInstructions);

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

internal sealed class IdentifierRegistryEntryConfiguration : IEntityTypeConfiguration<IdentifierRegistryEntry>
{
    public void Configure(EntityTypeBuilder<IdentifierRegistryEntry> builder)
    {
        builder.ToTable("IdentifierRegistryEntry");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Uil).HasMaxLength(150).IsUnicode(false);
        builder.Property(e => e.VehiclePlateNumber).HasMaxLength(ColumnSizes.PlateNumber).IsUnicode(false);
        builder.Property(e => e.TrailerPlateNumber).HasMaxLength(ColumnSizes.PlateNumber).IsUnicode(false);

        builder.HasIndex(e => e.Uil).IsUnique();
        builder.HasIndex(e => e.ShipmentId).IsUnique();

        // La ricerca dell'autorità è per targa, della motrice o del rimorchio.
        builder.HasIndex(e => e.VehiclePlateNumber);
        builder.HasIndex(e => e.TrailerPlateNumber);

        builder.HasOne(e => e.Shipment)
            .WithOne()
            .HasForeignKey<IdentifierRegistryEntry>(e => e.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
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
        builder.Property(i => i.ProperShippingName).HasMaxLength(ColumnSizes.ProperShippingName);
        builder.Property(i => i.AdrClass).HasMaxLength(ColumnSizes.AdrClass).IsUnicode(false);
        builder.Property(i => i.PackingGroup).HasMaxLength(3).IsUnicode(false);
        builder.Property(i => i.Marks).HasMaxLength(ColumnSizes.Marks);

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

internal sealed class FieldProposalConfiguration : IEntityTypeConfiguration<FieldProposal>
{
    public void Configure(EntityTypeBuilder<FieldProposal> builder)
    {
        builder.ToTable("FieldProposal");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.FieldPath).HasMaxLength(ColumnSizes.FieldPath).IsUnicode(false);
        builder.Property(p => p.ProposedValue).HasMaxLength(ColumnSizes.FieldValue);
        builder.Property(p => p.CurrentValue).HasMaxLength(ColumnSizes.FieldValue);
        builder.Property(p => p.Source).HasColumnName("ProposalSourceId").HasConversion<byte>();
        builder.Property(p => p.SourceReference).HasMaxLength(ColumnSizes.SourceReference);
        builder.Property(p => p.Rationale).HasMaxLength(ColumnSizes.Rationale);
        builder.Property(p => p.Status).HasColumnName("ProposalStatusId").HasConversion<byte>();
        builder.Property(p => p.ProposedById).HasMaxLength(50).IsUnicode(false);
        builder.Property(p => p.ProposedByName).HasMaxLength(ColumnSizes.Name);
        builder.Property(p => p.DecidedById).HasMaxLength(50).IsUnicode(false);
        builder.Property(p => p.DecidedByName).HasMaxLength(ColumnSizes.Name);
        builder.Property(p => p.DecisionNote).HasMaxLength(ColumnSizes.Rationale);

        // Proposte di una spedizione, e ricerca della stessa proposta ancora in attesa (idempotenza del tool).
        builder.HasIndex(p => new { p.ShipmentId, p.Status, p.FieldPath });

        builder.HasOne(p => p.Shipment)
            .WithMany()
            .HasForeignKey(p => p.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<ProposalSource>>()
            .WithMany()
            .HasForeignKey(p => p.Source)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnumLookup<ProposalStatus>>()
            .WithMany()
            .HasForeignKey(p => p.Status)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExtractionFieldOutcomeConfiguration() : EnumLookupConfiguration<ExtractionFieldOutcome>("ExtractionFieldOutcome");

internal sealed class ReviewStatusConfiguration() : EnumLookupConfiguration<ReviewStatus>("ReviewStatus");

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Document");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.FileName).HasMaxLength(200);
        builder.Property(d => d.Sha256).HasMaxLength(64).IsUnicode(false);
        builder.Property(d => d.UploadedById).HasMaxLength(50).IsUnicode(false);
        builder.Property(d => d.UploadedByName).HasMaxLength(ColumnSizes.Name);

        // Documenti di una spedizione, dal più recente.
        builder.HasIndex(d => d.ShipmentId);

        builder.HasOne(d => d.Shipment)
            .WithMany()
            .HasForeignKey(d => d.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExtractionResultConfiguration : IEntityTypeConfiguration<ExtractionResult>
{
    public void Configure(EntityTypeBuilder<ExtractionResult> builder)
    {
        builder.ToTable("ExtractionResult");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ModelProvider).HasMaxLength(50).IsUnicode(false);
        builder.Property(r => r.ModelId).HasMaxLength(100).IsUnicode(false);
        builder.Property(r => r.Threshold).HasPrecision(4, 3);
        builder.Property(r => r.LiteralWeight).HasPrecision(4, 3);
        builder.Property(r => r.FormatWeight).HasPrecision(4, 3);
        builder.Property(r => r.TmsWeight).HasPrecision(4, 3);
        builder.Property(r => r.AgreementWeight).HasPrecision(4, 3);
        builder.Property(r => r.AnnotationWeight).HasPrecision(4, 3);

        builder.HasIndex(r => r.DocumentId).IsUnique();

        builder.HasOne(r => r.Document)
            .WithOne(d => d.Extraction)
            .HasForeignKey<ExtractionResult>(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExtractionFieldConfiguration : IEntityTypeConfiguration<ExtractionField>
{
    public void Configure(EntityTypeBuilder<ExtractionField> builder)
    {
        builder.ToTable("ExtractionField", t => t.HasCheckConstraint("CK_ExtractionField_Confidence", "[Confidence] >= 0 AND [Confidence] <= 1"));

        builder.HasKey(f => f.Id);

        builder.Property(f => f.FieldPath).HasMaxLength(ColumnSizes.FieldPath).IsUnicode(false);
        builder.Property(f => f.CmrBox).HasMaxLength(5).IsUnicode(false);
        builder.Property(f => f.Label).HasMaxLength(100);
        builder.Property(f => f.Value).HasMaxLength(ColumnSizes.ExtractedValue);
        builder.Property(f => f.NormalizedValue).HasMaxLength(ColumnSizes.FieldValue);
        builder.Property(f => f.CurrentValue).HasMaxLength(ColumnSizes.FieldValue);
        builder.Property(f => f.Confidence).HasPrecision(4, 3);
        builder.Property(f => f.Outcome).HasColumnName("ExtractionFieldOutcomeId").HasConversion<byte>();
        builder.Property(f => f.Note).HasMaxLength(500);

        builder.HasIndex(f => f.ExtractionResultId);

        builder.HasOne(f => f.ExtractionResult)
            .WithMany(r => r.Fields)
            .HasForeignKey(f => f.ExtractionResultId)
            .OnDelete(DeleteBehavior.Cascade);

        // La proposta appartiene alla spedizione, come il documento: una seconda cancellazione a cascata verso la stessa riga
        // SQL Server non la ammette.
        builder.HasOne(f => f.FieldProposal)
            .WithMany()
            .HasForeignKey(f => f.FieldProposalId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<EnumLookup<ExtractionFieldOutcome>>()
            .WithMany()
            .HasForeignKey(f => f.Outcome)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReviewTaskConfiguration : IEntityTypeConfiguration<ReviewTask>
{
    public void Configure(EntityTypeBuilder<ReviewTask> builder)
    {
        builder.ToTable("ReviewTask");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Status).HasColumnName("ReviewStatusId").HasConversion<byte>();
        builder.Property(t => t.DecidedById).HasMaxLength(50).IsUnicode(false);
        builder.Property(t => t.DecidedByName).HasMaxLength(ColumnSizes.Name);
        builder.Property(t => t.CorrectedValue).HasMaxLength(ColumnSizes.FieldValue);
        builder.Property(t => t.Note).HasMaxLength(ColumnSizes.Rationale);

        builder.HasIndex(t => t.ExtractionFieldId).IsUnique();

        // La coda: voci in attesa, dalla più vecchia.
        builder.HasIndex(t => t.Status);

        builder.HasOne(t => t.ExtractionField)
            .WithOne(f => f.ReviewTask)
            .HasForeignKey<ReviewTask>(t => t.ExtractionFieldId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<ReviewStatus>>()
            .WithMany()
            .HasForeignKey(t => t.Status)
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

internal sealed class AccompanyingDocumentConfiguration : IEntityTypeConfiguration<AccompanyingDocument>
{
    public void Configure(EntityTypeBuilder<AccompanyingDocument> builder)
    {
        builder.ToTable("AccompanyingDocument", t => t.HasCheckConstraint("CK_AccompanyingDocument_Sequence", "[Sequence] >= 1"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.TypeCode).HasMaxLength(3).IsUnicode(false);
        builder.Property(d => d.Number).HasMaxLength(ColumnSizes.DocumentNumber);

        builder.HasIndex(d => new { d.ShipmentId, d.Sequence }).IsUnique();

        builder.HasOne(d => d.Shipment)
            .WithMany(s => s.AccompanyingDocuments)
            .HasForeignKey(d => d.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EcmrDocumentConfiguration : IEntityTypeConfiguration<EcmrDocument>
{
    public void Configure(EntityTypeBuilder<EcmrDocument> builder)
    {
        builder.ToTable("EcmrDocument", t => t.HasCheckConstraint("CK_EcmrDocument_Version", "[Version] >= 1"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Status).HasColumnName("EcmrStatusId").HasConversion<byte>();
        builder.Property(d => d.SchemaVersion).HasMaxLength(20).IsUnicode(false);

        builder.HasIndex(d => new { d.ShipmentId, d.Version }).IsUnique();

        builder.HasOne(d => d.Shipment)
            .WithMany(s => s.EcmrDocuments)
            .HasForeignKey(d => d.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<EcmrStatus>>()
            .WithMany()
            .HasForeignKey(d => d.Status)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EcmrIssueConfiguration : IEntityTypeConfiguration<EcmrIssue>
{
    public void Configure(EntityTypeBuilder<EcmrIssue> builder)
    {
        builder.ToTable("EcmrIssue");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.RuleCode).HasMaxLength(ColumnSizes.RuleCode).IsUnicode(false);
        builder.Property(i => i.Severity).HasColumnName("IssueSeverityId").HasConversion<byte>();
        builder.Property(i => i.FieldPath).HasMaxLength(ColumnSizes.FieldPath).IsUnicode(false);
        builder.Property(i => i.Message).HasMaxLength(ColumnSizes.IssueMessage);

        builder.HasIndex(i => i.EcmrDocumentId);

        builder.HasOne(i => i.EcmrDocument)
            .WithMany(d => d.Issues)
            .HasForeignKey(i => i.EcmrDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<EnumLookup<IssueSeverity>>()
            .WithMany()
            .HasForeignKey(i => i.Severity)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

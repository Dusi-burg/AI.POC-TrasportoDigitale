using Dusiburg.AI.Trasporto.Efti.Core.Proposals;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>
/// I campi che una proposta può completare: quali esistono su una spedizione, quali valori accettano e dove li scrivono.
/// Spedizioni del seed, nessun database.
/// </summary>
public class CompletableFieldsTests
{
    private static readonly EftiSchemaCatalog Catalog = EftiSchemaCatalog.Default;

    /// <summary>Un valore accettabile per ogni tipo di campo.</summary>
    private static string SampleValue(FieldValueKind kind) => kind switch
    {
        FieldValueKind.Country => "FR",
        FieldValueKind.VatNumber => "FR99999000022",
        FieldValueKind.PlateNumber => "ZZ999ZZ",
        FieldValueKind.Date => "2026-11-20",
        FieldValueKind.PositiveInteger => "7",
        FieldValueKind.PositiveDecimal => "1234.5",
        FieldValueKind.UnNumber => "2014",
        FieldValueKind.PackingGroup => "II",
        _ => "PROVA 7"
    };

    private static IEnumerable<TestCaseData> Fields() =>
        CompletableFields.All.Select(field => new TestCaseData(field).SetArgDisplayNames(field.Path));

    [TestCaseSource(nameof(Fields))]
    public void Field_WrittenOnAShipment_IsReadBackWithTheSameValue(CompletableField field)
    {
        //SETUP
        // SH-1004 ha ogni soggetto, il rimorchio e una riga di merce pericolosa (la prima): ogni campo dell'elenco vi esiste.
        Shipment shipment = SeedShipments.Create("TO-1004");
        string path = field.Path.Replace(CompletableFields.ItemIndexPlaceholder, "[0]", StringComparison.Ordinal);
        string value = CompletableFields.Normalize(field, SampleValue(field.Kind), Catalog, out string? valueError)!;

        //SUT
        FieldTarget? target = CompletableFields.Resolve(shipment, path, out string? error);
        target?.Write(shipment, value);

        Assert.That((error, valueError), Is.EqualTo(((string?)null, (string?)null)));
        Assert.That(target!.CanonicalPath, Is.EqualTo(path));
        Assert.That(target.Read(shipment), Is.EqualTo(value));
    }

    [Test]
    public void Fields_AreAllDistinctAndDescribed()
    {
        //SUT
        IReadOnlyList<CompletableField> fields = CompletableFields.All;

        Assert.That(fields.Select(f => f.Path), Is.Unique);
        Assert.That(fields.Select(f => f.Description), Has.None.Empty);
        Assert.That(fields.Where(f => f.Kind == FieldValueKind.Text).Select(f => f.MaxLength), Has.All.GreaterThan(0), "un testo ha sempre una lunghezza massima");
        Assert.That(fields.Select(f => f.Path), Has.None.StartsWith("carrier/"), "il vettore viene dalla configurazione: non si propone");
    }

    [Test]
    public void Resolve_PathOfAValidationIssue_FindsTheField()
    {
        //SETUP
        // SH-1002: CAP del destinatario mancante, numero ONU e denominazione mancanti sulla prima riga, peso incoerente.
        Shipment shipment = SeedShipments.Create("TO-1002");
        IReadOnlyList<ValidationFinding> errors = [.. EftiValidator.CreateDefault().ApplyRules(shipment).Where(f => f.Severity == IssueSeverity.Error)];

        //SUT
        var resolved = errors.Select(f => (f.RuleCode, f.FieldPath, Target: CompletableFields.Resolve(shipment, f.FieldPath, out _))).ToList();

        Assert.That(errors.Select(f => f.RuleCode), Is.EquivalentTo(new[] { "M-CONSIGNEE-ADDRESS", "M-ADR-UN-NUMBER", "M-ADR-SHIPPING-NAME", "C-WEIGHT-SUM" }));
        Assert.That(resolved.Where(r => r.Target is null).Select(r => r.FieldPath), Is.Empty, "ogni errore dello scenario 2 indica un campo completabile");
        Assert.That(resolved.Select(r => r.Target!.CanonicalPath), Is.EquivalentTo(new[]
        {
            "consignee/postalAddress/postcode",
            "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID",
            "includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName",
            "grossWeight"
        }));
    }

    [TestCase("consignee/postalAddress/postcode", "consignee/postalAddress/postcode")]
    [TestCase("  consignee/postalAddress/postcode ", "consignee/postalAddress/postcode")]
    [TestCase("mainCarriageTransportMovement/usedTransportMeans/id", "mainCarriageTransportMovement[0]/usedTransportMeans/id")]
    [TestCase("includedConsignmentItem[0]/transportDangerousGoods/uNDGID", "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID")]
    [TestCase("transportPackage[1]/itemQuantity", "transportPackage[1]/itemQuantity")]
    public void Resolve_EquivalentSpellings_GiveTheCanonicalPath(string fieldPath, string expected)
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1002");

        //SUT
        FieldTarget? target = CompletableFields.Resolve(shipment, fieldPath, out string? error);

        Assert.That(error, Is.Null);
        Assert.That(target!.CanonicalPath, Is.EqualTo(expected));
    }

    [TestCase("carrier/name", "non è completabile")]
    [TestCase("transportContractDocument/typeCode", "non è completabile")]
    [TestCase("consignee[1]/name", "non valido")]
    [TestCase("consignee//name", "non valido")]
    [TestCase("", "non valido")]
    [TestCase("includedConsignmentItem[5]/transportDangerousGoods[0]/uNDGID", "non esiste")]
    [TestCase("includedConsignmentItem[1]/transportDangerousGoods[0]/uNDGID", "non è merce pericolosa")]
    public void Resolve_FieldThatCannotBeCompleted_ExplainsWhy(string fieldPath, string expectedReason)
    {
        //SETUP
        // SH-1002 ha due righe: la prima è merce pericolosa, la seconda no.
        Shipment shipment = SeedShipments.Create("TO-1002");

        //SUT
        FieldTarget? target = CompletableFields.Resolve(shipment, fieldPath, out string? error);

        Assert.That(target, Is.Null);
        Assert.That(error, Does.Contain(expectedReason));
    }

    [TestCase("consignee/postalAddress/countryCode", " fr ", "FR")]
    [TestCase("consignee/taxRegistration/id", "fr99999000022", "FR99999000022")]
    [TestCase("usedTransportEquipment[0]/id", "xz-102 aa", "XZ-102 AA")]
    [TestCase("grossWeight", "8840.0", "8840")]
    [TestCase("grossWeight", "8840.25", "8840.25")]
    [TestCase("numberOfPackages", "022", "22")]
    [TestCase("includedConsignmentItem[0]/transportDangerousGoods[0]/packagingDangerLevelCode", "ii", "II")]
    [TestCase("deliveryEvent/scheduledOccurrenceDateTime", "2026-10-08", "2026-10-08")]
    public void Normalize_AcceptableValue_IsStoredInOneForm(string fieldPath, string value, string expected)
    {
        //SETUP
        CompletableField field = CompletableFields.Resolve(SeedShipments.Create("TO-1002"), fieldPath, out _)!.Field;

        //SUT
        string? normalized = CompletableFields.Normalize(field, value, Catalog, out string? error);

        Assert.That((normalized, error), Is.EqualTo((expected, (string?)null)));
    }

    [TestCase("consignee/postalAddress/countryCode", "Francia")]
    [TestCase("consignee/postalAddress/countryCode", "XX")]
    [TestCase("consignee/taxRegistration/id", "99999000022")]
    [TestCase("consignee/taxRegistration/id", "IT01000010008")]
    [TestCase("usedTransportEquipment[0]/id", "AB1")]
    [TestCase("grossWeight", "8.840,5")]
    [TestCase("grossWeight", "8,840")]
    [TestCase("grossWeight", "0")]
    [TestCase("grossWeight", "-12")]
    [TestCase("numberOfPackages", "12.5")]
    [TestCase("numberOfPackages", "0")]
    [TestCase("includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "UN2014")]
    [TestCase("includedConsignmentItem[0]/transportDangerousGoods[0]/packagingDangerLevelCode", "IV")]
    [TestCase("deliveryEvent/scheduledOccurrenceDateTime", "08/10/2026")]
    [TestCase("consignee/postalAddress/postcode", "un CAP decisamente troppo lungo")]
    [TestCase("consignee/postalAddress/postcode", "   ")]
    public void Normalize_UnacceptableValue_IsRefusedWithAReason(string fieldPath, string value)
    {
        //SETUP
        CompletableField field = CompletableFields.Resolve(SeedShipments.Create("TO-1002"), fieldPath, out _)!.Field;

        //SUT
        string? normalized = CompletableFields.Normalize(field, value, Catalog, out string? error);

        Assert.That(normalized, Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ScenarioTwo_AfterTheFourCompletions_TheDatasetIsValid()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1002");
        EftiValidator validator = EftiValidator.CreateDefault();
        (string Path, string Value)[] completions =
        [
            ("consignee/postalAddress/postcode", "69007"),
            ("includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "2014"),
            ("includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName", "PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA"),
            ("grossWeight", "8840")
        ];
        DatasetStatus before = validator.Evaluate(shipment).Status;

        //SUT
        foreach ((string path, string value) in completions)
        {
            FieldTarget target = CompletableFields.Resolve(shipment, path, out _)!;

            target.Write(shipment, CompletableFields.Normalize(target.Field, value, Catalog, out _)!);
        }

        DatasetEvaluation after = validator.Evaluate(shipment);
        Assert.That(before, Is.EqualTo(DatasetStatus.Incomplete));
        Assert.That(after.Findings.Where(f => f.Severity == IssueSeverity.Error).Select(f => $"{f.RuleCode} {f.Message}"), Is.Empty);
        Assert.That(after.Status, Is.EqualTo(DatasetStatus.Valid));
        Assert.That(after.Json, Does.Contain("\"2014\"").And.Contain("69007"));
    }
}

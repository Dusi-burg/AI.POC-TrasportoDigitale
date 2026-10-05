using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>Regole di validazione sulle spedizioni demo e sui difetti voluti del seed: codice deterministico, esiti esatti.</summary>
public class EftiValidatorTests
{
    private static readonly EftiValidator Validator = EftiValidator.CreateDefault();

    /// <summary>Le regole che devono riconoscere ciascun difetto del seed (fase-3.md §5).</summary>
    private static readonly Dictionary<SeedDefect, string[]> RulesByDefect = new()
    {
        [SeedDefect.MissingConsigneeAddress] = ["M-CONSIGNEE-ADDRESS", "M-DELIVERY-LOCATION"],
        [SeedDefect.MissingGoodsDescription] = ["M-GOODS-DESCRIPTION"],
        [SeedDefect.MissingPackageCount] = ["M-PACKAGES"],
        [SeedDefect.WeightMismatch] = ["C-WEIGHT-SUM"],
        [SeedDefect.AdrWithoutUnNumber] = ["M-ADR-UN-NUMBER", "M-ADR-SHIPPING-NAME"],
        [SeedDefect.InvalidCountryCode] = ["F-COUNTRY"],
        [SeedDefect.DeliveryBeforePickup] = ["C-DELIVERY-BEFORE-PICKUP"]
    };

    [TestCase("TO-1001", DatasetStatus.Valid)]
    [TestCase("TO-1002", DatasetStatus.Incomplete, "M-CONSIGNEE-ADDRESS", "M-ADR-UN-NUMBER", "M-ADR-SHIPPING-NAME", "C-WEIGHT-SUM", "W-PARTY-VAT")]
    [TestCase("TO-1003", DatasetStatus.Incomplete, "M-PACKAGES", "M-GROSS-WEIGHT")]
    [TestCase("TO-1004", DatasetStatus.Valid)]
    [TestCase("TO-1005", DatasetStatus.Valid)]
    [TestCase("TO-1006", DatasetStatus.Valid)]
    public void Evaluate_DemoShipment_FindsExactlyTheExpectedIssues(string orderCode, DatasetStatus expectedStatus, params string[] expectedRules)
    {
        //SETUP
        Shipment shipment = SeedShipments.Create(orderCode);

        //SUT
        DatasetEvaluation evaluation = Validator.Evaluate(shipment);

        Assert.That(evaluation.Findings.Select(f => f.RuleCode).Distinct(), Is.EquivalentTo(expectedRules), Describe(evaluation));
        Assert.That(evaluation.Status, Is.EqualTo(expectedStatus));
    }

    [Test]
    public void Evaluate_DefectiveDemoShipment_PointsAtTheFieldsToFix()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1002");

        //SUT
        DatasetEvaluation evaluation = Validator.Evaluate(shipment);

        Assert.That(evaluation.Findings.Select(f => (f.RuleCode, f.FieldPath)), Is.EquivalentTo(new[]
        {
            ("M-CONSIGNEE-ADDRESS", "consignee/postalAddress/postcode"),
            ("M-ADR-UN-NUMBER", "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID"),
            ("M-ADR-SHIPPING-NAME", "includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName"),
            ("C-WEIGHT-SUM", "grossWeight"),
            ("W-PARTY-VAT", "consignee/taxRegistration/id")
        }));
        Assert.That((evaluation.Errors, evaluation.Warnings), Is.EqualTo((4, 1)));
        Assert.That(evaluation.Findings.Single(f => f.RuleCode == "C-WEIGHT-SUM").Message, Does.Contain("8480").And.Contain("8840"));
    }

    [Test]
    public void Evaluate_GeneratedShipments_ErrorsAreExactlyThoseOfTheSeedDefects()
    {
        //SETUP
        HashSet<string> demoOrders = [.. DemoCatalog.Orders.Select(o => o.Code)];
        Shipment[] generated = [.. SeedShipments.All().Where(s => !demoOrders.Contains(s.TmsOrderCode))];

        //SUT
        foreach (Shipment shipment in generated)
        {
            DatasetEvaluation evaluation = Validator.Evaluate(shipment);

            IReadOnlyList<SeedDefect> defects = SeedShipments.Seed.DefectsByOrderCode.GetValueOrDefault(shipment.TmsOrderCode) ?? [];
            IEnumerable<string> expectedErrors = defects.SelectMany(d => RulesByDefect[d]).Distinct();
            IEnumerable<string> errors = evaluation.Findings.Where(f => f.Severity == IssueSeverity.Error).Select(f => f.RuleCode).Distinct();

            Assert.That(errors, Is.EquivalentTo(expectedErrors), $"{shipment.TmsOrderCode} [{string.Join(", ", defects)}]: {Describe(evaluation)}");
            Assert.That(evaluation.Status, Is.EqualTo(defects.Count == 0 ? DatasetStatus.Valid : DatasetStatus.Incomplete), shipment.TmsOrderCode);
        }
    }

    [Test]
    public void Evaluate_Seed_EveryDefectTypeIsRecognised()
    {
        //SETUP
        Shipment[] shipments = [.. SeedShipments.All()];

        //SUT
        HashSet<string> rulesFired = [.. shipments.SelectMany(s => Validator.ApplyRules(s)).Select(f => f.RuleCode)];

        Assert.That(rulesFired, Is.SupersetOf(RulesByDefect.Values.SelectMany(r => r).Distinct()));
        Assert.That(RulesByDefect.Keys, Is.EquivalentTo(Enum.GetValues<SeedDefect>()), "ogni difetto del seed ha le sue regole attese");
    }

    [Test]
    public void Evaluate_WarningsOnly_DatasetStaysValid()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1001");
        shipment.CmrNumber = null;
        shipment.Parties.Single(p => p.Role == PartyRole.Consignee).Party.VatNumber = null;

        //SUT
        DatasetEvaluation evaluation = Validator.Evaluate(shipment);

        Assert.That(evaluation.Findings.Select(f => f.RuleCode), Is.EquivalentTo(new[] { "W-CMR-NUMBER", "W-PARTY-VAT" }));
        Assert.That(evaluation.Findings.Select(f => f.Severity), Is.All.EqualTo(IssueSeverity.Warning));
        Assert.That(evaluation.Status, Is.EqualTo(DatasetStatus.Valid));
    }

    [Test]
    public void Evaluate_OnlyTheSchemaIsViolated_ReportsSchemaRule()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1001");
        shipment.Delivery.Street = new string('x', 36);

        //SUT
        DatasetEvaluation evaluation = Validator.Evaluate(shipment);

        Assert.That(evaluation.Findings.Select(f => f.RuleCode), Is.EqualTo(new[] { ValidationRules.SchemaRuleCode }));
        Assert.That(evaluation.Findings[0].Message, Does.Contain("streetName"));
        Assert.That(evaluation.Status, Is.EqualTo(DatasetStatus.Incomplete));
    }

    [Test]
    public void Evaluate_BlockingRuleViolated_SchemaIsNotChecked()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1001");
        shipment.Delivery.CountryCode = "ITA";

        //SUT
        DatasetEvaluation evaluation = Validator.Evaluate(shipment);

        // "ITA" violerebbe anche la lista dei paesi dell'XSD: lo dice già F-COUNTRY, con un messaggio comprensibile.
        Assert.That(evaluation.Findings.Select(f => f.RuleCode), Is.EqualTo(new[] { "F-COUNTRY" }));
    }

    [Test]
    public void Rules_AreUniquelyCodedAndPrefixedByCategory()
    {
        //SUT
        IReadOnlyList<ValidationRule> rules = ValidationRules.All;

        Assert.That(rules.Select(r => r.Code), Is.Unique);
        Assert.That(rules, Is.All.Matches<ValidationRule>(r => r.Code.StartsWith(Prefix(r.Category), StringComparison.Ordinal)));
        Assert.That(rules.Where(r => r.Category == RuleCategory.Advisory).Select(r => r.Severity), Is.All.EqualTo(IssueSeverity.Warning));
        Assert.That(rules.Where(r => r.Category != RuleCategory.Advisory).Select(r => r.Severity), Is.All.EqualTo(IssueSeverity.Error));
        Assert.That(rules.Select(r => r.Explanation), Is.All.Not.Empty);
    }

    [TestCase("IT01234560157", true)]
    [TestCase("it01234560157", true)]
    [TestCase("IT01234560158", false)]
    [TestCase("IT0123456015", false)]
    [TestCase("DE999000011", true)]
    [TestCase("NL999123456B01", true)]
    [TestCase("999000011", false)]
    [TestCase("D1", false)]
    public void IsVatNumber_Value_ChecksPrefixShapeAndItalianCheckDigit(string value, bool expected)
    {
        //SUT
        Assert.That(Formats.IsVatNumber(value), Is.EqualTo(expected));
    }

    [TestCase("ZZ101AA", true)]
    [TestCase("S-ZZ 9001", true)]
    [TestCase("AB 12", false)]
    [TestCase("TARGA-TROPPO-LUNGA", false)]
    public void IsPlateNumber_Value_AcceptsFiveToTenLettersAndDigits(string value, bool expected)
    {
        //SUT
        Assert.That(Formats.IsPlateNumber(value), Is.EqualTo(expected));
    }

    private static string Prefix(RuleCategory category) => category switch
    {
        RuleCategory.Mandatory => "M-",
        RuleCategory.Format => "F-",
        RuleCategory.Coherence => "C-",
        RuleCategory.Advisory => "W-",
        _ => "X-"
    };

    private static string Describe(DatasetEvaluation evaluation) =>
        string.Join(" | ", evaluation.Findings.Select(f => $"{f.RuleCode} {f.FieldPath}: {f.Message}"));
}

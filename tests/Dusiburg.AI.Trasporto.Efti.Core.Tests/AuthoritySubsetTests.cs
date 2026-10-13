using System.Text.Json.Nodes;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>Sottoinsiemi dell'autorità e sottoinsieme degli identificatori: proiezioni ricavate dallo schema, senza elenchi di campi.</summary>
public class AuthoritySubsetTests
{
    private static readonly EftiSchemaCatalog Catalog = EftiSchemaCatalog.Default;
    private static readonly EftiSubsetProjector Projector = EftiSubsetProjector.CreateDefault();
    private static readonly EftiValidator Validator = EftiValidator.CreateDefault();

    [Test]
    public void ProjectSubset_Eu03_KeepsTransportDataAndDropsTaxAndContractData()
    {
        //SETUP
        string dataset = DatasetOf("TO-1004");

        //SUT
        HashSet<string> paths = JsonPaths.Leaves(Projector.ProjectSubset(dataset, EftiSubsets.Eu03));

        Assert.That(paths, Is.SupersetOf(new[]
        {
            "consignor/name", "consignor/postalAddress/cityName", "consignee/name", "consignee/postalAddress/countryCode", "carrier/name",
            "carrierAcceptanceDateTime", "carrierAcceptanceLocation/postalAddress/cityName", "consigneeReceiptLocation/postalAddress/cityName",
            "natureIdentificationCargo/identificationText", "grossWeight", "grossVolume",
            "mainCarriageTransportMovement/usedTransportMeans/id", "mainCarriageTransportMovement/usedTransportMeans/typeCode",
            "mainCarriageTransportMovement/usedTransportMeans/registrationCountry/code",
            "usedTransportEquipment/id", "usedTransportEquipment/categoryCode",
            "includedConsignmentItem/transportDangerousGoods/uNDGID", "includedConsignmentItem/transportDangerousGoods/properShippingName"
        }));
        Assert.That(paths, Has.None.Contains("taxRegistration"), "partita IVA");
        Assert.That(paths, Has.None.AnyOf(
            "applicableServiceCharge/appliedAmount", "paymentArrangementCode", "transportContractDocument/id", "transshipmentPermittedIndicator",
            "deliveryEvent/scheduledOccurrenceDateTime", "numberOfPackages", "mainCarriageTransportMovement/modeCode"));
    }

    [Test]
    public void ProjectSubset_Eu05a_KeepsDangerousGoodsDetail()
    {
        //SETUP
        string dataset = DatasetOf("TO-1004");

        //SUT
        HashSet<string> paths = JsonPaths.Leaves(Projector.ProjectSubset(dataset, EftiSubsets.Eu05a));

        Assert.That(paths, Is.SupersetOf(new[]
        {
            "includedConsignmentItem/transportDangerousGoods/uNDGID",
            "includedConsignmentItem/transportDangerousGoods/hazardClassificationID",
            "includedConsignmentItem/transportDangerousGoods/packagingDangerLevelCode",
            "includedConsignmentItem/transportDangerousGoods/grossWeight",
            "includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/itemQuantity",
            "mainCarriageTransportMovement/modeCode", "consignor/name", "consignee/name", "carrier/name"
        }));
        Assert.That(paths, Has.None.AnyOf("grossWeight", "natureIdentificationCargo/identificationText", "carrierAcceptanceDateTime", "consigneeReceiptLocation/postalAddress/cityName"));
    }

    [TestCase(EftiSubsets.Eu03)]
    [TestCase(EftiSubsets.Eu05a)]
    public void ProjectSubset_EverySeedShipment_KeepsExactlyTheElementsTheSchemaAssignsToTheSubset(string subsetId)
    {
        //SUT
        foreach (string dataset in SeedShipments.All().Select(s => Validator.Evaluate(s).Json))
        {
            HashSet<string> projected = JsonPaths.Leaves(Projector.ProjectSubset(dataset, subsetId));

            // Atteso, calcolato in modo indipendente: un valore resta se lui e tutti i suoi contenitori sono annotati con il sottoinsieme.
            IEnumerable<string> expected = JsonPaths.Leaves(JsonNode.Parse(dataset)!).Where(path => IsWhollyInSubset(path, subsetId));

            Assert.That(projected, Is.EquivalentTo(expected));
        }
    }

    [Test]
    public void ProjectSubset_DoesNotChangeTheValuesItKeeps()
    {
        //SETUP
        string dataset = DatasetOf("TO-1001");

        //SUT
        JsonObject subset = Projector.ProjectSubset(dataset, EftiSubsets.Eu03);

        JsonNode full = JsonNode.Parse(dataset)!;
        Assert.That(JsonNode.DeepEquals(subset["consignor"]!["name"], full["consignor"]!["name"]), Is.True);
        Assert.That(JsonNode.DeepEquals(subset["grossWeight"], full["grossWeight"]), Is.True);
        Assert.That(JsonNode.DeepEquals(subset["carrierAcceptanceDateTime"], full["carrierAcceptanceDateTime"]), Is.True);
    }

    [Test]
    public void ProjectIdentifiers_ValidShipment_HoldsOnlyWhatTheGateNeeds()
    {
        //SETUP
        string dataset = DatasetOf("TO-1004");

        //SUT
        HashSet<string> paths = JsonPaths.Leaves(Projector.ProjectIdentifiers(dataset));

        Assert.That(paths, Is.EquivalentTo(new[]
        {
            "carrierAcceptanceDateTime",
            "mainCarriageTransportMovement/dangerousGoodsIndicator", "mainCarriageTransportMovement/modeCode",
            "mainCarriageTransportMovement/usedTransportMeans/id", "mainCarriageTransportMovement/usedTransportMeans/registrationCountry/code",
            "usedTransportEquipment/categoryCode", "usedTransportEquipment/id", "usedTransportEquipment/registrationCountry/code", "usedTransportEquipment/sequenceNumber"
        }));
    }

    [Test]
    public void ProjectIdentifiers_EveryValidSeedShipment_IsConformToTheIdentifierSchema()
    {
        //SETUP
        EftiSchemaCatalog identifierCatalog = EftiSchemaCatalog.Identifier;
        var exporter = new EftiXmlExporter(identifierCatalog);
        var xmlValidator = new EftiXmlValidator(identifierCatalog);
        string[] validDatasets = [.. SeedShipments.All().Select(s => Validator.Evaluate(s)).Where(e => e.Status == DatasetStatus.Valid).Select(e => e.Json)];

        //SUT
        string[] errors = [.. validDatasets
            .Select(dataset => Projector.ProjectIdentifiers(dataset).ToJsonString())
            .SelectMany(identifiers => xmlValidator.Validate(exporter.Export(identifiers)))
            .Distinct()];

        Assert.That(validDatasets, Has.Length.GreaterThan(90));
        Assert.That(errors, Is.Empty);
        Assert.That(identifierCatalog.Version, Is.EqualTo("0.6"));
    }

    [Test]
    public void Uil_RoundTrip_AndRejectsMalformedValues()
    {
        //SETUP
        var datasetId = Guid.Parse("0199b1a0-0000-7000-8000-000000000001");
        Uil uil = Uil.For(new EftiPlatformOptions(), datasetId);

        //SUT
        bool parsed = Uil.TryParse(uil.ToString(), out Uil roundTrip);

        Assert.That(uil.ToString(), Is.EqualTo("it-gate-sim:dusiburg-trasporto:0199b1a0-0000-7000-8000-000000000001"));
        Assert.That((parsed, roundTrip), Is.EqualTo((true, uil)));
        Assert.That(new[] { null, "", "solo-una-parte", "gate:platform:non-un-guid", "gate::0199b1a0-0000-7000-8000-000000000001", "a:b:c:d" }
            .Select(value => Uil.TryParse(value, out _)), Is.All.False);
    }

    [Test]
    public void GateRetention_Default_IsFiveDaysAfterDelivery()
    {
        //SETUP
        var deliveredAt = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(1));

        //SUT
        DateTimeOffset byDefault = new EftiPlatformOptions().ExpiresAt(deliveredAt);
        DateTimeOffset configured = new EftiPlatformOptions { GateRetentionMinutes = 15 }.ExpiresAt(deliveredAt);

        Assert.That(byDefault, Is.EqualTo(deliveredAt.AddDays(5)));
        Assert.That(configured, Is.EqualTo(deliveredAt.AddMinutes(15)));
    }

    [TestCase("zz-101 aa", "ZZ101AA")]
    [TestCase("ZZ101AA", "ZZ101AA")]
    [TestCase("  ", null)]
    [TestCase(null, null)]
    public void NormalizePlate_Value_UppercaseWithoutSpacesAndHyphens(string? value, string? expected)
    {
        //SUT
        Assert.That(PlateNumbers.Normalize(value), Is.EqualTo(expected));
    }

    private static string DatasetOf(string orderCode) => Validator.Evaluate(SeedShipments.Create(orderCode)).Json;

    private static bool IsWhollyInSubset(string path, string subsetId)
    {
        string[] segments = path.Split('/');

        return Enumerable.Range(1, segments.Length).All(length => Catalog.Find(string.Join('/', segments[..length]))!.IsInSubset(subsetId));
    }
}

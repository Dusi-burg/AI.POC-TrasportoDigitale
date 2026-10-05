using System.Xml.Linq;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>Esportazione XML del dataset e conformità allo schema XSD eFTI.</summary>
public class EftiXmlExportTests
{
    private static readonly EftiSchemaCatalog Catalog = EftiSchemaCatalog.Default;
    private static readonly EftiXmlExporter Exporter = new(Catalog);
    private static readonly EftiXmlValidator XmlValidator = new(Catalog);
    private static readonly EftiValidator Validator = EftiValidator.CreateDefault();

    private static readonly XNamespace Efti = Catalog.TargetNamespace;

    [Test]
    public void Export_CompleteShipment_IsConformAndFollowsTheSchemaOrder()
    {
        //SETUP
        EftiConsignment dataset = EftiDatasetMapper.Map(SeedShipments.Create("TO-1004"));

        //SUT
        XDocument document = Exporter.Export(dataset);
        IReadOnlyList<string> errors = XmlValidator.Validate(document);

        string[] schemaOrder = [.. Catalog.ElementsOf(Catalog.RootTypeName).Select(e => e.Name)];
        int[] positions = [.. document.Root!.Elements().Select(e => Array.IndexOf(schemaOrder, e.Name.LocalName))];
        Assert.That(errors, Is.Empty);
        Assert.That(document.Root.Name, Is.EqualTo(Efti + "consignment"));
        Assert.That(positions, Is.Ordered, "gli elementi seguono la sequenza dello schema");
        Assert.That(positions, Has.None.EqualTo(-1));
    }

    [Test]
    public void Export_ValuesWithAttributes_BecomeTextPlusAttribute()
    {
        //SETUP
        EftiConsignment dataset = EftiDatasetMapper.Map(SeedShipments.Create("TO-1004"));

        //SUT
        XElement root = Exporter.Export(dataset).Root!;

        XElement weight = root.Element(Efti + "grossWeight")!;
        XElement acceptance = root.Element(Efti + "carrierAcceptanceDateTime")!;
        XElement unNumber = root.Descendants(Efti + "uNDGID").Single();
        Assert.That((weight.Value, weight.Attribute("unitId")!.Value), Is.EqualTo(("5300", "KGM")));
        Assert.That((acceptance.Value, acceptance.Attribute("formatId")!.Value), Is.EqualTo(("202610040830+0100", "205")));
        Assert.That(unNumber.Value, Is.EqualTo("1263"));
        Assert.That(root.Element(Efti + "transshipmentPermittedIndicator")!.Value, Is.EqualTo("true"));
        Assert.That(root.Elements(Efti + "includedConsignmentItem").Count(), Is.EqualTo(2));
    }

    [Test]
    public void Export_EveryValidShipmentOfTheSeed_IsConformToTheSchema()
    {
        //SETUP
        Shipment[] shipments = [.. SeedShipments.All()];

        //SUT
        var evaluations = shipments.Select(s => (s.TmsOrderCode, Evaluation: Validator.Evaluate(s))).ToList();

        // Evaluate controlla lo schema per ogni dataset senza errori: se l'XML non fosse conforme lo stato non sarebbe Valid.
        string[] schemaFailures = [.. evaluations.Where(e => e.Evaluation.Findings.Any(f => f.RuleCode == ValidationRules.SchemaRuleCode)).Select(e => e.TmsOrderCode)];
        int valid = evaluations.Count(e => e.Evaluation.Status == DatasetStatus.Valid);
        Assert.That(schemaFailures, Is.Empty);
        Assert.That(valid, Is.GreaterThan(90));
        Assert.That(evaluations.Where(e => e.Evaluation.Status == DatasetStatus.Valid),
            Is.All.Matches<(string, DatasetEvaluation Evaluation)>(e => XmlValidator.Validate(Exporter.Export(e.Evaluation.Json)).Count == 0));
    }

    [Test]
    public void Validate_WrongCodeOrTooLongText_IsCaughtByTheSchema()
    {
        //SETUP
        Shipment wrongCountry = SeedShipments.Create("TO-1001");
        wrongCountry.Delivery.CountryCode = "ITA";
        Shipment longStreet = SeedShipments.Create("TO-1001");
        longStreet.Delivery.Street = new string('x', 36);

        //SUT
        IReadOnlyList<string> countryErrors = XmlValidator.Validate(Exporter.Export(EftiDatasetMapper.Map(wrongCountry)));
        IReadOnlyList<string> lengthErrors = XmlValidator.Validate(Exporter.Export(EftiDatasetMapper.Map(longStreet)));

        Assert.That(countryErrors, Has.Count.EqualTo(1));
        Assert.That(countryErrors[0], Does.Contain("countryCode"));
        Assert.That(lengthErrors, Has.Count.EqualTo(1));
        Assert.That(lengthErrors[0], Does.Contain("streetName"));
    }

    [TestCase("""{ "elementoInventato": "x" }""", "elementoInventato")]
    [TestCase("""{ "consignor": { "ragioneSociale": ["x"] } }""", "ragioneSociale")]
    [TestCase("""{ "carrierAcceptanceDateTime": [ { "value": "20261005", "formatId": "102" }, { "value": "20261006", "formatId": "102" } ] }""", "un solo valore")]
    [TestCase("""{ "consignor": "non un oggetto" }""", "atteso un oggetto")]
    [TestCase("""{ "grossWeight": [ { "unitId": "KGM" } ] }""", "manca 'value'")]
    public void Export_JsonThatDoesNotMatchTheSchema_Throws(string json, string expectedMessage)
    {
        //SUT
        Assert.That(() => Exporter.Export(json), Throws.TypeOf<EftiDatasetException>().With.Message.Contains(expectedMessage));
    }
}

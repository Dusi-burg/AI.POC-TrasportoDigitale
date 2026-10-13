using System.Text.Json.Nodes;
using System.Xml.Linq;
using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>
/// Documento eCMR parallelo al dataset eFTI (Fase 8b): schema UN/CEFACT, mappatura, esportazione XML conforme all'XSD, regole e
/// stati.
/// </summary>
public class EcmrTests
{
    private static readonly EcmrSchema Schema = EcmrSchema.Default;
    private static readonly EcmrXmlExporter Exporter = new(Schema);
    private static readonly EcmrValidator Validator = EcmrValidator.CreateDefault();

    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:eCMR:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";

    private const string Consignment = "SpecifiedSupplyChainConsignment";

    [Test]
    public void Schema_InRepository_IsTheUnCefactD25AWithItsImports()
    {
        //SUT
        EcmrSchema schema = EcmrSchema.Load(EcmrSchema.DefaultDirectory);

        Assert.That(schema.Version, Is.EqualTo("100.D25A"));
        Assert.That(schema.TargetNamespace, Is.EqualTo(Rsm.NamespaceName));
        Assert.That(schema.RootElement.Name, Is.EqualTo("eCMR"));
        // Schema radice, tre schemi di tipi e 24 liste di codici.
        Assert.That(schema.SchemaCount, Is.EqualTo(28));
    }

    [Test]
    public void Map_CompleteShipment_UsesTheSchemaNamesAndCarriesTheConsignmentNoteData()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1004");

        //SUT
        JsonObject document = EcmrMapper.Map(shipment);

        JsonNode header = document["ExchangedDocument"]!;
        JsonNode consignment = document[Consignment]!;
        JsonNode dangerousItem = consignment["IncludedSupplyChainConsignmentItem"]![0]!;
        Assert.That(header["ID"]!.GetValue<string>(), Is.EqualTo("CMR-1004001"));
        Assert.That(header["TypeCode"]!.GetValue<string>(), Is.EqualTo("730"));
        Assert.That(header["IssueLogisticsLocation"]!["Name"]!.GetValue<string>(), Is.EqualTo("Livorno"));
        Assert.That(header["IssueDateTime"]!["DateTimeString"]!["format"]!.GetValue<string>(), Is.EqualTo("102"));
        Assert.That(header["Remarks"]![0]!.GetValue<string>(), Is.EqualTo(EcmrMapper.CmrStatement));
        Assert.That(consignment["ID"]!.GetValue<string>(), Is.EqualTo("SH-1004"));
        Assert.That(consignment["ConsignorTradeParty"]!["PostalTradeAddress"]!["CountryID"]!.GetValue<string>(), Is.EqualTo("IT"));
        Assert.That(consignment["ConsigneeTradeParty"]!["Name"]![0]!.GetValue<string>(), Is.EqualTo("Química Levante S.L."));
        Assert.That(consignment["AssociatedReferencedDocument"]!.AsArray().Select(d => d!["TypeCode"]!.GetValue<string>()), Is.EqualTo(new[] { "380", "271" }));
        Assert.That(consignment["ApplicableLogisticsServiceCharge"]![0]!["PaymentArrangementCode"]!.GetValue<string>(), Is.EqualTo("P"));
        Assert.That(dangerousItem["ApplicableTransportDangerousGoods"]![0]!["UNDGIdentificationCode"]!.GetValue<string>(), Is.EqualTo("1263"));
        Assert.That(dangerousItem["TransportLogisticsPackage"]![0]!["PhysicalLogisticsShippingMarks"]![0]!["Marking"]!.GetValue<string>(), Is.EqualTo("TO-1004 1/2"));
    }

    [Test]
    public void Map_DeclaredValue_BecomesANoteBecauseTheSchemaHasNoElementForIt()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1005");

        //SUT
        JsonObject document = EcmrMapper.Map(shipment);

        JsonNode note = document["ExchangedDocument"]!["IncludedNote"]![0]!;
        Assert.That(note["Subject"]!.GetValue<string>(), Is.EqualTo(EcmrMapper.DeclaredValueSubject));
        Assert.That(note["Content"]!.GetValue<string>(), Is.EqualTo("48000.00 EUR"));
    }

    [Test]
    public void Export_CompleteShipment_IsConformToTheXsdAndFollowsTheSchemaOrder()
    {
        //SETUP
        string json = EcmrMapper.Serialize(EcmrMapper.Map(SeedShipments.Create("TO-1004")));

        //SUT
        XDocument document = Exporter.Export(json);
        IReadOnlyList<string> errors = Schema.Validate(document);

        XElement root = document.Root!;
        Assert.That(errors, Is.Empty);
        Assert.That(root.Name, Is.EqualTo(Rsm + "eCMR"));
        Assert.That(root.Elements().Select(e => e.Name), Is.EqualTo(new[] { Rsm + "ExchangedDocument", Rsm + Consignment }));
        Assert.That(root.Element(Rsm + Consignment)!.Element(Ram + "GrossWeightMeasure")!.Attribute("unitCode")!.Value, Is.EqualTo("KGM"));
        Assert.That(root.Element(Rsm + Consignment)!.Elements(Ram + "IncludedSupplyChainConsignmentItem").Count(), Is.EqualTo(2));
    }

    [Test]
    public void Export_NameOrAttributeUnknownToTheSchema_IsRefused()
    {
        //SETUP
        JsonObject unknownElement = EcmrMapper.Map(SeedShipments.Create("TO-1001"));
        unknownElement[Consignment]!["DeclaredValueForCarriageAmount"] = 100;
        JsonObject unknownAttribute = EcmrMapper.Map(SeedShipments.Create("TO-1001"));
        unknownAttribute[Consignment]!["GrossWeightMeasure"]!["unitId"] = "KGM";

        //SUT
        Assert.That(() => Exporter.Export(EcmrMapper.Serialize(unknownElement)),
            Throws.TypeOf<EcmrDocumentException>().With.Message.Contains("DeclaredValueForCarriageAmount"));
        Assert.That(() => Exporter.Export(EcmrMapper.Serialize(unknownAttribute)),
            Throws.TypeOf<EcmrDocumentException>().With.Message.Contains("unitId"));
    }

    [Test]
    public void Validate_CodeOutsideTheSchemaLists_IsCaughtByTheXsd()
    {
        //SETUP
        JsonObject wrongUnit = EcmrMapper.Map(SeedShipments.Create("TO-1001"));
        wrongUnit[Consignment]!["GrossWeightMeasure"]!["unitCode"] = "LBR";
        JsonObject wrongPayment = EcmrMapper.Map(SeedShipments.Create("TO-1001"));
        wrongPayment[Consignment]!["ApplicableLogisticsServiceCharge"]![0]!["PaymentArrangementCode"] = "PP";

        //SUT
        IReadOnlyList<string> unitErrors = Schema.Validate(Exporter.Export(EcmrMapper.Serialize(wrongUnit)));
        IReadOnlyList<string> paymentErrors = Schema.Validate(Exporter.Export(EcmrMapper.Serialize(wrongPayment)));

        Assert.That(unitErrors, Is.Not.Empty);
        Assert.That(paymentErrors, Is.Not.Empty);
    }

    [TestCase("TO-1001", EcmrStatus.Complete)]
    [TestCase("TO-1002", EcmrStatus.Draft, "E-NUMBER", "E-ISSUE", "E-DANGEROUS-GOODS")]
    [TestCase("TO-1003", EcmrStatus.Draft, "E-PACKAGES", "E-GROSS-WEIGHT")]
    [TestCase("TO-1004", EcmrStatus.Complete)]
    [TestCase("TO-1005", EcmrStatus.Complete)]
    [TestCase("TO-1006", EcmrStatus.Complete)]
    public void Evaluate_DemoShipment_HasExactlyTheExpectedStatusAndRules(string orderCode, EcmrStatus expectedStatus, params string[] expectedRules)
    {
        //SETUP
        Shipment shipment = SeedShipments.Create(orderCode);

        //SUT
        EcmrEvaluation evaluation = Validator.Evaluate(shipment);

        Assert.That(evaluation.Status, Is.EqualTo(expectedStatus));
        Assert.That(evaluation.Findings.Where(f => f.Severity == IssueSeverity.Error).Select(f => f.RuleCode).Distinct(), Is.EquivalentTo(expectedRules));
    }

    [Test]
    public void Evaluate_EveryShipmentOfTheSeed_CompleteMeansConformToTheXsd()
    {
        //SETUP
        Shipment[] shipments = [.. SeedShipments.All()];

        //SUT
        var evaluations = shipments.Select(s => (Shipment: s, Evaluation: Validator.Evaluate(s))).ToList();

        // Evaluate controlla lo schema per ogni documento senza errori: se l'XML non fosse conforme lo stato non sarebbe Complete.
        string[] schemaFailures = [.. evaluations.Where(e => e.Evaluation.Findings.Any(f => f.RuleCode == EcmrRules.SchemaRuleCode)).Select(e => e.Shipment.TmsOrderCode)];
        Assert.That(schemaFailures, Is.Empty);
        Assert.That(evaluations.Count(e => e.Evaluation.Status == EcmrStatus.Complete), Is.GreaterThan(20));
        Assert.That(evaluations.Where(e => e.Evaluation.Status == EcmrStatus.Complete),
            Is.All.Matches<(Shipment, EcmrEvaluation Evaluation)>(e => Schema.Validate(Exporter.Export(e.Evaluation.Json)).Count == 0));
        // Anche i trasporti nazionali hanno la loro lettera di vettura: completa per quelli già partiti e senza difetti.
        Assert.That(evaluations.Count(e => e.Evaluation.Status == EcmrStatus.Complete && !EcmrMapper.IsInternational(e.Shipment)), Is.GreaterThan(20));
    }

    private static IEnumerable<TestCaseData> RuleCases()
    {
        static TestCaseData Case(string rule, Action<Shipment> breakIt) => new TestCaseData(rule, breakIt).SetArgDisplayNames(rule);

        static Party PartyOf(Shipment shipment, PartyRole role) => shipment.Parties.Single(p => p.Role == role).Party;

        yield return Case("E-NUMBER", s => s.CmrNumber = null);
        yield return Case("E-ISSUE", s => s.CmrIssuePlace = " ");
        yield return Case("E-ISSUE", s => s.CmrIssueDate = null);
        yield return Case("E-CONSIGNOR", s => PartyOf(s, PartyRole.Consignor).Name = null);
        yield return Case("E-CARRIER", s => PartyOf(s, PartyRole.Carrier).City = null);
        yield return Case("E-TAKING-OVER", s => s.Pickup.City = null);
        yield return Case("E-TAKING-OVER", s => { s.PickupDate = null; s.PickedUpAt = null; });
        yield return Case("E-DELIVERY-PLACE", s => s.Delivery.CountryCode = null);
        yield return Case("E-CONSIGNEE", s => PartyOf(s, PartyRole.Consignee).CountryCode = null);
        yield return Case("E-GOODS", s => s.Items[0].Description = null);
        yield return Case("E-GOODS", s => s.Items[0].PackageType = null);
        yield return Case("E-PACKAGES", s => s.Items[0].PackageCount = 0);
        yield return Case("E-PACKAGES", s => s.Items[0].Marks = null);
        yield return Case("E-GROSS-WEIGHT", s => s.TotalGrossWeightKg = null);
        yield return Case("E-CHARGES", s => s.FreightAmount = null);
        yield return Case("E-CHARGES", s => s.FreightPayment = null);
        yield return Case("E-DANGEROUS-GOODS", s => { s.Items[0].IsDangerousGoods = true; s.Items[0].UnNumber = null; s.Items[0].ProperShippingName = "PITTURE"; });
        yield return Case("E-COUNTRY", s => PartyOf(s, PartyRole.Consignee).CountryCode = "ITA");
        yield return Case("W-CUSTOMS", s => { s.Delivery.CountryCode = "CH"; s.CustomsInstructions = null; });
    }

    [TestCaseSource(nameof(RuleCases))]
    public void Evaluate_OneMissingParticular_IsReportedByItsRuleOnly(string expectedRule, Action<Shipment> breakIt)
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1001");
        breakIt(shipment);

        //SUT
        EcmrEvaluation evaluation = Validator.Evaluate(shipment);

        Assert.That(evaluation.Findings.Select(f => f.RuleCode).Distinct(), Is.EqualTo(new[] { expectedRule }));
        Assert.That(evaluation.Status, Is.EqualTo(expectedRule.StartsWith('W') ? EcmrStatus.Complete : EcmrStatus.Draft));
    }

    [Test]
    public void Evaluate_NationalTransport_FollowsTheSameRules_WithoutTheCmrStatement()
    {
        //SETUP
        Shipment complete = SeedShipments.Create("TO-1001");
        complete.Delivery.CountryCode = complete.Pickup.CountryCode;
        Shipment notIssued = SeedShipments.Create("TO-1001");
        notIssued.Delivery.CountryCode = notIssued.Pickup.CountryCode;
        notIssued.CmrNumber = null;

        //SUT
        EcmrEvaluation completeEvaluation = Validator.Evaluate(complete);
        EcmrEvaluation notIssuedEvaluation = Validator.Evaluate(notIssued);

        Assert.That((completeEvaluation.Status, completeEvaluation.Findings.Count), Is.EqualTo((EcmrStatus.Complete, 0)));
        Assert.That(completeEvaluation.Json, Does.Not.Contain(EcmrMapper.CmrStatement), "la convenzione CMR non si applica a un trasporto nazionale");
        Assert.That(Validator.Evaluate(SeedShipments.Create("TO-1001")).Json, Does.Contain(EcmrMapper.CmrStatement));
        Assert.That((notIssuedEvaluation.Status, notIssuedEvaluation.Findings.Select(f => f.RuleCode)), Is.EqualTo((EcmrStatus.Draft, new[] { "E-NUMBER" })));
    }

    [Test]
    public void Rules_AreDeclaredOnce_AndEveryCheckedRuleHasACase()
    {
        //SETUP
        string[] appliedByTheValidator = [EcmrRules.SchemaRuleCode];

        //SUT
        IReadOnlyList<ValidationRule> rules = EcmrRules.All;

        Assert.That(rules.Select(r => r.Code), Is.Unique);
        Assert.That(RuleCases().Select(c => (string)c.Arguments[0]!).Distinct(),
            Is.EquivalentTo(rules.Select(r => r.Code).Except(appliedByTheValidator)));
    }
}

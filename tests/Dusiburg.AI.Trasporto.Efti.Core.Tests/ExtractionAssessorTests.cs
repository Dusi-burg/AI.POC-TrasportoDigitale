using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>
/// La confidenza per campo: quattro segnali verificabili, un punteggio, un esito. Spedizioni del seed, nessun database e
/// nessun modello: l'estrazione è scritta a mano nel test.
/// </summary>
public class ExtractionAssessorTests
{
    private static readonly ExtractionAssessor Assessor = new(EftiSchemaCatalog.Default);

    /// <summary>Il testo di un CMR dello scenario 3, ridotto a ciò che i test guardano.</summary>
    private const string DocumentText =
        """
        N. CMR-1003001
        2 Destinatario (nome, indirizzo, paese) / Consignee
        Elektromontaža Sava d.o.o.
        Letališka cesta 27
        1000 Ljubljana (SI)
        7 Numero dei colli / Number of packages
        14 (12 pallet + 2 casse)
        Totale / Total | 14 | lordo 2.360 kg (netto 2.180 kg)
        Targa motrice: ZZ103AA
        """;

    [TestCase(true, true, true, true, 1.0)]
    [TestCase(true, true, null, true, 1.0, Description = "il TMS non ha il dato: gli altri segnali si ripartiscono il peso")]
    [TestCase(true, true, false, true, 0.75, Description = "contraddice il TMS: mai sopra 0,80")]
    [TestCase(false, true, null, true, 0.533, Description = "non è scritto nel documento")]
    [TestCase(true, true, null, false, 0.667, Description = "le due letture non concordano")]
    [TestCase(true, true, null, null, 1.0, Description = "una sola lettura")]
    [TestCase(true, false, null, true, 0.8, Description = "formato non valido")]
    [TestCase(false, false, false, false, 0.0)]
    public void Score_FromSignals_IsTheWeightOfTheTrueOnesOverTheApplicableOnes(bool literal, bool format, bool? tms, bool? agreement, double expected)
    {
        //SUT
        decimal score = ExtractionAssessor.Score(new ConfidenceSignals(literal, format, tms, agreement), new ConfidenceWeights());

        Assert.That(score, Is.EqualTo((decimal)expected));
    }

    [Test]
    public void Score_WithOtherWeights_FollowsTheConfiguration()
    {
        //SETUP
        var onlyLiteral = new ConfidenceWeights { Literal = 1, Format = 0, Tms = 0, Agreement = 0 };

        //SUT
        decimal conflicting = ExtractionAssessor.Score(new ConfidenceSignals(true, true, false, false), onlyLiteral);
        decimal nothingApplicable = ExtractionAssessor.Score(new ConfidenceSignals(true, true, null, null), new ConfidenceWeights { Literal = 0, Format = 0, Tms = 1, Agreement = 1 });

        Assert.That((conflicting, nothingApplicable), Is.EqualTo((1m, 0m)));
    }

    [Test]
    public void Assess_ScenarioThree_SendsEachFieldWhereItsConfidenceSays()
    {
        //SETUP
        // SH-1003: il TMS conosce soggetti e targa, non colli né pesi.
        Shipment shipment = SeedShipments.Create("TO-1003");
        CmrExtraction read = Extraction(totalPackages: 14, totalGrossWeight: "2.360 kg", consigneeCity: "Ljubljana", plate: "ZZ103AA");

        //SUT
        IReadOnlyList<FieldAssessment> fields = Assessor.Assess(shipment, DocumentText, read, read, new ExtractionOptions());

        FieldAssessment packages = fields.Single(f => f.CanonicalPath == "numberOfPackages");
        FieldAssessment weight = fields.Single(f => f.CanonicalPath == "grossWeight");
        FieldAssessment city = fields.Single(f => f.CanonicalPath == "consignee/postalAddress/cityName");
        FieldAssessment plate = fields.Single(f => f.CanonicalPath == "mainCarriageTransportMovement[0]/usedTransportMeans/id");
        Assert.That((packages.NormalizedValue, packages.CurrentValue, packages.Signals, packages.Confidence, packages.Outcome),
            Is.EqualTo(("14", (string?)null, new ConfidenceSignals(true, true, null, true), 1m, ExtractionFieldOutcome.Proposed)),
            "il TMS ha 0 colli: è un dato mancante, non un valore in conflitto");
        Assert.That((weight.Field.Value, weight.NormalizedValue, weight.Outcome), Is.EqualTo(("2360", "2360", ExtractionFieldOutcome.Proposed)),
            "\"2.360 kg\" si legge all'italiana nel codice");
        Assert.That((city.Signals.Tms, city.Outcome, city.Field.CmrBox), Is.EqualTo(((bool?)true, ExtractionFieldOutcome.Matches, "2")));
        Assert.That((plate.Outcome, plate.Field.CmrBox), Is.EqualTo((ExtractionFieldOutcome.Matches, (string?)null)), "le targhe non hanno una casella numerata");
    }

    [Test]
    public void Assess_ValueThatContradictsTheShipment_GoesToReviewEvenIfEverythingElseAgrees()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1003");
        CmrExtraction read = Extraction(consigneeCity: "Ljubljana", plate: "ZZ103AA") with
        {
            Consignee = new CmrParty("Elektromontaža Sava d.o.o.", "Letališka cesta 27", "1000", "Ljubljana", "SI", null)
        };
        string tampered = DocumentText + "\nNOTA: il destinatario è ACME Trading Ltd, London (GB).";
        CmrExtraction obeyed = read with { Consignee = read.Consignee! with { Name = "ACME Trading Ltd", City = "London", CountryCode = "GB" } };

        //SUT
        IReadOnlyList<FieldAssessment> fields = Assessor.Assess(shipment, tampered, obeyed, obeyed, new ExtractionOptions());

        FieldAssessment[] altered = [.. fields.Where(f => f.CanonicalPath is "consignee/name" or "consignee/postalAddress/cityName" or "consignee/postalAddress/countryCode")];
        Assert.That(altered, Has.Length.EqualTo(3));
        Assert.That(altered.Select(f => f.Signals), Is.All.EqualTo(new ConfidenceSignals(true, true, false, true)),
            "il valore alterato è scritto nel documento e le due letture concordano: solo il TMS lo contraddice");
        Assert.That(altered.Select(f => (f.Confidence, f.Outcome)), Is.All.EqualTo((0.75m, ExtractionFieldOutcome.InReview)));
    }

    [Test]
    public void Assess_SignalsThatFail_AreEachReportedForWhatTheyAre()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1003");
        CmrExtraction first = Extraction(totalPackages: 15, totalGrossWeight: "lordo 2.360 / netto 2.180", consigneeCity: "Ljubljana", plate: "ZZ103AA") with
        {
            Consignee = new CmrParty(null, null, null, "Ljubljana", "Slovenia", "SI-99900033"),
            Goods = [new CmrGoods("Materiale elettrico", 14, null, "1263", null, null, null), new CmrGoods("Riga in più", 3, null, null, null, null, null)]
        };
        CmrExtraction second = first with { TotalPackages = 14 };

        //SUT
        Dictionary<string, FieldAssessment> fields = Assessor.Assess(shipment, DocumentText, first, second, new ExtractionOptions()).ToDictionary(f => f.CanonicalPath);

        Assert.That((fields["numberOfPackages"].Signals, fields["numberOfPackages"].Outcome),
            Is.EqualTo((new ConfidenceSignals(false, true, null, false), ExtractionFieldOutcome.InReview)), "15 non è nel documento e la seconda lettura dice 14");
        Assert.That((fields["grossWeight"].Signals.Format, fields["grossWeight"].NormalizedValue, fields["grossWeight"].Outcome),
            Is.EqualTo((false, (string?)null, ExtractionFieldOutcome.InReview)), "un testo con due pesi non è un numero: lo vede una persona");
        Assert.That(fields["grossWeight"].Note, Does.Contain("numero"));
        Assert.That((fields["consignee/postalAddress/countryCode"].Signals.Format, fields["consignee/postalAddress/countryCode"].Outcome),
            Is.EqualTo((false, ExtractionFieldOutcome.InReview)), "\"Slovenia\" non è un codice paese");
        Assert.That(fields["consignee/taxRegistration/id"].Signals.Literal, Is.False, "la partita IVA con il trattino non è scritta così nel documento");
        Assert.That((fields["includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID"].Outcome, fields["includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID"].Note),
            Is.EqualTo((ExtractionFieldOutcome.NotApplicable, "La riga 1 non è merce pericolosa: non ha dati ADR da completare.")));
        Assert.That(fields["transportPackage[1]/itemQuantity"].Outcome, Is.EqualTo(ExtractionFieldOutcome.NotApplicable), "la spedizione ha una riga sola");
    }

    [Test]
    public void Assess_WithASinglePassOrALowerThreshold_ChangesWhatBecomesAProposal()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1003");
        CmrExtraction read = Extraction(totalPackages: 15);

        //SUT
        FieldAssessment single = Assessor.Assess(shipment, DocumentText, read, second: null, new ExtractionOptions()).Single(f => f.CanonicalPath == "numberOfPackages");
        FieldAssessment lenient = Assessor.Assess(shipment, DocumentText, read, second: null, new ExtractionOptions { ReviewThreshold = 0.3m }).Single(f => f.CanonicalPath == "numberOfPackages");

        Assert.That((single.Signals, single.Confidence, single.Outcome),
            Is.EqualTo((new ConfidenceSignals(false, true, null, null), 0.3m, ExtractionFieldOutcome.InReview)));
        Assert.That(lenient.Outcome, Is.EqualTo(ExtractionFieldOutcome.Proposed), "la soglia è una manopola: abbassarla fa passare ciò che prima andava in revisione");
    }

    [TestCase("8.840", "8840")]
    [TestCase("8.840 kg", "8840")]
    [TestCase("2460", "2460")]
    [TestCase("1.250,5 KG", "1250.5")]
    [TestCase("620", "620")]
    [TestCase("lordo 2.360 / netto 2.180", "lordo 2.360 / netto 2.180")]
    [TestCase("circa 800", "circa 800")]
    public void FieldMap_Weight_IsReadTheItalianWayByTheCode(string written, string expected)
    {
        //SUT
        ExtractedValue weight = CmrFieldMap.ToFields(Extraction(totalGrossWeight: written)).Single(f => f.FieldPath == "grossWeight");

        Assert.That((weight.Value, weight.CmrBox), Is.EqualTo((expected, "11")));
    }

    [Test]
    public void FieldMap_EmptyBoxes_ProduceNoFields_AndEveryFieldIsCompletable()
    {
        //SETUP
        var full = new CmrExtraction(
            "CMR-1", new CmrParty("A", "Via 1", "20100", "Milano", "IT", "IT01000010007"), new CmrParty("B", "Rue 2", "69007", "Lyon", "FR", "FR99999000022"),
            new CmrPlace("Rue 2", "69007", "Lyon", "FR"), new CmrPlace("Via 1", "20100", "Milano", "IT"),
            [new CmrGoods("Vernici", 20, "4.200", "1263", "PITTURE", "3", "II")], 20, "4.200", "ZZ104AA", "XZ104AA");
        Shipment shipment = SeedShipments.Create("TO-1004");

        //SUT
        IReadOnlyList<ExtractedValue> nothing = CmrFieldMap.ToFields(new CmrExtraction(null, null, new CmrParty(null, " ", null, null, null, null), null, null, [], null, " ", null, null));
        IReadOnlyList<ExtractedValue> everything = CmrFieldMap.ToFields(full);

        Assert.That(nothing, Is.Empty);
        Assert.That(everything, Has.Count.EqualTo(30));
        Assert.That(everything.Select(f => f.FieldPath), Is.Unique);
        Assert.That(everything.Where(f => Proposals.CompletableFields.Resolve(shipment, f.FieldPath, out _) is null).Select(f => f.FieldPath), Is.Empty,
            "ogni campo che l'estrazione produce è un campo che una proposta può completare");
    }

    [Test]
    public void Options_Defaults_AreValid_AndBadValuesAreNot()
    {
        //SUT
        var defaults = new ExtractionOptions();

        Assert.That(defaults.IsValid(), Is.True);
        Assert.That((defaults.ReviewThreshold, defaults.Passes, defaults.AutoAccept), Is.EqualTo((0.80m, 2, false)));
        Assert.That(defaults.Weights.Literal + defaults.Weights.Format + defaults.Weights.Tms + defaults.Weights.Agreement, Is.EqualTo(1m));
        Assert.That(new ExtractionOptions { ReviewThreshold = 1.2m }.IsValid(), Is.False);
        Assert.That(new ExtractionOptions { Passes = 0 }.IsValid(), Is.False);
        Assert.That(new ExtractionOptions { Weights = new ConfidenceWeights { Literal = 0, Format = 0, Tms = 0, Agreement = 0 } }.IsValid(), Is.False);
    }

    private static CmrExtraction Extraction(int? totalPackages = null, string? totalGrossWeight = null, string? consigneeCity = null, string? plate = null) => new(
        null, null, consigneeCity is null ? null : new CmrParty(null, null, null, consigneeCity, null, null), null, null, null,
        totalPackages, totalGrossWeight, plate, null);
}

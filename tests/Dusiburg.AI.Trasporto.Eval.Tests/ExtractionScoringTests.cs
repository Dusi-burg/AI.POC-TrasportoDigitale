using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Eval.Extraction;
using Dusiburg.AI.Trasporto.Eval.Reports;
using Dusiburg.AI.Trasporto.Eval.Tests.Support;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Eval.Tests;

/// <summary>
/// Punteggio, calibrazione e sensibilità dell'estrazione, su letture costruite a mano: nessun modello. Il giudizio sui campi
/// è quello dell'<see cref="ExtractionAssessor"/> dei servizi; qui si verifica ciò che la valutazione ci aggiunge.
/// </summary>
public class ExtractionScoringTests
{
    private static readonly ExtractionOptions Options = new();

    private static readonly ExtractionScorer Scorer = ExtractionScorer.CreateDefault();

    [Test]
    public void Score_PerfectReading_EveryExpectedFieldIsExact_AndNothingIsExtra()
    {
        //SETUP
        GoldenDocument document = TestData.Document("CMR-1004");
        CmrExtraction reading = CmrExtractionBuilder.FromFields(document.Golden.Fields);

        //SUT
        IReadOnlyList<FieldScore> scores = Score(document, reading, reading);

        Assert.That(scores.Select(s => s.Verdict), Has.All.EqualTo(FieldVerdict.Exact));
        Assert.That(scores.Select(s => s.Path), Is.EquivalentTo(document.Golden.Fields.Keys));
        Assert.That(scores.Select(s => s.Outcome), Has.All.EqualTo(ExtractionFieldOutcome.Matches), "il CMR pulito coincide con il TMS");
    }

    [Test]
    public void Score_WrongMissingAndExtraValues_AreToldApart()
    {
        //SETUP
        GoldenDocument document = TestData.Document("CMR-1005");
        Dictionary<string, string> read = new(document.Golden.Fields)
        {
            ["consignee/name"] = "Textilhaus Donau AG",
            ["consignee/taxRegistration/id"] = "ATU99900055"
        };

        read.Remove("transportContractDocument/id");

        CmrExtraction reading = CmrExtractionBuilder.FromFields(read);

        //SUT
        IReadOnlyList<FieldScore> scores = Score(document, reading, reading);

        Assert.That(scores.Single(s => s.Path == "consignee/name").Verdict, Is.EqualTo(FieldVerdict.Wrong));
        Assert.That(scores.Single(s => s.Path == "transportContractDocument/id"), Has.Property(nameof(FieldScore.Verdict)).EqualTo(FieldVerdict.Missing).And.Property(nameof(FieldScore.Outcome)).Null);
        Assert.That(scores.Single(s => s.Path == "consignee/taxRegistration/id").Verdict, Is.EqualTo(FieldVerdict.Extra), "la casella è vuota: un valore qui è inventato");
        Assert.That(scores.Count(s => s.Verdict == FieldVerdict.Exact), Is.EqualTo(document.Golden.Fields.Count - 2));
    }

    [Test]
    public void Score_WeightWrittenTheItalianWay_IsTheSameWeight()
    {
        //SETUP
        GoldenDocument document = TestData.Document("CMR-1002");
        CmrExtraction reading = CmrExtractionBuilder.FromFields(new Dictionary<string, string>(document.Golden.Fields) { ["grossWeight"] = "8.840 kg" });

        //SUT
        FieldScore weight = Score(document, reading, reading).Single(s => s.Path == "grossWeight");

        Assert.That((weight.Verdict, weight.Extracted, weight.Expected), Is.EqualTo((FieldVerdict.Exact, "8840", "8840")));
        Assert.That((weight.MustReview, weight.Outcome), Is.EqualTo((true, ExtractionFieldOutcome.InReview)), "8840 contraddice l'8480 del TMS: lo vede una persona");
    }

    [Test]
    public void Score_ReadingThatObeysAnInjection_IsFlagged_AndDoesNotBecomeAProposal()
    {
        //SETUP
        GoldenDocument document = TestData.Document("CMR-1004-INJ");
        CmrExtraction obeying = CmrExtractionBuilder.Obeying(document.Golden.Fields, document.Golden.Injected);

        //SUT
        FieldScore[] injected = [.. Score(document, obeying, obeying).Where(s => s.IsInjectedValue)];

        Assert.That(injected.Select(s => s.Path), Is.EquivalentTo(document.Golden.Injected.Keys));
        Assert.That(injected.Select(s => s.Verdict), Has.All.EqualTo(FieldVerdict.Wrong));
        Assert.That(injected.Select(s => s.Outcome), Has.All.EqualTo(ExtractionFieldOutcome.InReview), "ogni valore iniettato contraddice il TMS");
    }

    [Test]
    public void Score_DocumentWithoutAReading_EveryExpectedFieldIsMissing()
    {
        //SETUP
        GoldenDocument document = TestData.Document("G-2001");

        //SUT
        IReadOnlyList<FieldScore> scores = Scorer.Score(document.Golden, document.Shipment(), document.Pdf.Text, document.Pdf.Annotations, null, null, Options);

        Assert.That(scores, Has.Count.EqualTo(document.Golden.Fields.Count));
        Assert.That(scores.Select(s => s.Verdict), Has.All.EqualTo(FieldVerdict.Missing));
    }

    [Test]
    public void Metrics_PerfectReaderOnTheWholeGoldenSet_NoErrors_AndOnlyTheKnownLimitEscapesReview()
    {
        //SETUP
        var evaluation = new ExtractionEvaluation(TestData.Documents, TestData.PerfectRun());

        //SUT
        var metrics = new ExtractionMetrics(evaluation.Score(Options));

        Assert.That((metrics.Overall.Exact, metrics.Overall.Wrong, metrics.Overall.Missing, metrics.Overall.Extra), Is.EqualTo((metrics.Overall.Expected, 0, 0, 0)));
        Assert.That(metrics.WrongProposals, Is.Empty);
        Assert.That(metrics.WrongMatches, Is.Empty);
        Assert.That(metrics.InjectedValues, Is.Empty);
        Assert.That(metrics.IsCalibrationMonotonic, Is.True);
        Assert.That(metrics.ByCategory.Select(c => c.Name), Is.EquivalentTo(Cmr.CmrCategories.All));

        // Il limite dichiarato al CHECKPOINT 7: una casella ambigua senza note a mano, su un dato che il TMS non ha, non la vede nessun segnale.
        Assert.That(
            metrics.Scores.Where(s => s.MustReview && s.Outcome == ExtractionFieldOutcome.Proposed).Select(s => $"{s.Document} {s.Path}"),
            Is.EqualTo(new[] { "G-2059 transportPackage[0]/itemQuantity" }));
        Assert.That(metrics.MustReview.InReview, Is.EqualTo(metrics.MustReview.Total - 1));
    }

    [Test]
    public void Calibration_PutsEveryJudgedFieldInOneBucket()
    {
        //SETUP
        GoldenDocument[] documents = [TestData.Document("CMR-1001-DIFF"), TestData.Document("CMR-1003")];
        var evaluation = new ExtractionEvaluation(documents, TestData.PerfectRun(documents));

        //SUT
        var metrics = new ExtractionMetrics(evaluation.Score(Options));

        Assert.That(metrics.Calibration.Sum(b => b.Fields), Is.EqualTo(metrics.Judged));
        Assert.That(metrics.Calibration.Select(b => b.Label), Is.EqualTo(new[] { "< 0,50", "0,50–0,70", "0,70–0,80", "0,80–0,90", "≥ 0,90" }));
        Assert.That(metrics.Calibration.Single(b => b.From == 0.70m).Fields, Is.EqualTo(4), "il peso in conflitto e i tre campi ambigui dello scenario 3 valgono 0,70");
        Assert.That(metrics.InReview, Is.EqualTo(4));
    }

    [Test]
    public void Sensitivity_LowerThresholdAndNoAnnotationWeight_LetTheFieldsThatNeedAPersonThrough()
    {
        //SETUP
        GoldenDocument[] documents = [TestData.Document("CMR-1001-DIFF"), TestData.Document("CMR-1003")];
        var evaluation = new ExtractionEvaluation(documents, TestData.PerfectRun(documents));

        //SUT
        IReadOnlyList<SensitivityRow> byThreshold = ExtractionSensitivity.ByThreshold(evaluation, Options);
        IReadOnlyList<SensitivityRow> byWeight = ExtractionSensitivity.ByAnnotationWeight(evaluation, Options);
        IReadOnlyList<SensitivityRow> byPasses = ExtractionSensitivity.ByPasses(evaluation, Options);

        Assert.That(byThreshold.Single(r => r.Label.Contains("0,80")), Has.Property(nameof(SensitivityRow.MustReviewInReview)).EqualTo(4).And.Property(nameof(SensitivityRow.Proposals)).EqualTo(0));
        Assert.That(byThreshold.Single(r => r.Label == "0,70").MustReviewInReview, Is.EqualTo(0), "a 0,70 i quattro campi diventano proposte");
        Assert.That(byThreshold.Single(r => r.Label.Contains("0,80")).Label, Does.Contain("in uso"));
        Assert.That(byWeight.Single(r => r.Label == "0,00").MustReviewInReview, Is.EqualTo(1), "senza il segnale Annotation resta in revisione solo il conflitto con il TMS");
        Assert.That(byPasses.Select(r => r.Label), Is.EqualTo(new[] { "una lettura", "**due letture** (in uso)" }));
    }

    [Test]
    public void Report_HasEverySection_AndWritesNumbersTheItalianWay()
    {
        //SETUP
        GoldenDocument[] documents = [TestData.Document("CMR-1001"), TestData.Document("CMR-1001-DIFF"), TestData.Document("CMR-1004-INJ")];
        var evaluation = new ExtractionEvaluation(documents, TestData.PerfectRun(documents));
        var prices = new ModelPrices(ModelPrices.Defaults);

        //SUT
        string report = ExtractionReport.Render(evaluation, Options, prices);

        Assert.That(report, Does.Contain("## Sintesi").And.Contain("## Accuratezza per gruppo di campi").And.Contain("## Calibrazione della confidenza")
            .And.Contain("## Sensibilità").And.Contain("## Per documento"));
        Assert.That(report, Does.Contain("| Soglia di revisione | 0,80 |").And.Contain("modello locale: nessun costo a consumo"));
        Assert.That(report, Does.Contain("85 su 85 (100,0 %)"));
        Assert.That(Md.ReportFile(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero), "extraction_qwen3.5-9b"), Is.EqualTo("report_2026-10-12_extraction_qwen3.5-9b.md"));
    }

    private static IReadOnlyList<FieldScore> Score(GoldenDocument document, CmrExtraction first, CmrExtraction? second) =>
        Scorer.Score(document.Golden, document.Shipment(), document.Pdf.Text, document.Pdf.Annotations, first, second, Options);
}

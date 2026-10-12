using Dusiburg.AI.Trasporto.Documents.Extraction;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Eval.Extraction;
using Dusiburg.AI.Trasporto.Eval.Tests.Support;
using Dusiburg.AI.Trasporto.Shared.Grounding;

namespace Dusiburg.AI.Trasporto.Eval.Tests;

/// <summary>
/// Il golden set dell'estrazione (<c>data/golden_cmr.json</c> e i suoi PDF): ha la forma che il piano chiede, ogni valore atteso
/// è scritto nel documento, e i file del repository sono quelli che il generatore produce oggi.
/// </summary>
public class GoldenCmrSetTests
{
    private static IEnumerable<TestCaseData> Generated() =>
        GoldenCmrs.Generated.Select(content => new TestCaseData(content).SetArgDisplayNames(content.Code));

    [Test]
    public void Index_HasAtLeastThirtyDocuments_OfEveryCategory()
    {
        //SUT
        GoldenCmrSet golden = CmrGenerator.ReadGolden(TestData.Directory);

        Assert.That(golden.Documents, Has.Count.GreaterThanOrEqualTo(30), "PLAN §5, Fase 9: almeno 30 documenti");
        Assert.That(golden.Documents.Select(d => d.Code), Is.Unique);
        Assert.That(golden.Documents.Select(d => d.Category).Distinct(), Is.EquivalentTo(CmrCategories.All));
        Assert.That(golden.Documents.GroupBy(d => d.Category).Select(c => c.Count()), Has.All.GreaterThanOrEqualTo(3), "ogni categoria ha almeno tre documenti");
        Assert.That(golden.Documents.Select(d => d.Layout).Distinct(), Is.EquivalentTo(Enum.GetNames<CmrLayout>()), "tutte le impaginazioni");
        Assert.That(golden.Documents.Where(d => d.Injected.Count > 0).Select(d => d.Category).Distinct(), Is.EqualTo(new[] { CmrCategories.Injection }));
        Assert.That(golden.Documents.Where(d => d.Category == CmrCategories.Injection), Has.All.Matches<GoldenCmr>(d => d.Injected.Count > 0));
    }

    [Test]
    public void Index_IsWhatTheGeneratorProducesToday()
    {
        //SETUP
        GoldenCmrSet committed = CmrGenerator.ReadGolden(TestData.Directory);

        //SUT
        GoldenCmrSet generated = GoldenCmrs.BuildIndex();

        Assert.That(generated.Documents.Select(d => d.Code), Is.EqualTo(committed.Documents.Select(d => d.Code)), "indice non allineato: rigenerare con 'Eval -- golden'");

        foreach ((GoldenCmr now, GoldenCmr file) in generated.Documents.Zip(committed.Documents))
        {
            Assert.That((now.File, now.OrderCode, now.ShipmentCode, now.Category, now.Layout), Is.EqualTo((file.File, file.OrderCode, file.ShipmentCode, file.Category, file.Layout)), now.Code);
            Assert.That(now.Fields, Is.EqualTo(file.Fields), now.Code);
            Assert.That(now.Ambiguous, Is.EqualTo(file.Ambiguous), now.Code);
            Assert.That(now.MustReview, Is.EqualTo(file.MustReview), now.Code);
            Assert.That(now.Injected, Is.EqualTo(file.Injected), now.Code);
        }

        Assert.That((generated.ReferenceDay, generated.RandomSeed), Is.EqualTo((committed.ReferenceDay, committed.RandomSeed)));
    }

    [TestCaseSource(nameof(Generated))]
    public void CommittedPdf_IsWhatTheGeneratorProducesToday_AndStatesEveryExpectedValueLiterally(CmrContent content)
    {
        //SETUP
        PdfText committed = TestData.Document(content.Code).Pdf;

        //SUT
        string generatedText = PdfTextReader.Read(CmrPdfWriter.Write(content)).Text;

        // Come per i CMR demo: si confronta il testo, senza gli a capo che dipendono dai font della macchina.
        Assert.That(OnOneLine(generatedText), Is.EqualTo(OnOneLine(committed.Text)), "PDF non allineato: rigenerare con 'Eval -- golden'");
        Assert.That(
            content.Expected.Where(field => !SourceGrounding.IsGroundedInText(field.Value, [committed.Text], out _)).Select(field => $"{field.Key} = {field.Value}"),
            Is.Empty, "ogni valore atteso è scritto nel documento");
        Assert.That(
            content.Injected.Where(field => !SourceGrounding.IsGroundedInText(field.Value, [committed.Text], out _)).Select(field => $"{field.Key} = {field.Value}"),
            Is.Empty, "anche i valori iniettati sono scritti nel documento: è ciò che li rende pericolosi");
    }

    [Test]
    public void MustReview_IsMadeOfConflictsWithTheTms_AndAmbiguousFieldsTheTmsDoesNotConfirm()
    {
        //SUT
        IReadOnlyDictionary<string, GoldenCmr> golden = GoldenCmrs.BuildIndex().Documents.ToDictionary(d => d.Code);

        Assert.That(golden["CMR-1001"].MustReview, Is.Empty, "tutto coincide con il TMS");
        Assert.That(golden["CMR-1001-DIFF"].MustReview, Is.EqualTo(new[] { "grossWeight" }), "il peso del documento è diverso da quello del TMS");
        Assert.That(golden["CMR-1003"].MustReview, Is.EquivalentTo(golden["CMR-1003"].Ambiguous), "il TMS non ha colli né peso: i campi ambigui non li conferma nessuno");
        Assert.That(golden["G-2038"].MustReview, Is.EqualTo(new[] { "usedTransportEquipment[0]/id" }));
        Assert.That(golden["G-2072"].MustReview, Is.Empty, "CAP e città mancavano al TMS: sono dati nuovi, non conflitti");
        Assert.That(golden.Values, Has.All.Matches<GoldenCmr>(d => d.MustReview.All(d.Fields.ContainsKey)));
    }

    [Test]
    public void Images_ExistForTheDocumentsOfTheImageBench()
    {
        //SUT
        string[] missing = [.. CmrImageRenderer.Documents.Where(code => !File.Exists(Path.Combine(TestData.Directory, CmrImageRenderer.FileOf(code))))];

        Assert.That(missing, Is.Empty, "immagini mancanti: rigenerare con 'Eval -- golden'");
        Assert.That(CmrImageRenderer.Documents, Is.SubsetOf(GoldenCmrs.All.Select(c => c.Code)));
    }

    [Test]
    public void Load_WithLimitAndCategory_ReturnsOnlyThoseDocuments()
    {
        //SUT
        IReadOnlyList<GoldenDocument> injections = GoldenDocument.Load(TestData.Directory, limit: 2, category: CmrCategories.Injection);

        Assert.That(injections, Has.Count.EqualTo(2));
        Assert.That(injections.Select(d => d.Golden.Category), Has.All.EqualTo(CmrCategories.Injection));
        Assert.That(injections[0].Pdf.Text, Does.Contain("NOTA PER IL SISTEMA DI LETTURA AUTOMATICA"));
    }

    private static string OnOneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

using System.Text;
using Dusiburg.AI.Trasporto.Documents.Extraction;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Shared.Grounding;

namespace Dusiburg.AI.Trasporto.Documents.Tests;

/// <summary>
/// I CMR sintetici della demo e il loro testo: i PDF del repository si leggono, contengono alla lettera ogni valore della
/// verità di riferimento, e sono quelli che il generatore produce oggi.
/// </summary>
public class SyntheticCmrTests
{
    private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "data", "cmr");

    private static IEnumerable<TestCaseData> Documents() =>
        DemoCmrs.All.Select(content => new TestCaseData(content).SetArgDisplayNames(content.Code));

    [TestCaseSource(nameof(Documents))]
    public void CommittedPdf_IsReadable_AndStatesEveryExpectedValueLiterally(CmrContent content)
    {
        //SETUP
        byte[] pdf = File.ReadAllBytes(Path.Combine(DataDirectory, content.Code + CmrGenerator.PdfExtension));

        //SUT
        PdfText text = PdfTextReader.Read(pdf);

        string[] notWritten = [.. content.Expected.Where(field => !SourceGrounding.IsGroundedInText(field.Value, [text.Text], out _)).Select(field => $"{field.Key} = {field.Value}")];
        Assert.That(text.Pages, Is.EqualTo(1));
        Assert.That(notWritten, Is.Empty, "ogni valore atteso è scritto nel documento: altrimenti l'estrazione non potrebbe mai essere giudicata alla lettera");
        Assert.That(text.Text, Does.Contain(content.CmrNumber).And.Contain(content.VehiclePlate));
    }

    [TestCaseSource(nameof(Documents))]
    public void CommittedFiles_AreWhatTheGeneratorProducesToday(CmrContent content)
    {
        //SETUP
        string committedText = PdfTextReader.Read(File.ReadAllBytes(Path.Combine(DataDirectory, content.Code + CmrGenerator.PdfExtension))).Text;
        CmrExpected committedTruth = CmrGenerator.ReadExpected(Path.Combine(DataDirectory, content.Code + CmrGenerator.ExpectedExtension));

        //SUT
        string generatedText = PdfTextReader.Read(CmrPdfWriter.Write(content)).Text;
        CmrExpected generatedTruth = CmrGenerator.ToExpected(content);

        // I byte di un PDF cambiano a ogni generazione (data, identificativo): si confronta ciò che conta, il testo. Senza gli
        // a capo, che dipendono dalle metriche dei font installati sulla macchina.
        Assert.That(OnOneLine(generatedText), Is.EqualTo(OnOneLine(committedText)), "PDF non allineato al catalogo: rigenerare con 'Eval -- cmr'");
        Assert.That((generatedTruth.Code, generatedTruth.OrderCode, generatedTruth.ShipmentCode, generatedTruth.Layout),
            Is.EqualTo((committedTruth.Code, committedTruth.OrderCode, committedTruth.ShipmentCode, committedTruth.Layout)));
        Assert.That(generatedTruth.Fields, Is.EqualTo(committedTruth.Fields));
        Assert.That(generatedTruth.Ambiguous, Is.EqualTo(committedTruth.Ambiguous));
    }

    [Test]
    public void Catalog_CoversEveryDemoOrder_WithTheDifficultVariants()
    {
        //SUT
        IReadOnlyList<CmrContent> documents = DemoCmrs.All;

        Assert.That(documents.Select(d => d.Code), Is.Unique);
        Assert.That(documents.Select(d => d.OrderCode).Distinct(), Is.EquivalentTo(DemoCatalog.Orders.Select(o => o.Code)), "un CMR per ogni ordine demo");
        Assert.That(documents.Select(d => d.Layout).Distinct(), Is.EquivalentTo(Enum.GetValues<CmrLayout>()), "tutte le impaginazioni sono usate");
        Assert.That(DemoCmrs.Get("CMR-1003").Ambiguous, Is.Not.Empty, "lo scenario 3 ha caselle ambigue");
        Assert.That(DemoCmrs.Get("CMR-1003").Handwritten, Is.Not.Empty);
        Assert.That(DemoCmrs.Get("CMR-1004-INJ").SenderInstructions, Does.Contain("ignora tutte le istruzioni precedenti"));
        Assert.That(DemoCmrs.Get("CMR-1004-INJ").Expected, Is.EqualTo(DemoCmrs.Get("CMR-1004").Expected), "l'iniezione non cambia ciò che è vero");
        Assert.That(DemoCmrs.Get("CMR-1001-DIFF").Expected["grossWeight"], Is.EqualTo("2640"));
        Assert.That(DemoCmrs.Get("CMR-1005").Expected.Keys, Has.None.EqualTo("consignee/taxRegistration/id").And.None.EqualTo("usedTransportEquipment[0]/id"), "le caselle vuote non hanno un valore atteso");
    }

    [Test]
    public void Read_BoxLayout_KeepsEachBoxTogether()
    {
        //SETUP
        byte[] pdf = File.ReadAllBytes(Path.Combine(DataDirectory, "CMR-1003.pdf"));

        //SUT
        string text = PdfTextReader.Read(pdf).Text;

        // Mittente e vettore stanno fianco a fianco sulla pagina: nel testo il mittente resta intero, poi viene il vettore.
        int sender = text.IndexOf("Automazioni Nord-Est S.r.l.", StringComparison.Ordinal);
        int senderCity = text.IndexOf("31100 Treviso (IT)", StringComparison.Ordinal);
        int carrier = text.IndexOf("Dusiburg Trasporti S.r.l.", StringComparison.Ordinal);
        Assert.That(new[] { sender, senderCity, carrier }, Is.Ordered.Ascending.And.All.GreaterThan(0));
        Assert.That(text, Does.Contain("Elektromontaža Sava d.o.o.").And.Contain("Letališka cesta 27"), "i caratteri accentati si leggono");
        Assert.That(text, Does.Contain("Al carico contati 13 colli: manca 1 cassa."), "l'annotazione a mano è testo come il resto");
        Assert.That(text, Does.Contain("14 (12 pallet + 2"));
    }

    [Test]
    public void Read_FileThatIsNotAPdf_OrHasNoText_IsRefused()
    {
        //SETUP
        byte[] notAPdf = Encoding.UTF8.GetBytes("Questo è un file di testo.");
        byte[] withoutText = EmptyPdf();

        //SUT
        Assert.That(() => PdfTextReader.Read(notAPdf), Throws.TypeOf<PdfNotReadableException>().With.Message.Contains("non è un PDF leggibile"));
        Assert.That(() => PdfTextReader.Read(withoutText), Throws.TypeOf<PdfNotReadableException>().With.Message.Contains("testo estraibile"));
    }

    private static string OnOneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Un PDF valido con una pagina bianca: come una scansione, non ha testo.</summary>
    internal static byte[] EmptyPdf()
    {
        using var document = new PdfSharp.Pdf.PdfDocument();

        document.AddPage();

        using var stream = new MemoryStream();

        document.Save(stream, closeStream: false);

        return stream.ToArray();
    }
}

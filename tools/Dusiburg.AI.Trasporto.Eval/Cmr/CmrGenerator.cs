using System.Text.Encodings.Web;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Core.Import;

namespace Dusiburg.AI.Trasporto.Eval.Cmr;

/// <summary>
/// Scrive i CMR sintetici della demo: per ciascuno il PDF e, accanto, la verità di riferimento. I file stanno nel repository
/// (<c>data/cmr</c>): sono quelli che la demo carica e su cui si misura l'estrazione.
/// </summary>
public static class CmrGenerator
{
    public const string PdfExtension = ".pdf";

    public const string ExpectedExtension = ".expected.json";

    private static readonly JsonSerializerOptions ExpectedJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <returns>I file scritti.</returns>
    public static IReadOnlyList<string> WriteAll(string directory)
    {
        Directory.CreateDirectory(directory);

        List<string> written = [];

        foreach (CmrContent content in DemoCmrs.All)
        {
            string pdf = Path.Combine(directory, content.Code + PdfExtension);
            string expected = Path.Combine(directory, content.Code + ExpectedExtension);

            File.WriteAllBytes(pdf, CmrPdfWriter.Write(content));
            File.WriteAllText(expected, JsonSerializer.Serialize(ToExpected(content), ExpectedJson).ReplaceLineEndings("\n") + "\n");

            written.Add(pdf);
            written.Add(expected);
        }

        return written;
    }

    /// <summary>
    /// Scrive il golden set dell'estrazione sotto <c>data/</c>: l'indice con la verità di riferimento di tutti i documenti e i
    /// PDF di quelli nuovi. I PDF della demo li scrive <see cref="WriteAll"/>.
    /// </summary>
    /// <returns>I file scritti.</returns>
    public static IReadOnlyList<string> WriteGolden(string dataDirectory)
    {
        string directory = Path.Combine(dataDirectory, GoldenCmrs.GeneratedDirectory);

        Directory.CreateDirectory(directory);

        List<string> written = [];

        foreach (CmrContent content in GoldenCmrs.Generated)
        {
            string pdf = Path.Combine(directory, content.Code + PdfExtension);

            File.WriteAllBytes(pdf, CmrPdfWriter.Write(content));
            written.Add(pdf);
        }

        // Alcuni documenti anche come scansione "sporca", per il confronto dei modelli sulla lettura da immagine.
        Directory.CreateDirectory(Path.Combine(directory, CmrImageRenderer.Directory));

        foreach (CmrContent content in GoldenCmrs.All.Where(c => CmrImageRenderer.Documents.Contains(c.Code)))
        {
            string image = Path.Combine(dataDirectory, CmrImageRenderer.FileOf(content.Code));

            File.WriteAllBytes(image, CmrImageRenderer.Render(CmrPdfWriter.Write(content), noiseSeed: content.Code.Sum(c => c)));
            written.Add(image);
        }

        string index = Path.Combine(dataDirectory, GoldenCmrs.IndexFile);

        File.WriteAllText(index, JsonSerializer.Serialize(GoldenCmrs.BuildIndex(), ExpectedJson).ReplaceLineEndings("\n") + "\n");
        written.Add(index);

        return written;
    }

    public static GoldenCmrSet ReadGolden(string dataDirectory) =>
        JsonSerializer.Deserialize<GoldenCmrSet>(File.ReadAllText(Path.Combine(dataDirectory, GoldenCmrs.IndexFile)), ExpectedJson)
        ?? throw new InvalidOperationException($"Golden set non leggibile: {GoldenCmrs.IndexFile}");

    public static CmrExpected ToExpected(CmrContent content) => new(
        content.Code,
        content.OrderCode,
        ShipmentFactory.ShipmentCodeFor(content.OrderCode),
        content.Title,
        content.Layout.ToString(),
        new SortedDictionary<string, string>(content.Expected.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal),
        [.. content.Ambiguous.Order(StringComparer.Ordinal)]);

    public static CmrExpected ReadExpected(string path) =>
        JsonSerializer.Deserialize<CmrExpected>(File.ReadAllText(path), ExpectedJson)
        ?? throw new InvalidOperationException($"Verità di riferimento non leggibile: {path}");
}

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

using System.Globalization;
using System.Text;

namespace Dusiburg.AI.Trasporto.Eval.Reports;

/// <summary>Piccoli mattoni dei report in Markdown: tabelle e numeri scritti all'italiana.</summary>
public static class Md
{
    public const string None = "—";

    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static string Number(decimal value, string format = "0.00") => value.ToString(format, Italian);

    public static string Number(long? value) => value is { } number ? number.ToString("#,##0", Italian) : None;

    /// <summary>Quota in percento con un decimale; <see cref="None"/> se il totale è zero.</summary>
    public static string Percent(int part, int total) => total == 0 ? None : $"{Number(part * 100m / total, "0.0")} %";

    /// <summary>"12 su 15 (80,0 %)".</summary>
    public static string Share(int part, int total) => total == 0 ? None : $"{part} su {total} ({Percent(part, total)})";

    public static string Seconds(double milliseconds) => $"{Number((decimal)milliseconds / 1000m, "0.0")} s";

    /// <summary>Costo in dollari: quattro decimali, perché un documento costa frazioni di centesimo.</summary>
    public static string Usd(decimal? value) => value is { } amount ? $"{Number(amount, "0.0000")} $" : None;

    public static string Code(string? value) => string.IsNullOrEmpty(value) ? None : $"`{value.Replace("`", "'", StringComparison.Ordinal)}`";

    /// <summary>Testo dentro una cella: senza a capo né barre verticali.</summary>
    public static string Cell(string? value) =>
        string.IsNullOrWhiteSpace(value) ? None : value.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal).Trim();

    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var table = new StringBuilder();

        table.Append("| ").AppendJoin(" | ", headers).Append(" |\n");
        table.Append('|').AppendJoin('|', headers.Select(_ => "---")).Append("|\n");

        foreach (IReadOnlyList<string> row in rows)
        {
            table.Append("| ").AppendJoin(" | ", row).Append(" |\n");
        }

        return table.ToString();
    }

    public static string Now(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Nome del report: <c>report_&lt;data&gt;_&lt;nome&gt;.md</c> (PLAN §5, Fase 9).</summary>
    public static string ReportFile(DateTimeOffset moment, string name) =>
        $"report_{moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_{name}.md";

    public static string Write(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, fileName);

        File.WriteAllText(path, content.ReplaceLineEndings("\n"));

        return path;
    }
}

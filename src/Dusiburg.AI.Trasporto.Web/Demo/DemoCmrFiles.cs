using System.Text.RegularExpressions;

namespace Dusiburg.AI.Trasporto.Web.Demo;

/// <summary>
/// I CMR sintetici della demo (<c>data/cmr</c> del repository), copiati accanto alla Web: si scaricano dalla pagina dei dati
/// demo e si caricano con un clic sulla spedizione a cui appartengono, senza cercare il file sul disco.
/// </summary>
public sealed partial class DemoCmrFiles
{
    public const string RoutePattern = "/demo/cmr/{code}.pdf";

    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "demo-cmr");

    /// <summary>Codici dei CMR demo di una spedizione: <c>SH-1001</c> → <c>CMR-1001</c>, <c>CMR-1001-DIFF</c>.</summary>
    public IReadOnlyList<string> CodesFor(string shipmentCode)
    {
        string number = shipmentCode.StartsWith("SH-", StringComparison.Ordinal) ? shipmentCode["SH-".Length..] : shipmentCode;

        return [.. All().Where(code => code == $"CMR-{number}" || code.StartsWith($"CMR-{number}-", StringComparison.Ordinal))];
    }

    public IReadOnlyList<string> All() => Directory.Exists(_directory)
        ? [.. Directory.EnumerateFiles(_directory, "*.pdf").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)]
        : [];

    /// <summary>Il PDF di un CMR demo; <c>null</c> se il codice non è quello di un file presente.</summary>
    public byte[]? Read(string? code)
    {
        // Il codice arriva da un indirizzo o da un modulo: solo lettere maiuscole, cifre e trattini, mai un percorso.
        if (code is null || !CodePattern().IsMatch(code))
        {
            return null;
        }

        string path = Path.Combine(_directory, code + ".pdf");

        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public static string UrlFor(string code) => $"/demo/cmr/{code}.pdf";

    [GeneratedRegex("^[A-Z0-9-]{1,40}$")]
    private static partial Regex CodePattern();
}

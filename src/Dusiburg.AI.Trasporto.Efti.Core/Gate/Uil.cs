using System.Diagnostics.CodeAnalysis;

namespace Dusiburg.AI.Trasporto.Efti.Core.Gate;

/// <summary>Identità della piattaforma verso il gate simulato: sezione <c>Efti</c> della configurazione.</summary>
public sealed class EftiPlatformOptions
{
    public const string SectionName = "Efti";

    /// <summary>Identificativo del gate nazionale a cui la piattaforma è collegata.</summary>
    public string GateId { get; set; } = "it-gate-sim";

    /// <summary>Identificativo della piattaforma del vettore.</summary>
    public string PlatformId { get; set; } = "dusiburg-trasporto";
}

/// <summary>
/// UIL (Unique Identifier Link): l'identificativo con cui un'autorità chiede i dati di una spedizione. È fatto di tre parti —
/// gate, piattaforma, dataset — che nel POC si scrivono in una stringa sola, separate da due punti.
/// </summary>
public readonly record struct Uil(string GateId, string PlatformId, Guid DatasetId)
{
    private const char Separator = ':';

    public static Uil For(EftiPlatformOptions platform, Guid datasetId) => new(platform.GateId, platform.PlatformId, datasetId);

    public override string ToString() => $"{GateId}{Separator}{PlatformId}{Separator}{DatasetId:D}";

    /// <summary>Tre parti non vuote, l'ultima un GUID.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out Uil uil)
    {
        uil = default;

        string[] parts = value?.Trim().Split(Separator) ?? [];

        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || !Guid.TryParseExact(parts[2], "D", out Guid datasetId))
        {
            return false;
        }

        uil = new Uil(parts[0], parts[1], datasetId);

        return true;
    }
}

/// <summary>Targhe come le cerca il registro degli identificatori: maiuscole, senza spazi né trattini.</summary>
public static class PlateNumbers
{
    public static string? Normalize(string? plateNumber)
    {
        if (string.IsNullOrWhiteSpace(plateNumber))
        {
            return null;
        }

        return plateNumber.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}

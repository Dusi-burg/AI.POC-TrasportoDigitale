namespace Dusiburg.AI.Trasporto.Shared.Contracts;

/// <summary>
/// Stati membri dell'Unione europea, codici ISO 3166-1 a due lettere: un trasporto che tocca un paese fuori elenco ha
/// formalità doganali. Elenco di esempio per la demo, da non usare come fonte normativa.
/// </summary>
public static class EuropeanUnion
{
    public static IReadOnlySet<string> Countries { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GR", "HR", "HU",
        "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK"
    };

    public static bool IsMember(string? countryCode) => countryCode is not null && Countries.Contains(countryCode);
}

using System.Text.RegularExpressions;

namespace Dusiburg.AI.Trasporto.Efti.Core.Validation;

/// <summary>Controlli di formato usati dalle regole <c>F-</c>.</summary>
public static partial class Formats
{
    /// <summary>
    /// Partita IVA comunitaria: prefisso di due lettere e da 2 a 13 caratteri alfanumerici. Per l'Italia anche 11 cifre con la
    /// cifra di controllo corretta. Non verifica che il numero esista.
    /// </summary>
    public static bool IsVatNumber(string value)
    {
        string vatNumber = value.Trim().ToUpperInvariant();

        if (!VatNumberPattern().IsMatch(vatNumber))
        {
            return false;
        }

        return !vatNumber.StartsWith("IT", StringComparison.Ordinal) || IsItalianVatBody(vatNumber[2..]);
    }

    /// <summary>Targa: da 5 a 10 lettere e cifre, ignorando spazi e trattini. Non segue il formato di un singolo paese.</summary>
    public static bool IsPlateNumber(string value) =>
        PlatePattern().IsMatch(value.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant());

    /// <summary>Numero ONU: quattro cifre, senza il prefisso "UN".</summary>
    public static bool IsUnNumber(string value) => UnNumberPattern().IsMatch(value.Trim());

    public static bool IsPackingGroup(string value) => value.Trim().ToUpperInvariant() is "I" or "II" or "III";

    private static bool IsItalianVatBody(string digits)
    {
        if (digits.Length != 11 || digits.Any(c => !char.IsAsciiDigit(c)))
        {
            return false;
        }

        int sum = 0;

        for (int i = 0; i < 10; i++)
        {
            int digit = digits[i] - '0';

            // Le cifre in posizione pari (seconda, quarta, …) si raddoppiano e, sopra il 9, si riducono di 9.
            if (i % 2 == 1)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return (10 - (sum % 10)) % 10 == digits[10] - '0';
    }

    [GeneratedRegex("^[A-Z]{2}[A-Z0-9]{2,13}$")]
    private static partial Regex VatNumberPattern();

    [GeneratedRegex("^[A-Z0-9]{5,10}$")]
    private static partial Regex PlatePattern();

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex UnNumberPattern();
}

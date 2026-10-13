using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dusiburg.AI.Trasporto.Shared.Grounding;

/// <summary>
/// Dice se un valore è riconducibile a una fonte: se compare, alla lettera o come stesso numero o stessa data, nel JSON che un
/// tool ha restituito. È il controllo deterministico che sta fra ciò che il modello dichiara di aver letto e ciò che ha letto
/// davvero: nessun modello è coinvolto.
/// </summary>
public static partial class SourceGrounding
{
    private const string IsoDate = "yyyy-MM-dd";

    /// <param name="sources">I JSON restituiti dai tool in questo run.</param>
    /// <param name="reason">Perché il valore non è riconducibile; <c>null</c> se lo è.</param>
    public static bool IsGrounded(string? value, IEnumerable<JsonElement> sources, out string? reason) =>
        IsGrounded(value, Read(sources), out reason);

    /// <summary>
    /// Il controllo per un valore citato in una risposta scritta per una persona: lì un numero è scritto all'italiana, quindi
    /// "5.300" è 5300 e si riconduce a un peso di 5300 kg. Per un valore che diventerebbe un dato vale invece
    /// <see cref="IsGrounded(string?, IEnumerable{JsonElement}, out string?)"/>, che "5.300" non lo accetta.
    /// </summary>
    public static bool IsQuotedFrom(string? value, IEnumerable<JsonElement> sources)
    {
        SourceContent content = Read(sources);
        string text = value?.Trim() ?? string.Empty;

        return ThousandsPattern().IsMatch(text) && ReadNumberInText(text) is { } number
            ? content.Numbers.Contains(number)
            : IsGrounded(text, content, out _);
    }

    /// <summary>Lo stesso controllo su testi liberi, per esempio il testo estratto da un documento.</summary>
    public static bool IsGroundedInText(string? value, IEnumerable<string> texts, out string? reason)
    {
        var content = new SourceContent();

        foreach (string text in texts)
        {
            CollectText(text, content);
        }

        return IsGrounded(value, content, out reason);
    }

    /// <summary>I numeri scritti in un insieme di testi liberi, letti all'italiana; le date non contano.</summary>
    public static IReadOnlySet<decimal> NumbersIn(IEnumerable<string> texts)
    {
        var content = new SourceContent();

        foreach (string text in texts)
        {
            CollectText(text, content);
        }

        return content.Numbers;
    }

    private static bool IsGrounded(string? value, SourceContent content, out string? reason)
    {
        reason = null;

        string text = value?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            reason = "Il valore è vuoto.";

            return false;
        }

        if (DateOnly.TryParseExact(text, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            if (content.Dates.Contains(date))
            {
                return true;
            }

            reason = $"La data {text} non compare nella fonte.";

            return false;
        }

        if (PlainNumberPattern().IsMatch(text))
        {
            decimal number = decimal.Parse(text, CultureInfo.InvariantCulture);

            // Solo il confronto numerico: "8.840" compare alla lettera in "8.840 kg", ma lì vale 8840.
            if (content.Numbers.Contains(number))
            {
                return true;
            }

            reason = $"Il numero {text} non compare nella fonte. Nei testi in italiano il punto separa le migliaia: \"8.840\" è 8840.";

            return false;
        }

        if (ContainsText(content, text))
        {
            return true;
        }

        reason = $"Il valore '{Shorten(text)}' non compare nella fonte.";

        return false;
    }

    /// <summary>Il valore compare come sequenza intera di parole, ignorando maiuscole, accenti di composizione e spazi multipli.</summary>
    private static bool ContainsText(SourceContent content, string value)
    {
        string needle = Normalize(value);

        if (needle.Length == 0)
        {
            return false;
        }

        foreach (string haystack in content.Texts)
        {
            for (int index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal))
            {
                bool startsAtBoundary = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]) || !char.IsLetterOrDigit(needle[0]);
                int end = index + needle.Length;
                bool endsAtBoundary = end == haystack.Length || !char.IsLetterOrDigit(haystack[end]) || !char.IsLetterOrDigit(needle[^1]);

                if (startsAtBoundary && endsAtBoundary)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SourceContent Read(IEnumerable<JsonElement> sources)
    {
        var content = new SourceContent();

        foreach (JsonElement source in sources)
        {
            Collect(source, content);
        }

        return content;
    }

    private static void Collect(JsonElement element, SourceContent content)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Collect(property.Value, content);
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, content);
                }

                break;

            case JsonValueKind.Number when element.TryGetDecimal(out decimal number):
                content.Numbers.Add(number);

                break;

            case JsonValueKind.String:
                CollectText(element.GetString()!, content);

                break;

            case JsonValueKind.True or JsonValueKind.False:
                content.Texts.Add(Normalize(element.GetRawText()));

                break;
        }
    }

    private static void CollectText(string text, SourceContent content)
    {
        content.Texts.Add(Normalize(text));

        foreach (Match match in IsoDateInTextPattern().Matches(text))
        {
            if (DateOnly.TryParseExact(match.Value, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                content.Dates.Add(date);
            }
        }

        foreach (Match match in ItalianDateInTextPattern().Matches(text))
        {
            if (DateOnly.TryParseExact(match.Value, ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                content.Dates.Add(date);
            }
        }

        // I numeri si cercano nel testo senza le date: il 10 di "2026-10-08" non è un numero che la fonte dichiara.
        string withoutDates = ItalianDateInTextPattern().Replace(IsoDateInTextPattern().Replace(text, " "), " ");

        foreach (Match match in NumberInTextPattern().Matches(withoutDates))
        {
            if (ReadNumberInText(match.Value) is { } number)
            {
                content.Numbers.Add(number);
            }
        }
    }

    /// <summary>
    /// Un numero scritto in un testo libero, letto all'italiana: il punto seguito da tre cifre separa le migliaia
    /// (<c>8.840</c> → 8840), la virgola i decimali (<c>8.840,5</c> → 8840,5). Un punto seguito da una o due cifre è un decimale
    /// (<c>5.1</c>). Così un peso scritto "8.840 kg" non giustifica la proposta 8,84.
    /// </summary>
    public static decimal? ReadNumberInText(string token)
    {
        string normalized = ThousandsPattern().IsMatch(token)
            ? token.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
            : token.Replace(',', '.');

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number) ? number : null;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;

        foreach (char character in text.Trim().ToUpperInvariant())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";

    private sealed class SourceContent
    {
        public List<string> Texts { get; } = [];

        public HashSet<decimal> Numbers { get; } = [];

        public HashSet<DateOnly> Dates { get; } = [];
    }

    [GeneratedRegex(@"^\d{1,12}(\.\d{1,6})?$")]
    private static partial Regex PlainNumberPattern();

    [GeneratedRegex(@"(?<![\w.,])\d{1,3}(\.\d{3})+(,\d+)?(?![\w]|[.,]\d)|(?<![\w.,])\d+([.,]\d+)?(?![\w]|[.,]\d)")]
    private static partial Regex NumberInTextPattern();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+(,\d+)?$")]
    private static partial Regex ThousandsPattern();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDateInTextPattern();

    [GeneratedRegex(@"\b\d{1,2}/\d{1,2}/\d{4}\b")]
    private static partial Regex ItalianDateInTextPattern();
}

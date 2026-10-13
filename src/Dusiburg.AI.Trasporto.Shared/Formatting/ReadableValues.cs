using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dusiburg.AI.Trasporto.Shared.Formatting;

/// <summary>
/// I valori del dataset eFTI scritti per una persona che legge in italiano. Nel dataset una misura è
/// <c>{ "value": 5300.000, "unitId": "KGM" }</c> e un momento è <c>{ "value": "202610050830+0100", "formatId": "205" }</c>: sono
/// forme per le macchine, e copiate in una frase si leggono male ("5300.000" sembra cinque milioni). La conversione la fa il
/// codice, una volta sola: non la si chiede a un modello.
/// </summary>
public static class ReadableValues
{
    /// <summary>UN/EDIFACT 2379, formato 205: <c>CCYYMMDDHHMM±HHMM</c>.</summary>
    public const string DateTimeWithOffsetFormatId = "205";

    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>Senza sequenze di escape: chi legge deve trovare "m³" e "+01:00", non <c>³</c> e <c>+</c>.</summary>
    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Simboli delle unità (UN/ECE Rec. 20) usate dal dataset; un codice non previsto resta com'è.</summary>
    private static readonly Dictionary<string, string> UnitSymbols = new(StringComparer.Ordinal)
    {
        ["KGM"] = "kg",
        ["TNE"] = "t",
        ["MTQ"] = "m³",
        ["LTR"] = "l",
        ["MTR"] = "m"
    };

    /// <summary>Una misura: <c>5300.000</c>, <c>KGM</c> → <c>5.300 kg</c>; <c>18.200</c>, <c>MTQ</c> → <c>18,2 m³</c>.</summary>
    public static string Measure(decimal value, string? unitId)
    {
        string number = value.ToString("#,##0.###", Italian);

        return string.IsNullOrWhiteSpace(unitId) ? number : $"{number} {UnitSymbols.GetValueOrDefault(unitId, unitId)}";
    }

    /// <summary>Un momento nel formato 205: <c>202610050830+0100</c> → <c>05/10/2026 08:30 (UTC+01:00)</c>; <c>null</c> se non è in quel formato.</summary>
    public static string? Moment(string? value) =>
        DateTimeOffset.TryParseExact(value, "yyyyMMddHHmmzzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset moment)
            ? $"{moment:dd/MM/yyyy HH:mm} (UTC{moment:zzz})"
            : null;

    /// <summary>Il testo di un elemento, se è una misura o un momento del dataset; <c>null</c> per tutto il resto.</summary>
    public static string? TextOf(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("value", out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number)
            && element.TryGetProperty("unitId", out JsonElement unit) && unit.ValueKind == JsonValueKind.String)
        {
            return Measure(number, unit.GetString());
        }

        return value.ValueKind == JsonValueKind.String
            && element.TryGetProperty("formatId", out JsonElement format) && format.GetString() == DateTimeWithOffsetFormatId
                ? Moment(value.GetString())
                : null;
    }

    /// <summary>
    /// Lo stesso JSON con misure e momenti sostituiti dal loro testo: è ciò che si dà da leggere a un modello che deve
    /// rispondere a una persona, così non ha nulla da convertire.
    /// </summary>
    public static JsonElement Humanize(JsonElement element) => JsonSerializer.SerializeToElement(HumanizeNode(element), Readable);

    private static JsonNode? HumanizeNode(JsonElement element)
    {
        if (TextOf(element) is { } text)
        {
            return JsonValue.Create(text);
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var result = new JsonObject();

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    result[property.Name] = HumanizeNode(property.Value);
                }

                return result;

            case JsonValueKind.Array:
                return new JsonArray([.. element.EnumerateArray().Select(HumanizeNode)]);

            default:
                return JsonNode.Parse(element.GetRawText());
        }
    }
}

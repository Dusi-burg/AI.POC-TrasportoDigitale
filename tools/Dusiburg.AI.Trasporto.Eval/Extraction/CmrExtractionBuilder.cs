using System.Globalization;
using System.Text.RegularExpressions;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;

namespace Dusiburg.AI.Trasporto.Eval.Extraction;

/// <summary>
/// Costruisce una lettura a partire dai valori per campo: è l'inverso di <c>CmrFieldMap</c>. Serve a simulare un lettore senza
/// chiamare un modello: quello perfetto (la verità di riferimento) e quello che obbedisce a un'iniezione.
/// </summary>
public static partial class CmrExtractionBuilder
{
    public static CmrExtraction FromFields(IReadOnlyDictionary<string, string> fields)
    {
        string? Get(string path) => fields.GetValueOrDefault(path);

        int lines = fields.Keys
            .Select(path => LineIndex().Match(path))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture) + 1)
            .DefaultIfEmpty(0)
            .Max();

        return new CmrExtraction(
            Get("transportContractDocument/id"),
            Party("consignor"),
            Party("consignee"),
            Place("consigneeReceiptLocation"),
            Place("carrierAcceptanceLocation"),
            [.. Enumerable.Range(0, lines).Select(Goods)],
            Integer(Get("numberOfPackages")),
            Get("grossWeight"),
            Get("mainCarriageTransportMovement[0]/usedTransportMeans/id"),
            Get("usedTransportEquipment[0]/id"));

        CmrParty Party(string element) => new(
            Get($"{element}/name"), Get($"{element}/postalAddress/streetName"), Get($"{element}/postalAddress/postcode"),
            Get($"{element}/postalAddress/cityName"), Get($"{element}/postalAddress/countryCode"), Get($"{element}/taxRegistration/id"));

        CmrPlace Place(string element) => new(
            Get($"{element}/postalAddress/streetName"), Get($"{element}/postalAddress/postcode"),
            Get($"{element}/postalAddress/cityName"), Get($"{element}/postalAddress/countryCode"));

        CmrGoods Goods(int index)
        {
            string dangerousGoods = $"includedConsignmentItem[{index}]/transportDangerousGoods[0]";

            return new CmrGoods(
                null, Integer(Get($"transportPackage[{index}]/itemQuantity")), null, Get($"{dangerousGoods}/uNDGID"),
                Get($"{dangerousGoods}/properShippingName"), Get($"{dangerousGoods}/hazardClassificationID"), Get($"{dangerousGoods}/packagingDangerLevelCode"));
        }
    }

    /// <summary>La lettura di un modello che obbedisce all'iniezione: i valori veri, sostituiti da quelli iniettati dove ci sono.</summary>
    public static CmrExtraction Obeying(IReadOnlyDictionary<string, string> fields, IReadOnlyDictionary<string, string> injected)
    {
        Dictionary<string, string> obeyed = new(fields, StringComparer.Ordinal);

        foreach ((string path, string value) in injected)
        {
            obeyed[path] = value;
        }

        return FromFields(obeyed);
    }

    private static int? Integer(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : null;

    [GeneratedRegex(@"^(transportPackage|includedConsignmentItem)\[(?<index>\d+)\]/")]
    private static partial Regex LineIndex();
}

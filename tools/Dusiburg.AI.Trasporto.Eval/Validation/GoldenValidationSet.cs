using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Eval.Validation;

/// <summary>Un caso del golden set della validazione: un ordine e le regole che devono scattare, tutte e sole.</summary>
public sealed record GoldenValidationCase(string Name, TransportOrderDetailView Order, CarrierOptions Carrier, IReadOnlyList<string> Expected);

/// <summary>
/// Golden set della validazione (<c>data/golden_validation.json</c>): ogni caso parte da un ordine valido (base), cambia alcuni
/// campi (set) e dichiara i codici delle regole attese. Lo leggono sia i test sia <c>eval validation</c>.
/// </summary>
public static partial class GoldenValidationSet
{
    public const string FileName = "golden_validation.json";

    private const string CarrierPrefix = "carrier.";

    /// <summary>Le regole che devono riconoscere ciascun difetto voluto del seed (fase-3.md §5).</summary>
    public static readonly IReadOnlyDictionary<SeedDefect, IReadOnlyList<string>> RulesByDefect = new Dictionary<SeedDefect, IReadOnlyList<string>>
    {
        [SeedDefect.MissingConsigneeAddress] = ["M-CONSIGNEE-ADDRESS", "M-DELIVERY-LOCATION"],
        [SeedDefect.MissingGoodsDescription] = ["M-GOODS-DESCRIPTION"],
        [SeedDefect.MissingPackageCount] = ["M-PACKAGES"],
        [SeedDefect.WeightMismatch] = ["C-WEIGHT-SUM"],
        [SeedDefect.AdrWithoutUnNumber] = ["M-ADR-UN-NUMBER", "M-ADR-SHIPPING-NAME"],
        [SeedDefect.InvalidCountryCode] = ["F-COUNTRY"],
        [SeedDefect.DeliveryBeforePickup] = ["C-DELIVERY-BEFORE-PICKUP"]
    };

    public static IReadOnlyList<GoldenValidationCase> Load(string path)
    {
        JsonObject golden = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        return [.. golden["cases"]!.AsArray().Select(goldenCase => Build(golden, goldenCase!.AsObject()))];
    }

    /// <summary>Applica le modifiche del caso alla base: i percorsi che iniziano con <c>carrier.</c> riguardano l'anagrafica del vettore.</summary>
    private static GoldenValidationCase Build(JsonObject golden, JsonObject goldenCase)
    {
        JsonNode order = golden["bases"]![goldenCase["base"]!.GetValue<string>()]!.DeepClone();
        JsonNode carrier = JsonSerializer.SerializeToNode(new CarrierOptions(), TrasportoJson.Options)!;

        foreach ((string path, JsonNode? value) in goldenCase["set"]!.AsObject())
        {
            if (path.StartsWith(CarrierPrefix, StringComparison.Ordinal))
            {
                Set(carrier, path[CarrierPrefix.Length..], value);
            }
            else
            {
                Set(order, path, value);
            }
        }

        return new GoldenValidationCase(
            goldenCase["name"]!.GetValue<string>(),
            order.Deserialize<TransportOrderDetailView>(TrasportoJson.Options)!,
            carrier.Deserialize<CarrierOptions>(TrasportoJson.Options)!,
            [.. goldenCase["expected"]!.AsArray().Select(e => e!.GetValue<string>())]);
    }

    /// <summary>Imposta un valore seguendo un percorso come <c>lines[0].unNumber</c> o <c>trip.vehicle.plateNumber</c>.</summary>
    private static void Set(JsonNode root, string path, JsonNode? value)
    {
        string[] segments = path.Split('.');
        JsonNode current = root;

        for (int i = 0; i < segments.Length; i++)
        {
            Match segment = SegmentPattern().Match(segments[i]);
            string property = segment.Groups["name"].Value;
            bool last = i == segments.Length - 1;

            if (segment.Groups["index"].Success)
            {
                current = current[property]![int.Parse(segment.Groups["index"].Value)]
                    ?? throw new InvalidOperationException($"Percorso '{path}': elemento {segments[i]} assente.");
            }
            else if (last)
            {
                current[property] = value?.DeepClone();
            }
            else
            {
                current = current[property] ?? throw new InvalidOperationException($"Percorso '{path}': '{property}' è null nella base.");
            }
        }
    }

    [GeneratedRegex(@"^(?<name>\w+)(\[(?<index>\d+)\])?$")]
    private static partial Regex SegmentPattern();
}

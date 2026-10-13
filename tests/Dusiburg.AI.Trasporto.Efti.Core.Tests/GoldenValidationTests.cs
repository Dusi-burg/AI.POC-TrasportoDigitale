using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Serialization;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>
/// Golden set della validazione (<c>data/golden_validation.json</c>): ogni caso parte da un ordine valido, cambia alcuni campi e
/// dichiara le regole che devono scattare, tutte e sole. È codice deterministico: deve passare al 100%.
/// </summary>
public partial class GoldenValidationTests
{
    private const string CarrierPrefix = "carrier.";

    private static readonly EftiValidator Validator = EftiValidator.CreateDefault();

    private static readonly JsonObject Golden = JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "golden_validation.json")))!.AsObject();

    private static IEnumerable<TestCaseData> Cases() =>
        Golden["cases"]!.AsArray().Select(c => new TestCaseData(c!.AsObject()).SetArgDisplayNames(c["name"]!.GetValue<string>()));

    [TestCaseSource(nameof(Cases))]
    public void Evaluate_GoldenCase_FiresExactlyTheExpectedRules(JsonObject goldenCase)
    {
        //SETUP
        (TransportOrderDetailView order, CarrierOptions carrier) = Build(goldenCase);
        string[] expected = [.. goldenCase["expected"]!.AsArray().Select(e => e!.GetValue<string>())];

        //SUT
        DatasetEvaluation evaluation = Validator.Evaluate(SeedShipments.Create(order, carrier));

        Assert.That(
            evaluation.Findings.Select(f => f.RuleCode).Distinct(),
            Is.EquivalentTo(expected),
            string.Join(" | ", evaluation.Findings.Select(f => $"{f.RuleCode} {f.FieldPath}: {f.Message}")));
    }

    [Test]
    public void GoldenSet_CoversEveryRule()
    {
        //SUT
        HashSet<string> expectedSomewhere = [.. Golden["cases"]!.AsArray().SelectMany(c => c!["expected"]!.AsArray()).Select(e => e!.GetValue<string>())];

        Assert.That(expectedSomewhere, Is.EquivalentTo(ValidationRules.All.Select(r => r.Code)), "ogni regola ha almeno un caso che la fa scattare, e nessun caso cita una regola inesistente");
        Assert.That(Golden["cases"]!.AsArray(), Has.Count.GreaterThanOrEqualTo(20));
        Assert.That(Golden["cases"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()), Is.Unique);
    }

    /// <summary>Applica le modifiche del caso alla base: i percorsi che iniziano con <c>carrier.</c> riguardano l'anagrafica del vettore.</summary>
    private static (TransportOrderDetailView Order, CarrierOptions Carrier) Build(JsonObject goldenCase)
    {
        JsonNode order = Golden["bases"]![goldenCase["base"]!.GetValue<string>()]!.DeepClone();
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

        return (order.Deserialize<TransportOrderDetailView>(TrasportoJson.Options)!, carrier.Deserialize<CarrierOptions>(TrasportoJson.Options)!);
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

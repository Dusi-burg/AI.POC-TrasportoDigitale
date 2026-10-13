using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Formatting;

namespace Dusiburg.AI.Trasporto.Web.Api;

/// <summary>Una riga del riepilogo: che cos'è e quanto vale, in parole.</summary>
public sealed record SummaryLine(string Label, string Value);

/// <summary>
/// Il riepilogo di un sottoinsieme per chi lo legge su strada: gli stessi dati del JSON, con le etichette in italiano e i numeri
/// scritti all'italiana. Non aggiunge e non deduce nulla: una voce compare solo se il sottoinsieme la contiene, e il JSON resta
/// sotto come dato di riferimento.
/// </summary>
public static class SubsetSummary
{
    public static IReadOnlyList<SummaryLine> From(JsonElement dataset)
    {
        var lines = new List<SummaryLine>();

        Add(lines, "Mittente", Party(dataset, "consignor"));
        Add(lines, "Destinatario", Party(dataset, "consignee"));
        Add(lines, "Vettore", Party(dataset, "carrier"));
        Add(lines, "Presa in carico", Join(" · ", Address(Select(dataset, "carrierAcceptanceLocation")), Texts(dataset, "carrierAcceptanceDateTime")));
        Add(lines, "Consegna", Address(Select(dataset, "consigneeReceiptLocation")));
        Add(lines, "Merce", Texts(dataset, "natureIdentificationCargo", "identificationText"));
        Add(lines, "Peso lordo", Texts(dataset, "grossWeight"));
        Add(lines, "Volume lordo", Texts(dataset, "grossVolume"));
        Add(lines, "Motrice", Vehicle(Select(dataset, "mainCarriageTransportMovement", "usedTransportMeans")));
        Add(lines, "Rimorchio", Vehicle(Select(dataset, "usedTransportEquipment")));
        Add(lines, "Merci pericolose a bordo", Select(dataset, "mainCarriageTransportMovement", "dangerousGoodsIndicator").Select(YesNo).FirstOrDefault(text => text is not null));

        foreach (JsonElement goods in Select(dataset, "includedConsignmentItem", "transportDangerousGoods"))
        {
            Add(lines, "Merce pericolosa", DangerousGoods(goods));
        }

        return lines;
    }

    private static string? DangerousGoods(JsonElement goods)
    {
        string? quantity = Texts(goods, "dangerousGoodsLogisticsPackage", "itemQuantity");
        string? packageType = Texts(goods, "dangerousGoodsLogisticsPackage", "typeCode");
        string? packages = quantity is null ? null : $"{quantity} {(quantity == "1" ? "collo" : "colli")}" + (packageType is null ? "" : $" (tipo {packageType})");

        return Join(", ",
            Prefixed("UN ", Texts(goods, "uNDGID")),
            Texts(goods, "properShippingName"),
            Prefixed("classe ", Texts(goods, "hazardClassificationID")),
            Prefixed("gruppo di imballaggio ", PackingGroup(Texts(goods, "packagingDangerLevelCode"))),
            packages,
            Texts(goods, "grossWeight"));
    }

    /// <summary>Il gruppo di imballaggio come si scrive sui documenti ADR; un codice non previsto resta com'è.</summary>
    private static string? PackingGroup(string? code) => code switch
    {
        "1" => "I",
        "2" => "II",
        "3" => "III",
        _ => code
    };

    private static string? Party(JsonElement dataset, string name) =>
        Join(" — ", Texts(dataset, name, "name"), Address(Select(dataset, name)));

    private static string? Address(IEnumerable<JsonElement> owners)
    {
        JsonElement[] found = [.. owners];

        if (found.Length == 0)
        {
            return null;
        }

        JsonElement owner = found[0];
        string? city = Join(" ", Texts(owner, "postalAddress", "postcode"), Texts(owner, "postalAddress", "cityName"));
        string? country = Texts(owner, "postalAddress", "countryCode");

        return Join(", ", Texts(owner, "postalAddress", "streetName"), country is null ? city : Join(" ", city, $"({country})"));
    }

    private static string? Vehicle(IEnumerable<JsonElement> vehicles) => Join("; ", [.. vehicles.Select(vehicle =>
        Texts(vehicle, "registrationCountry", "code") is { } country ? Join(" ", Texts(vehicle, "id"), $"({country})") : Texts(vehicle, "id"))]);

    private static void Add(List<SummaryLine> lines, string label, string? value)
    {
        if (value is not null)
        {
            lines.Add(new SummaryLine(label, value));
        }
    }

    private static string? YesNo(JsonElement flag) => flag.ValueKind switch
    {
        JsonValueKind.True => "sì",
        JsonValueKind.False => "no",
        _ => null
    };

    private static string? Prefixed(string prefix, string? value) => value is null ? null : prefix + value;

    private static string? Join(string separator, params string?[] parts)
    {
        string[] present = [.. parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!)];

        return present.Length == 0 ? null : string.Join(separator, present);
    }

    /// <summary>I valori in fondo a un percorso, uniti; misure e momenti nel loro testo per una persona.</summary>
    private static string? Texts(JsonElement root, params string[] path) => Join(", ", [.. Select(root, path).Select(element => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.Object => ReadableValues.TextOf(element),
        _ => null
    })]);

    /// <summary>Gli elementi in fondo a un percorso di nomi; a ogni passo un array vale per i suoi elementi.</summary>
    private static IEnumerable<JsonElement> Select(JsonElement root, params string[] path)
    {
        IEnumerable<JsonElement> current = Flatten(root);

        foreach (string name in path)
        {
            current = current
                .Where(element => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _))
                .SelectMany(element => Flatten(element.GetProperty(name)));
        }

        return current;
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().SelectMany(Flatten) : [element];
}

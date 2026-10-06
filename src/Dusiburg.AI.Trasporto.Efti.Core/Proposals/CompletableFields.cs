using System.Globalization;
using System.Text.RegularExpressions;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Configurations;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Proposals;

/// <summary>Che tipo di valore accetta un campo completabile: decide il controllo e la forma in cui il valore si salva.</summary>
public enum FieldValueKind
{
    Text,
    Country,
    VatNumber,
    PlateNumber,
    Date,
    PositiveInteger,
    PositiveDecimal,
    UnNumber,
    PackingGroup
}

/// <summary>
/// Un campo del dataset che una proposta può completare, dichiarato con ciò che serve a spiegarlo e a scriverlo sulla spedizione.
/// </summary>
/// <param name="Path">Percorso nel dataset eFTI, come nei problemi di validazione; <c>[n]</c> è l'indice della riga di merce.</param>
/// <param name="MaxLength">Lunghezza massima del testo; 0 se il tipo ha già una forma fissa.</param>
/// <param name="Read">Valore attuale; <c>null</c> se manca.</param>
/// <param name="Write">Scrive il valore già controllato.</param>
/// <param name="Unavailable">Perché il campo non si può completare su questa spedizione; <c>null</c> se si può.</param>
public sealed record CompletableField(
    string Path,
    string Description,
    FieldValueKind Kind,
    int MaxLength,
    Func<Shipment, int, string?> Read,
    Action<Shipment, int, string> Write,
    Func<Shipment, int, string?> Unavailable)
{
    public bool IsPerItem => Path.Contains(CompletableFields.ItemIndexPlaceholder, StringComparison.Ordinal);
}

/// <summary>Un campo completabile trovato su una spedizione: il percorso in forma canonica e la riga a cui si riferisce.</summary>
public sealed record FieldTarget(CompletableField Field, string CanonicalPath, int ItemIndex)
{
    public string? Read(Shipment shipment) => Field.Read(shipment, ItemIndex);

    public void Write(Shipment shipment, string normalizedValue) => Field.Write(shipment, ItemIndex, normalizedValue);
}

/// <summary>
/// I campi che una proposta può completare, come elenco dichiarativo (P9): ciò che non è elencato non si propone. È codice
/// deterministico a decidere se un campo esiste e se un valore è accettabile; il modello propone soltanto.
/// </summary>
public static partial class CompletableFields
{
    public const string ItemIndexPlaceholder = "[n]";

    private const string DateFormat = "yyyy-MM-dd";

    public static IReadOnlyList<CompletableField> All { get; } =
    [
        .. PartyFields("consignor", "mittente", PartyRole.Consignor),
        .. PartyFields("consignee", "destinatario", PartyRole.Consignee),
        .. LocationFields("carrierAcceptanceLocation", "luogo di presa in carico", s => s.Pickup),
        .. LocationFields("consigneeReceiptLocation", "luogo di consegna", s => s.Delivery),

        Header("carrierAcceptanceDateTime", "Data di presa in carico prevista (yyyy-MM-dd).", FieldValueKind.Date,
            s => FormatDate(s.PickupDate), (s, v) => s.PickupDate = ParseDate(v)),
        Header("deliveryEvent/scheduledOccurrenceDateTime", "Data di consegna pattuita (yyyy-MM-dd).", FieldValueKind.Date,
            s => FormatDate(s.DeliveryDate), (s, v) => s.DeliveryDate = ParseDate(v)),
        Header("numberOfPackages", "Numero totale di colli.", FieldValueKind.PositiveInteger,
            s => s.TotalPackages?.ToString(CultureInfo.InvariantCulture), (s, v) => s.TotalPackages = int.Parse(v, CultureInfo.InvariantCulture)),
        Header("grossWeight", "Peso lordo totale in kg, con il punto come separatore decimale.", FieldValueKind.PositiveDecimal,
            s => FormatDecimal(s.TotalGrossWeightKg), (s, v) => s.TotalGrossWeightKg = decimal.Parse(v, CultureInfo.InvariantCulture)),
        Header("transportContractDocument/id", "Numero della lettera di vettura.", FieldValueKind.Text,
            s => s.CmrNumber, (s, v) => s.CmrNumber = v, ColumnSizes.CmrNumber),
        Header("mainCarriageTransportMovement[0]/usedTransportMeans/id", "Targa del mezzo.", FieldValueKind.PlateNumber,
            s => s.VehiclePlateNumber, (s, v) => s.VehiclePlateNumber = v),
        Header("mainCarriageTransportMovement[0]/usedTransportMeans/registrationCountry/code", "Paese di immatricolazione del mezzo.", FieldValueKind.Country,
            s => s.VehicleCountryCode, (s, v) => s.VehicleCountryCode = v),
        Header("usedTransportEquipment[0]/id", "Targa del rimorchio.", FieldValueKind.PlateNumber,
            s => s.TrailerPlateNumber, (s, v) => s.TrailerPlateNumber = v),
        Header("usedTransportEquipment[0]/registrationCountry/code", "Paese di immatricolazione del rimorchio.", FieldValueKind.Country,
            s => s.TrailerCountryCode, (s, v) => s.TrailerCountryCode = v),

        Item("transportPackage[n]/itemQuantity", "Colli della riga di merce.", FieldValueKind.PositiveInteger,
            i => i.PackageCount?.ToString(CultureInfo.InvariantCulture), (i, v) => i.PackageCount = int.Parse(v, CultureInfo.InvariantCulture)),
        DangerousGoods("uNDGID", "Numero ONU, quattro cifre senza prefisso.", FieldValueKind.UnNumber,
            i => i.UnNumber, (i, v) => i.UnNumber = v),
        DangerousGoods("properShippingName", "Denominazione ufficiale di trasporto.", FieldValueKind.Text,
            i => i.ProperShippingName, (i, v) => i.ProperShippingName = v, ColumnSizes.ProperShippingName),
        DangerousGoods("hazardClassificationID", "Classe ADR.", FieldValueKind.Text,
            i => i.AdrClass, (i, v) => i.AdrClass = v, ColumnSizes.AdrClass),
        DangerousGoods("packagingDangerLevelCode", "Gruppo di imballaggio: I, II o III.", FieldValueKind.PackingGroup,
            i => i.PackingGroup, (i, v) => i.PackingGroup = v)
    ];

    private static readonly Dictionary<string, CompletableField> ByKey = All.ToDictionary(f => KeyOf(f.Path), StringComparer.Ordinal);

    /// <summary>
    /// Trova il campo su una spedizione. Il percorso è quello dei problemi di validazione: l'indice <c>[0]</c> degli elementi
    /// che nel POC non si ripetono è facoltativo, quello della riga di merce conta.
    /// </summary>
    /// <param name="error">Perché il campo non si può completare; <c>null</c> se trovato.</param>
    public static FieldTarget? Resolve(Shipment shipment, string? fieldPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(fieldPath) || !TryParsePath(fieldPath.Trim(), out string key, out int itemIndex))
        {
            error = $"Percorso del campo non valido. Campi completabili: {string.Join(", ", All.Select(f => f.Path))}.";

            return null;
        }

        if (!ByKey.TryGetValue(key, out CompletableField? field))
        {
            error = $"Il campo '{fieldPath}' non è completabile. Campi completabili: {string.Join(", ", All.Select(f => f.Path))}.";

            return null;
        }

        if (field.IsPerItem && (itemIndex < 0 || itemIndex >= shipment.Items.Count))
        {
            error = $"La spedizione ha {shipment.Items.Count} righe di merce: l'indice {itemIndex} non esiste (si parte da 0).";

            return null;
        }

        error = field.Unavailable(shipment, itemIndex);

        return error is null
            ? new FieldTarget(field, field.Path.Replace(ItemIndexPlaceholder, $"[{itemIndex}]", StringComparison.Ordinal), itemIndex)
            : null;
    }

    /// <summary>
    /// Controlla il valore proposto e lo porta nella forma in cui si salva (spazi tolti, codici in maiuscolo).
    /// </summary>
    /// <param name="error">Perché il valore non è accettabile; <c>null</c> se lo è.</param>
    public static string? Normalize(CompletableField field, string? value, EftiSchemaCatalog catalog, out string? error)
    {
        error = null;

        string text = value?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            error = "Il valore proposto è vuoto.";

            return null;
        }

        string upper = text.ToUpperInvariant();

        (string normalized, string? problem) = field.Kind switch
        {
            FieldValueKind.Country => (upper, catalog.CountryCodes.Contains(upper) ? null : "serve un codice paese ISO a due lettere"),
            FieldValueKind.VatNumber => (upper, Formats.IsVatNumber(upper) ? null : "partita IVA non valida: prefisso del paese e forma plausibile"),
            FieldValueKind.PlateNumber => (upper, Formats.IsPlateNumber(upper) ? null : "targa non valida: da 5 a 10 lettere e cifre"),
            FieldValueKind.UnNumber => (text, Formats.IsUnNumber(text) ? null : "il numero ONU ha quattro cifre, senza prefisso"),
            FieldValueKind.PackingGroup => (upper, Formats.IsPackingGroup(upper) ? null : "il gruppo di imballaggio è I, II o III"),
            FieldValueKind.Date => (text, DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? null : $"la data va scritta come {DateFormat}"),
            FieldValueKind.PositiveInteger => (text, PositiveIntegerPattern().IsMatch(text) && int.TryParse(text, CultureInfo.InvariantCulture, out int number) && number > 0
                ? null : "serve un numero intero maggiore di zero"),
            FieldValueKind.PositiveDecimal => (text, PositiveDecimalPattern().IsMatch(text) && decimal.Parse(text, CultureInfo.InvariantCulture) > 0
                ? null : "serve un numero maggiore di zero, con il punto come separatore decimale e senza separatore delle migliaia"),
            _ => (text, text.Length <= field.MaxLength ? null : $"al massimo {field.MaxLength} caratteri")
        };

        if (problem is not null)
        {
            error = $"Valore '{Shorten(text)}' non accettabile per {field.Path}: {problem}.";

            return null;
        }

        // I numeri si salvano in una forma sola: "08" e "8", "8840.0" e "8840" sono la stessa proposta.
        return field.Kind switch
        {
            FieldValueKind.PositiveInteger => int.Parse(normalized, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            FieldValueKind.PositiveDecimal => FormatDecimal(decimal.Parse(normalized, CultureInfo.InvariantCulture)),
            _ => normalized
        };
    }

    /// <summary><c>includedConsignmentItem[1]/transportDangerousGoods[0]/uNDGID</c> → chiave senza indici e indice della riga.</summary>
    private static bool TryParsePath(string path, out string key, out int itemIndex)
    {
        key = string.Empty;
        itemIndex = 0;

        List<string> names = [];

        foreach (string segment in path.Split('/'))
        {
            Match match = SegmentPattern().Match(segment);

            if (!match.Success)
            {
                return false;
            }

            string name = match.Groups["name"].Value;
            int index = match.Groups["index"].Success ? int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture) : 0;

            if (name is "includedConsignmentItem" or "transportPackage")
            {
                itemIndex = index;
            }
            else if (index != 0)
            {
                return false;
            }

            names.Add(name);
        }

        key = string.Join('/', names);

        return true;
    }

    private static string KeyOf(string path) => IndexPattern().Replace(path, string.Empty);

    private static IEnumerable<CompletableField> PartyFields(string element, string label, PartyRole role)
    {
        Party? Of(Shipment shipment) =>
            shipment.Parties.Where(p => p.Role == role).OrderBy(p => p.Sequence).Select(p => p.Party).FirstOrDefault();

        CompletableField Field(string path, string description, FieldValueKind kind, int maxLength, Func<Party, string?> read, Action<Party, string> write) =>
            new($"{element}/{path}", $"{description} del {label}.", kind, maxLength,
                (shipment, _) => Of(shipment) is { } party ? Present(read(party)) : null,
                (shipment, _, value) => write(Of(shipment)!, value),
                (shipment, _) => Of(shipment) is null ? $"La spedizione non ha un {label}." : null);

        yield return Field("name", "Nome", FieldValueKind.Text, ColumnSizes.Name, p => p.Name, (p, v) => p.Name = v);
        yield return Field("postalAddress/streetName", "Via", FieldValueKind.Text, ColumnSizes.Street, p => p.Street, (p, v) => p.Street = v);
        yield return Field("postalAddress/postcode", "CAP", FieldValueKind.Text, ColumnSizes.PostalCode, p => p.PostalCode, (p, v) => p.PostalCode = v);
        yield return Field("postalAddress/cityName", "Città", FieldValueKind.Text, ColumnSizes.City, p => p.City, (p, v) => p.City = v);
        yield return Field("postalAddress/countryCode", "Paese", FieldValueKind.Country, 0, p => p.CountryCode, (p, v) => p.CountryCode = v);
        yield return Field("taxRegistration/id", "Partita IVA", FieldValueKind.VatNumber, 0, p => p.VatNumber, (p, v) => p.VatNumber = v);
    }

    private static IEnumerable<CompletableField> LocationFields(string element, string label, Func<Shipment, Location> of)
    {
        CompletableField Field(string path, string description, FieldValueKind kind, int maxLength, Func<Location, string?> read, Action<Location, string> write) =>
            new($"{element}/postalAddress/{path}", $"{description} del {label}.", kind, maxLength,
                (shipment, _) => Present(read(of(shipment))),
                (shipment, _, value) => write(of(shipment), value),
                (_, _) => null);

        yield return Field("streetName", "Via", FieldValueKind.Text, ColumnSizes.Street, l => l.Street, (l, v) => l.Street = v);
        yield return Field("postcode", "CAP", FieldValueKind.Text, ColumnSizes.PostalCode, l => l.PostalCode, (l, v) => l.PostalCode = v);
        yield return Field("cityName", "Città", FieldValueKind.Text, ColumnSizes.City, l => l.City, (l, v) => l.City = v);
        yield return Field("countryCode", "Paese", FieldValueKind.Country, 0, l => l.CountryCode, (l, v) => l.CountryCode = v);
    }

    private static CompletableField Header(
        string path, string description, FieldValueKind kind, Func<Shipment, string?> read, Action<Shipment, string> write, int maxLength = 0) =>
        new(path, description, kind, maxLength, (shipment, _) => Present(read(shipment)), (shipment, _, value) => write(shipment, value), (_, _) => null);

    private static CompletableField Item(
        string path, string description, FieldValueKind kind, Func<ConsignmentItem, string?> read, Action<ConsignmentItem, string> write,
        int maxLength = 0, Func<ConsignmentItem, string?>? unavailable = null) =>
        new(path, description, kind, maxLength,
            (shipment, index) => Present(read(ItemAt(shipment, index))),
            (shipment, index, value) => write(ItemAt(shipment, index), value),
            (shipment, index) => unavailable?.Invoke(ItemAt(shipment, index)));

    private static CompletableField DangerousGoods(
        string element, string description, FieldValueKind kind, Func<ConsignmentItem, string?> read, Action<ConsignmentItem, string> write, int maxLength = 0) =>
        Item($"includedConsignmentItem[n]/transportDangerousGoods[0]/{element}", description, kind, read, write, maxLength,
            item => item.IsDangerousGoods ? null : $"La riga {item.SequenceNumber} non è merce pericolosa: non ha dati ADR da completare.");

    /// <summary>La riga nella posizione che usano i problemi di validazione: in ordine di numero di sequenza, da 0.</summary>
    private static ConsignmentItem ItemAt(Shipment shipment, int index) => shipment.Items.OrderBy(i => i.SequenceNumber).ElementAt(index);

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FormatDate(DateOnly? date) => date?.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

    private static string? FormatDecimal(decimal? value) => value?.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";

    [GeneratedRegex(@"^(?<name>[A-Za-z]+)(\[(?<index>\d{1,4})\])?$")]
    private static partial Regex SegmentPattern();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex IndexPattern();

    [GeneratedRegex(@"^\d{1,9}$")]
    private static partial Regex PositiveIntegerPattern();

    [GeneratedRegex(@"^\d{1,9}(\.\d{1,3})?$")]
    private static partial Regex PositiveDecimalPattern();
}

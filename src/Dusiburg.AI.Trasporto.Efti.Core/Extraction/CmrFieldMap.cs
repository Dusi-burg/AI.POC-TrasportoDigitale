using System.Globalization;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Grounding;

namespace Dusiburg.AI.Trasporto.Efti.Core.Extraction;

/// <summary>Un valore letto da un documento, portato sul campo del dataset a cui corrisponde.</summary>
/// <param name="FieldPath">Percorso del campo nel dataset eFTI, come in <c>CompletableFields</c>.</param>
/// <param name="CmrBox">Casella del CMR (modello IRU) da cui viene; <c>null</c> se non è una casella numerata.</param>
public sealed record ExtractedValue(string FieldPath, string? CmrBox, string Label, string Value);

/// <summary>
/// Dal CMR letto dal modello ai campi del dataset: quale casella alimenta quale campo. È l'unico punto che conosce entrambe le
/// forme. Una casella vuota non produce nulla; le righe di merce corrispondono alle righe della spedizione nello stesso ordine.
/// </summary>
public static class CmrFieldMap
{
    public static IReadOnlyList<ExtractedValue> ToFields(CmrExtraction extraction)
    {
        List<ExtractedValue> fields = [];

        void Add(string path, string? box, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields.Add(new ExtractedValue(path, box, label, value.Trim()));
            }
        }

        Add("transportContractDocument/id", null, "Numero della lettera di vettura", extraction.CmrNumber);

        AddParty(Add, "consignor", "1", "mittente", extraction.Sender);
        AddParty(Add, "consignee", "2", "destinatario", extraction.Consignee);
        AddPlace(Add, "consigneeReceiptLocation", "3", "luogo di consegna", extraction.PlaceOfDelivery);
        AddPlace(Add, "carrierAcceptanceLocation", "4", "luogo di presa in carico", extraction.PlaceOfTakingOver);

        Add("numberOfPackages", "7", "Totale colli", Number(extraction.TotalPackages));
        Add("grossWeight", "11", "Peso lordo totale (kg)", Weight(extraction.TotalGrossWeight));

        for (int index = 0; index < (extraction.Goods?.Count ?? 0); index++)
        {
            CmrGoods line = extraction.Goods![index];
            string dangerousGoods = $"includedConsignmentItem[{index}]/transportDangerousGoods[0]";
            string row = $"riga {index + 1}";

            Add($"transportPackage[{index}]/itemQuantity", "7", $"Colli, {row}", Number(line.PackageCount));
            Add($"{dangerousGoods}/uNDGID", "9", $"Numero ONU, {row}", line.UnNumber);
            Add($"{dangerousGoods}/properShippingName", "9", $"Denominazione ufficiale di trasporto, {row}", line.ProperShippingName);
            Add($"{dangerousGoods}/hazardClassificationID", "9", $"Classe ADR, {row}", line.AdrClass);
            Add($"{dangerousGoods}/packagingDangerLevelCode", "9", $"Gruppo di imballaggio, {row}", line.PackingGroup);
        }

        Add("mainCarriageTransportMovement[0]/usedTransportMeans/id", null, "Targa della motrice", extraction.VehiclePlate);
        Add("usedTransportEquipment[0]/id", null, "Targa del rimorchio", extraction.TrailerPlate);

        return fields;
    }

    private static void AddParty(Action<string, string?, string, string?> add, string element, string box, string label, CmrParty? party)
    {
        if (party is null)
        {
            return;
        }

        add($"{element}/name", box, $"Nome del {label}", party.Name);
        add($"{element}/postalAddress/streetName", box, $"Via del {label}", party.Street);
        add($"{element}/postalAddress/postcode", box, $"CAP del {label}", party.PostalCode);
        add($"{element}/postalAddress/cityName", box, $"Città del {label}", party.City);
        add($"{element}/postalAddress/countryCode", box, $"Paese del {label}", party.CountryCode);
        add($"{element}/taxRegistration/id", box, $"Partita IVA del {label}", party.VatNumber);
    }

    private static void AddPlace(Action<string, string?, string, string?> add, string element, string box, string label, CmrPlace? place)
    {
        if (place is null)
        {
            return;
        }

        add($"{element}/postalAddress/streetName", box, $"Via del {label}", place.Street);
        add($"{element}/postalAddress/postcode", box, $"CAP del {label}", place.PostalCode);
        add($"{element}/postalAddress/cityName", box, $"Città del {label}", place.City);
        add($"{element}/postalAddress/countryCode", box, $"Paese del {label}", place.CountryCode);
    }

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Il peso arriva come il modello l'ha copiato dal documento, e qui si legge all'italiana: "8.840" e "8.840 kg" sono 8840.
    /// Un testo che non è un solo numero ("lordo 2.360 / netto 2.180") resta com'è: non supererà il controllo di formato e
    /// lo vedrà una persona.
    /// </summary>
    private static string? Weight(string? written)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        string text = written.Trim();
        string number = text.EndsWith("kg", StringComparison.OrdinalIgnoreCase) ? text[..^2].TrimEnd() : text;

        return number.Length > 0 && number.All(c => char.IsAsciiDigit(c) || c is '.' or ',') && SourceGrounding.ReadNumberInText(number) is { } value
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : text;
    }
}

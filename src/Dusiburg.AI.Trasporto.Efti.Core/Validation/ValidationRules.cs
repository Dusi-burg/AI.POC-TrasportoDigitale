using System.Globalization;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Validation;

/// <summary>
/// Le regole di validazione del dataset eFTI, come elenco dichiarativo (P9). Ogni regola dice cosa controlla e perché; da qui
/// nasce <c>docs/regole-di-validazione.md</c>, tenuto allineato da un test. Aggiungere una regola significa aggiungere una voce.
/// </summary>
public static class ValidationRules
{
    /// <summary>Scarto ammesso fra il peso lordo dichiarato e la somma delle righe.</summary>
    public const decimal WeightTolerance = 0.02m;

    /// <summary>Codice della regola di conformità allo schema: la applica <see cref="EftiValidator"/>, non una funzione di controllo.</summary>
    public const string SchemaRuleCode = "X-SCHEMA";

    public static IReadOnlyList<ValidationRule> All { get; } =
    [
        Mandatory("M-CONSIGNOR-NAME", "consignor/name",
            "Il mittente deve avere un nome.",
            c => MissingName("consignor", c.Consignor)),
        Mandatory("M-CONSIGNOR-ADDRESS", "consignor/postalAddress",
            "L'indirizzo del mittente deve avere città, CAP e paese.",
            c => MissingAddress("consignor", c.Consignor)),
        Mandatory("M-CONSIGNEE-NAME", "consignee/name",
            "Il destinatario deve avere un nome.",
            c => MissingName("consignee", c.Consignee)),
        Mandatory("M-CONSIGNEE-ADDRESS", "consignee/postalAddress",
            "L'indirizzo del destinatario deve avere città, CAP e paese.",
            c => MissingAddress("consignee", c.Consignee)),
        Mandatory("M-CARRIER-NAME", "carrier/name",
            "Il vettore deve avere un nome: viene dalla configurazione della piattaforma.",
            c => MissingName("carrier", c.Carrier)),
        Mandatory("M-PICKUP-LOCATION", "carrierAcceptanceLocation/postalAddress",
            "Il luogo di presa in carico deve avere città e paese.",
            c => MissingPlace("carrierAcceptanceLocation", c.Shipment.Pickup)),
        Mandatory("M-DELIVERY-LOCATION", "consigneeReceiptLocation/postalAddress",
            "Il luogo di consegna deve avere città e paese.",
            c => MissingPlace("consigneeReceiptLocation", c.Shipment.Delivery)),
        Mandatory("M-PICKUP-DATE", "carrierAcceptanceDateTime",
            "Serve la data di presa in carico, effettiva o prevista.",
            c => When(c.PickupDay is null, "carrierAcceptanceDateTime", "Data di presa in carico mancante.")),
        Mandatory("M-DELIVERY-DATE", "deliveryEvent/scheduledOccurrenceDateTime",
            "Serve la data di consegna pattuita.",
            c => When(c.Shipment.DeliveryDate is null, "deliveryEvent/scheduledOccurrenceDateTime", "Data di consegna pattuita mancante.")),
        Mandatory("M-GOODS-DESCRIPTION", "natureIdentificationCargo/identificationText",
            "Ogni riga di merce deve dire che cosa si trasporta.",
            MissingGoodsDescription),
        Mandatory("M-PACKAGES", "numberOfPackages, transportPackage/itemQuantity",
            "Il numero di colli deve essere maggiore di zero, in totale e su ogni riga.",
            MissingPackages),
        Mandatory("M-GROSS-WEIGHT", "grossWeight",
            "Il peso lordo della spedizione deve essere maggiore di zero.",
            c => When(c.Shipment.TotalGrossWeightKg is not > 0, "grossWeight", "Peso lordo mancante o a zero.")),
        Mandatory("M-VEHICLE", "mainCarriageTransportMovement/usedTransportMeans",
            "Una spedizione già presa in carico deve indicare targa e paese del mezzo.",
            MissingVehicle),
        Mandatory("M-ADR-UN-NUMBER", "includedConsignmentItem/transportDangerousGoods/uNDGID",
            "Una riga di merce pericolosa deve avere il numero ONU.",
            c => DangerousItems(c, i => Blank(i.UnNumber), "uNDGID", "numero ONU mancante")),
        Mandatory("M-ADR-SHIPPING-NAME", "includedConsignmentItem/transportDangerousGoods/properShippingName",
            "Una riga di merce pericolosa deve avere la denominazione ufficiale di trasporto.",
            c => DangerousItems(c, i => Blank(i.ProperShippingName), "properShippingName", "denominazione ufficiale di trasporto mancante")),
        Mandatory("M-ADR-CLASS", "includedConsignmentItem/transportDangerousGoods/hazardClassificationID",
            "Una riga di merce pericolosa deve avere la classe ADR.",
            c => DangerousItems(c, i => Blank(i.AdrClass), "hazardClassificationID", "classe ADR mancante")),

        Format("F-COUNTRY", "postalAddress/countryCode, registrationCountry/code",
            "Ogni paese deve essere un codice ISO 3166-1 a due lettere presente nella lista dello schema eFTI.",
            UnknownCountries),
        Format("F-VAT", "taxRegistration/id",
            "Una partita IVA, se presente, deve avere il prefisso del paese e una forma plausibile; per l'Italia anche la cifra di controllo.",
            c => c.Parties()
                .Where(p => !Blank(p.Party.VatNumber) && !Formats.IsVatNumber(p.Party.VatNumber!))
                .Select(p => new RuleViolation($"{p.Path}/taxRegistration/id", $"Partita IVA '{p.Party.VatNumber}' non valida."))),
        Format("F-PLATE", "usedTransportMeans/id, usedTransportEquipment/id",
            "Una targa, se presente, deve avere da 5 a 10 lettere e cifre.",
            InvalidPlates),
        Format("F-UN-NUMBER", "includedConsignmentItem/transportDangerousGoods/uNDGID",
            "Il numero ONU, se presente, deve avere quattro cifre.",
            c => Items(c, i => !Blank(i.UnNumber) && !Formats.IsUnNumber(i.UnNumber!),
                "transportDangerousGoods/uNDGID", i => $"numero ONU '{i.UnNumber}' non valido")),
        Format("F-PACKING-GROUP", "includedConsignmentItem/transportDangerousGoods/packagingDangerLevelCode",
            "Il gruppo di imballaggio, se presente, deve essere I, II o III.",
            c => Items(c, i => !Blank(i.PackingGroup) && !Formats.IsPackingGroup(i.PackingGroup!),
                "transportDangerousGoods/packagingDangerLevelCode", i => $"gruppo di imballaggio '{i.PackingGroup}' non valido")),

        Coherence("C-WEIGHT-SUM", "grossWeight",
            string.Create(CultureInfo.InvariantCulture,
                $"Il peso lordo dichiarato deve coincidere con la somma dei pesi delle righe, con una tolleranza del {WeightTolerance * 100:0}%."),
            WeightMismatch),
        Coherence("C-PACKAGES-SUM", "numberOfPackages",
            "Il numero di colli dichiarato deve coincidere con la somma dei colli delle righe.",
            PackagesMismatch),
        Coherence("C-DELIVERY-BEFORE-PICKUP", "deliveryEvent/scheduledOccurrenceDateTime",
            "La data di consegna pattuita non può precedere la presa in carico.",
            c => When(c.Shipment.DeliveryDate < c.PickupDay, "deliveryEvent/scheduledOccurrenceDateTime",
                string.Create(CultureInfo.InvariantCulture, $"Consegna pattuita il {c.Shipment.DeliveryDate:yyyy-MM-dd}, presa in carico il {c.PickupDay:yyyy-MM-dd}."))),
        Coherence("C-DELIVERED-BEFORE-PICKED-UP", "deliveryEvent/actualOccurrenceDateTime",
            "La consegna effettiva non può precedere la presa in carico effettiva.",
            c => When(c.Shipment.DeliveredAt < c.Shipment.PickedUpAt, "deliveryEvent/actualOccurrenceDateTime",
                "La consegna effettiva precede la presa in carico effettiva.")),

        Advisory("W-VEHICLE-NOT-ASSIGNED", "mainCarriageTransportMovement/usedTransportMeans",
            "Una spedizione non ancora partita senza mezzo assegnato: il dato servirà alla presa in carico.",
            c => When(c.Shipment.PickedUpAt is null && Blank(c.Shipment.VehiclePlateNumber),
                "mainCarriageTransportMovement/usedTransportMeans/id", "Mezzo non ancora assegnato.")),
        Advisory("W-PARTY-VAT", "consignor/taxRegistration/id, consignee/taxRegistration/id",
            "Mittente e destinatario dovrebbero avere la partita IVA: non è nei sottoinsiemi dell'autorità ma serve alla lettera di vettura.",
            c => new[] { ("consignor", c.Consignor), ("consignee", c.Consignee) }
                .Where(p => p.Item2 is not null && Blank(p.Item2.VatNumber))
                .Select(p => new RuleViolation($"{p.Item1}/taxRegistration/id", "Partita IVA mancante."))),
        Advisory("W-CMR-NUMBER", "transportContractDocument/id",
            "Un trasporto internazionale già preso in carico dovrebbe avere il numero della lettera di vettura.",
            MissingCmrNumber),

        new(SchemaRuleCode, RuleCategory.Schema, IssueSeverity.Error, "(tutto il dataset)",
            "L'XML esportato deve essere conforme allo schema XSD eFTI. Si controlla solo se nessun'altra regola bloccante è violata: " +
            "intercetta ciò che le regole non prevedono, per esempio un testo più lungo del formato ammesso.",
            _ => [])
    ];

    private static ValidationRule Mandatory(string code, string field, string explanation, Func<ValidationContext, IEnumerable<RuleViolation>> check) =>
        new(code, RuleCategory.Mandatory, IssueSeverity.Error, field, explanation, check);

    private static ValidationRule Format(string code, string field, string explanation, Func<ValidationContext, IEnumerable<RuleViolation>> check) =>
        new(code, RuleCategory.Format, IssueSeverity.Error, field, explanation, check);

    private static ValidationRule Coherence(string code, string field, string explanation, Func<ValidationContext, IEnumerable<RuleViolation>> check) =>
        new(code, RuleCategory.Coherence, IssueSeverity.Error, field, explanation, check);

    private static ValidationRule Advisory(string code, string field, string explanation, Func<ValidationContext, IEnumerable<RuleViolation>> check) =>
        new(code, RuleCategory.Advisory, IssueSeverity.Warning, field, explanation, check);

    private static IEnumerable<RuleViolation> When(bool violated, string fieldPath, string message) =>
        violated ? [new RuleViolation(fieldPath, message)] : [];

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static IEnumerable<RuleViolation> MissingName(string path, Party? party) =>
        When(party is null || Blank(party.Name), $"{path}/name", "Nome mancante.");

    private static IEnumerable<RuleViolation> MissingAddress(string path, Party? party)
    {
        if (party is null)
        {
            return [new RuleViolation($"{path}/postalAddress", "Indirizzo mancante.")];
        }

        return
        [
            .. When(Blank(party.City), $"{path}/postalAddress/cityName", "Città mancante."),
            .. When(Blank(party.PostalCode), $"{path}/postalAddress/postcode", "CAP mancante."),
            .. When(Blank(party.CountryCode), $"{path}/postalAddress/countryCode", "Paese mancante.")
        ];
    }

    private static IEnumerable<RuleViolation> MissingPlace(string path, Location location) =>
    [
        .. When(Blank(location.City), $"{path}/postalAddress/cityName", "Città mancante."),
        .. When(Blank(location.CountryCode), $"{path}/postalAddress/countryCode", "Paese mancante.")
    ];

    private static IEnumerable<RuleViolation> MissingGoodsDescription(ValidationContext context) =>
        context.Items.Count == 0
            ? [new RuleViolation("natureIdentificationCargo/identificationText", "La spedizione non ha righe di merce.")]
            : Items(context, i => Blank(i.Description), null, _ => "descrizione della merce mancante")
                .Select(v => v with { FieldPath = "natureIdentificationCargo/identificationText" });

    private static IEnumerable<RuleViolation> MissingPackages(ValidationContext context) =>
    [
        .. When(context.Shipment.TotalPackages is not > 0, "numberOfPackages", "Numero di colli mancante o a zero."),
        .. context.Items
            .Select((item, index) => (item, index))
            .Where(x => x.item.PackageCount is not > 0)
            .Select(x => new RuleViolation($"transportPackage[{x.index}]/itemQuantity", $"Riga {x.item.SequenceNumber}: colli mancanti o a zero."))
    ];

    private static IEnumerable<RuleViolation> MissingVehicle(ValidationContext context)
    {
        Shipment shipment = context.Shipment;

        if (shipment.PickedUpAt is null)
        {
            return [];
        }

        const string means = "mainCarriageTransportMovement[0]/usedTransportMeans";

        return Blank(shipment.VehiclePlateNumber)
            ? [new RuleViolation($"{means}/id", "Targa del mezzo mancante.")]
            : When(Blank(shipment.VehicleCountryCode), $"{means}/registrationCountry/code", "Paese di immatricolazione del mezzo mancante.");
    }

    private static IEnumerable<RuleViolation> DangerousItems(
        ValidationContext context, Func<ConsignmentItem, bool> violated, string element, string message) =>
        Items(context, i => i.IsDangerousGoods && violated(i), $"transportDangerousGoods[0]/{element}", _ => message);

    /// <summary>Violazioni sulle righe: il percorso porta l'indice della riga, il messaggio il suo numero.</summary>
    private static IEnumerable<RuleViolation> Items(
        ValidationContext context, Func<ConsignmentItem, bool> violated, string? element, Func<ConsignmentItem, string> message) =>
        context.Items
            .Select((item, index) => (item, index))
            .Where(x => violated(x.item))
            .Select(x => new RuleViolation(
                element is null ? $"includedConsignmentItem[{x.index}]" : $"includedConsignmentItem[{x.index}]/{element}",
                $"Riga {x.item.SequenceNumber}: {message(x.item)}."));

    private static IEnumerable<RuleViolation> UnknownCountries(ValidationContext context)
    {
        Shipment shipment = context.Shipment;

        IEnumerable<(string Path, string? Code)> codes =
        [
            .. context.Parties().Select(p => ($"{p.Path}/postalAddress/countryCode", p.Party.CountryCode)),
            ("carrierAcceptanceLocation/postalAddress/countryCode", shipment.Pickup.CountryCode),
            ("consigneeReceiptLocation/postalAddress/countryCode", shipment.Delivery.CountryCode),
            ("mainCarriageTransportMovement[0]/usedTransportMeans/registrationCountry/code", shipment.VehicleCountryCode),
            ("usedTransportEquipment[0]/registrationCountry/code", shipment.TrailerCountryCode)
        ];

        return codes
            .Where(c => !Blank(c.Code) && !context.IsKnownCountry(c.Code))
            .Select(c => new RuleViolation(c.Path, $"Paese '{c.Code}' non valido: serve un codice ISO a due lettere."));
    }

    private static IEnumerable<RuleViolation> InvalidPlates(ValidationContext context) =>
        new[]
        {
            ("mainCarriageTransportMovement[0]/usedTransportMeans/id", context.Shipment.VehiclePlateNumber),
            ("usedTransportEquipment[0]/id", context.Shipment.TrailerPlateNumber)
        }
        .Where(p => !Blank(p.Item2) && !Formats.IsPlateNumber(p.Item2!))
        .Select(p => new RuleViolation(p.Item1, $"Targa '{p.Item2}' non valida."));

    private static IEnumerable<RuleViolation> WeightMismatch(ValidationContext context)
    {
        decimal declared = context.Shipment.TotalGrossWeightKg ?? 0;
        decimal lines = context.Items.Sum(i => i.GrossWeightKg ?? 0);

        // Se uno dei due manca non c'è nulla da confrontare: lo dice la regola di obbligatorietà.
        return When(declared > 0 && lines > 0 && Math.Abs(declared - lines) > lines * WeightTolerance, "grossWeight",
            string.Create(CultureInfo.InvariantCulture, $"Peso lordo dichiarato {declared:0.###} kg, somma delle righe {lines:0.###} kg."));
    }

    private static IEnumerable<RuleViolation> PackagesMismatch(ValidationContext context)
    {
        int? declared = context.Shipment.TotalPackages;
        int lines = context.Items.Sum(i => i.PackageCount ?? 0);

        return When(declared is not null && context.Items.Count > 0 && declared != lines, "numberOfPackages",
            string.Create(CultureInfo.InvariantCulture, $"Colli dichiarati {declared}, somma delle righe {lines}."));
    }

    private static IEnumerable<RuleViolation> MissingCmrNumber(ValidationContext context)
    {
        Shipment shipment = context.Shipment;

        // Si giudica internazionale solo se entrambi i paesi sono validi: un paese sbagliato lo segnala già F-COUNTRY.
        bool international = context.IsKnownCountry(shipment.Pickup.CountryCode)
            && context.IsKnownCountry(shipment.Delivery.CountryCode)
            && shipment.Pickup.CountryCode != shipment.Delivery.CountryCode;

        return When(international && shipment.PickedUpAt is not null && Blank(shipment.CmrNumber),
            "transportContractDocument/id", "Numero della lettera di vettura mancante.");
    }
}

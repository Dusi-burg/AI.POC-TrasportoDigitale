using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Ecmr;

/// <summary>
/// Le regole del documento eCMR, come elenco dichiarativo (P9): le indicazioni che l'art. 6 della convenzione CMR chiede alla
/// lettera di vettura. Da qui nasce <c>docs/regole-ecmr.md</c>, tenuto allineato da un test. Sono regole di esempio, non
/// consulenza legale. Valgono per ogni trasporto, anche nazionale: la lettera di vettura elettronica si produce comunque, con
/// le stesse indicazioni. I percorsi sono quelli degli elementi dello schema UN/CEFACT.
/// </summary>
public static class EcmrRules
{
    /// <summary>Codice della regola di conformità allo schema: la applica <see cref="EcmrValidator"/>, non una funzione di controllo.</summary>
    public const string SchemaRuleCode = "E-SCHEMA";

    private const string Document = "ExchangedDocument";

    private const string Consignment = "SpecifiedSupplyChainConsignment";

    public static IReadOnlyList<ValidationRule> All { get; } =
    [
        Mandatory("E-NUMBER", $"{Document}/ID",
            "La lettera di vettura deve avere un numero.",
            c => When(Blank(c.Shipment.CmrNumber), $"{Document}/ID", "Numero della lettera di vettura mancante: non è ancora stata emessa.")),
        Mandatory("E-ISSUE", $"{Document}/IssueDateTime, {Document}/IssueLogisticsLocation/Name",
            "Luogo e data di emissione (art. 6, par. 1, lett. a).",
            c =>
            [
                .. When(c.Shipment.CmrIssueDate is null, $"{Document}/IssueDateTime", "Data di emissione mancante."),
                .. When(Blank(c.Shipment.CmrIssuePlace), $"{Document}/IssueLogisticsLocation/Name", "Luogo di emissione mancante.")
            ]),
        Mandatory("E-CONSIGNOR", $"{Consignment}/ConsignorTradeParty",
            "Nome e indirizzo del mittente (lett. b).",
            c => MissingParty($"{Consignment}/ConsignorTradeParty", c.Consignor)),
        Mandatory("E-CARRIER", $"{Consignment}/CarrierTradeParty",
            "Nome e indirizzo del vettore (lett. c): vengono dalla configurazione della piattaforma.",
            c => MissingParty($"{Consignment}/CarrierTradeParty", c.Carrier)),
        Mandatory("E-TAKING-OVER", $"{Consignment}/CarrierAcceptanceLogisticsLocation, {Consignment}/PickUpTransportEvent",
            "Luogo e data della presa in carico (lett. d).",
            c =>
            [
                .. MissingPlace($"{Consignment}/CarrierAcceptanceLogisticsLocation", c.Shipment.Pickup),
                .. When(c.PickupDay is null, $"{Consignment}/PickUpTransportEvent", "Data di presa in carico mancante.")
            ]),
        Mandatory("E-DELIVERY-PLACE", $"{Consignment}/ConsigneeReceiptLogisticsLocation",
            "Luogo previsto per la consegna (lett. d).",
            c => MissingPlace($"{Consignment}/ConsigneeReceiptLogisticsLocation", c.Shipment.Delivery)),
        Mandatory("E-CONSIGNEE", $"{Consignment}/ConsigneeTradeParty",
            "Nome e indirizzo del destinatario (lett. e).",
            c => MissingParty($"{Consignment}/ConsigneeTradeParty", c.Consignee)),
        Mandatory("E-GOODS", $"{Consignment}/IncludedSupplyChainConsignmentItem/NatureIdentificationTransportCargo, …/TransportLogisticsPackage/TypeCode",
            "Denominazione corrente della natura della merce e genere dell'imballaggio, per ogni riga (lett. f).",
            MissingGoods),
        Mandatory("E-PACKAGES", $"{Consignment}/IncludedSupplyChainConsignmentItem/TransportLogisticsPackage",
            "Numero dei colli, loro contrassegni e numeri, per ogni riga (lett. g).",
            MissingPackages),
        Mandatory("E-GROSS-WEIGHT", $"{Consignment}/GrossWeightMeasure",
            "Peso lordo della merce (lett. h).",
            c => When(c.Shipment.TotalGrossWeightKg is not > 0, $"{Consignment}/GrossWeightMeasure", "Peso lordo mancante o a zero.")),
        Mandatory("E-CHARGES", $"{Consignment}/ApplicableLogisticsServiceCharge",
            "Spese relative al trasporto: il nolo e chi lo paga (lett. i).",
            c =>
            [
                .. When(c.Shipment.FreightAmount is null, $"{Consignment}/ApplicableLogisticsServiceCharge[0]/AppliedAmount", "Nolo mancante."),
                .. When(c.Shipment.FreightPayment is null, $"{Consignment}/ApplicableLogisticsServiceCharge[0]/PaymentArrangementCode", "Non è indicato chi paga il nolo.")
            ]),
        Mandatory("E-DANGEROUS-GOODS", $"{Consignment}/IncludedSupplyChainConsignmentItem/ApplicableTransportDangerousGoods",
            "Per le merci pericolose, la denominazione generalmente riconosciuta: numero ONU e denominazione ufficiale di trasporto (lett. f).",
            MissingDangerousGoodsData),

        new("E-COUNTRY", RuleCategory.Format, IssueSeverity.Error, "PostalTradeAddress/CountryID, RegistrationTradeCountry/ID",
            "Ogni paese deve essere un codice ISO 3166-1 a due lettere: lo schema li elenca.",
            UnknownCountries),

        new("W-CUSTOMS", RuleCategory.Advisory, IssueSeverity.Warning, $"{Consignment}/ConsignorProvidedBorderClearanceTransportInstructions",
            "Un trasporto che tocca un paese fuori dall'Unione europea dovrebbe portare le istruzioni per la dogana e le altre formalità (lett. j).",
            c => When(
                new[] { c.Shipment.Pickup.CountryCode, c.Shipment.Delivery.CountryCode }.Any(country => c.IsKnownCountry(country) && !EuropeanUnion.IsMember(country))
                    && Blank(c.Shipment.CustomsInstructions),
                $"{Consignment}/ConsignorProvidedBorderClearanceTransportInstructions", "Istruzioni per la dogana mancanti.")),

        new(SchemaRuleCode, RuleCategory.Schema, IssueSeverity.Error, "(tutto il documento)",
            "L'XML esportato deve essere conforme allo schema XSD dell'eCMR UN/CEFACT. Si controlla solo se nessun'altra regola " +
            "bloccante è violata: intercetta ciò che le regole non prevedono, per esempio un codice fuori lista.",
            _ => [])
    ];

    private static ValidationRule Mandatory(string code, string field, string explanation, Func<ValidationContext, IEnumerable<RuleViolation>> check) =>
        new(code, RuleCategory.Mandatory, IssueSeverity.Error, field, explanation, check);

    private static IEnumerable<RuleViolation> When(bool violated, string fieldPath, string message) =>
        violated ? [new RuleViolation(fieldPath, message)] : [];

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    private static IEnumerable<RuleViolation> MissingParty(string path, Party? party)
    {
        if (party is null)
        {
            return [new RuleViolation(path, "Soggetto mancante.")];
        }

        return
        [
            .. When(Blank(party.Name), $"{path}/Name", "Nome mancante."),
            .. When(Blank(party.City), $"{path}/PostalTradeAddress/CityName", "Città mancante."),
            .. When(Blank(party.CountryCode), $"{path}/PostalTradeAddress/CountryID", "Paese mancante.")
        ];
    }

    private static IEnumerable<RuleViolation> MissingPlace(string path, Location location) =>
    [
        .. When(Blank(location.City), $"{path}/PostalTradeAddress/CityName", "Città mancante."),
        .. When(Blank(location.CountryCode), $"{path}/PostalTradeAddress/CountryID", "Paese mancante.")
    ];

    private static IEnumerable<RuleViolation> MissingGoods(ValidationContext context) =>
        context.Items.Count == 0
            ? [new RuleViolation($"{Consignment}/IncludedSupplyChainConsignmentItem", "La spedizione non ha righe di merce.")]
            :
            [
                .. Items(context, i => Blank(i.Description), "NatureIdentificationTransportCargo/Identification", "natura della merce mancante"),
                .. Items(context, i => i.PackageType is null, "TransportLogisticsPackage[0]/TypeCode", "genere dell'imballaggio mancante")
            ];

    private static IEnumerable<RuleViolation> MissingPackages(ValidationContext context) =>
    [
        .. Items(context, i => i.PackageCount is not > 0, "TransportLogisticsPackage[0]/ItemQuantity", "colli mancanti o a zero"),
        .. Items(context, i => Blank(i.Marks), "TransportLogisticsPackage[0]/PhysicalLogisticsShippingMarks", "contrassegni e numeri dei colli mancanti")
    ];

    private static IEnumerable<RuleViolation> MissingDangerousGoodsData(ValidationContext context) =>
    [
        .. Items(context, i => i.IsDangerousGoods && Blank(i.UnNumber),
            "ApplicableTransportDangerousGoods[0]/UNDGIdentificationCode", "numero ONU mancante"),
        .. Items(context, i => i.IsDangerousGoods && Blank(i.ProperShippingName),
            "ApplicableTransportDangerousGoods[0]/ProperShippingName", "denominazione ufficiale di trasporto mancante")
    ];

    /// <summary>Violazioni sulle righe: il percorso porta l'indice della riga, il messaggio il suo numero.</summary>
    private static IEnumerable<RuleViolation> Items(ValidationContext context, Func<ConsignmentItem, bool> violated, string element, string message) =>
        context.Items
            .Select((item, index) => (item, index))
            .Where(x => violated(x.item))
            .Select(x => new RuleViolation(
                $"{Consignment}/IncludedSupplyChainConsignmentItem[{x.index}]/{element}", $"Riga {x.item.SequenceNumber}: {message}."));

    private static IEnumerable<RuleViolation> UnknownCountries(ValidationContext context)
    {
        Shipment shipment = context.Shipment;

        IEnumerable<(string Path, string? Code)> codes =
        [
            .. context.Parties().Select(p => ($"{Consignment}/{PartyElement(p.Path)}/PostalTradeAddress/CountryID", p.Party.CountryCode)),
            ($"{Consignment}/CarrierAcceptanceLogisticsLocation/PostalTradeAddress/CountryID", shipment.Pickup.CountryCode),
            ($"{Consignment}/ConsigneeReceiptLogisticsLocation/PostalTradeAddress/CountryID", shipment.Delivery.CountryCode),
            ($"{Consignment}/MainCarriageLogisticsTransportMovement[0]/UsedLogisticsTransportMeans/RegistrationTradeCountry/ID", shipment.VehicleCountryCode)
        ];

        return codes
            .Where(c => !Blank(c.Code) && !context.IsKnownCountry(c.Code))
            .Select(c => new RuleViolation(c.Path, $"Paese '{c.Code}' non valido: serve un codice ISO a due lettere."));
    }

    /// <summary>Dal prefisso eFTI del soggetto (<c>consignor</c>, <c>connectingCarrier[0]</c>) all'elemento dell'eCMR.</summary>
    private static string PartyElement(string eftiPath) =>
        char.ToUpperInvariant(eftiPath[0]) + eftiPath[1..].Replace("[", "TradeParty[", StringComparison.Ordinal) +
        (eftiPath.Contains('[', StringComparison.Ordinal) ? string.Empty : "TradeParty");
}

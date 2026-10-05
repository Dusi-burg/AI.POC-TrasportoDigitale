namespace Dusiburg.AI.Trasporto.Efti.Core.Dataset;

/// <summary>Un elemento del dataset eFTI che il POC valorizza.</summary>
/// <param name="Path">Percorso dell'elemento dalla radice <c>consignment</c>, senza indici.</param>
/// <param name="Source">Da dove viene il valore.</param>
/// <param name="CmrBox">Casella della lettera di vettura CMR (modello IRU), se il dato vi compare.</param>
public sealed record CoveredElement(string Path, string Source, string? CmrBox = null);

/// <summary>
/// Copertura dichiarata del dataset eFTI (G3): gli elementi che <see cref="EftiDatasetMapper"/> scrive, con l'origine del dato.
/// Identificativi, formati e sottoinsiemi non stanno qui: li aggiunge lo schema quando si genera
/// <c>docs/copertura-dataset.md</c>. Un test verifica che il mapper non scriva nulla fuori da questo elenco.
/// </summary>
public static class DatasetCoverage
{
    private const string Order = "ordine del TMS";
    private const string Lines = "righe dell'ordine";
    private const string Trip = "viaggio";
    private const string Configuration = "configurazione della piattaforma";

    public static IReadOnlyList<CoveredElement> Elements { get; } =
    [
        .. Party("consignor", $"{Order}: mittente", "1"),
        .. Party("consignee", $"{Order}: destinatario", "2"),
        .. Party("carrier", $"{Configuration}: vettore", "16"),
        // Del vettore successivo il TMS conosce solo nome, paese e partita IVA.
        new("connectingCarrier/name", $"{Trip}: vettore successivo, nome", "17"),
        new("connectingCarrier/postalAddress/countryCode", $"{Trip}: vettore successivo, paese", "17"),
        new("connectingCarrier/taxRegistration/id", $"{Trip}: vettore successivo, partita IVA", "17"),

        .. Address("carrierAcceptanceLocation/postalAddress", $"{Order}: luogo di presa in carico", "4"),
        new("carrierAcceptanceDateTime", $"{Order}: presa in carico effettiva, altrimenti prevista", "4"),
        .. Address("consigneeReceiptLocation/postalAddress", $"{Order}: luogo di consegna", "3"),
        new("deliveryEvent/scheduledOccurrenceDateTime", $"{Order}: consegna pattuita"),
        new("deliveryEvent/actualOccurrenceDateTime", $"{Order}: consegna effettiva", "24"),

        new("natureIdentificationCargo/identificationText", $"{Lines}: descrizione", "9"),
        new("numberOfPackages", $"{Order}: colli dichiarati", "7"),
        new("transportPackage/itemQuantity", $"{Lines}: colli", "7"),
        new("transportPackage/typeCode", $"{Lines}: tipo di imballo", "8"),
        new("grossWeight", $"{Order}: peso lordo dichiarato", "11"),
        new("grossVolume", $"{Lines}: somma dei volumi", "12"),

        new("includedConsignmentItem/grossVolume", $"{Lines}: volume", "12"),
        new("includedConsignmentItem/transportDangerousGoods/uNDGID", $"{Lines}: numero ONU", "9"),
        new("includedConsignmentItem/transportDangerousGoods/properShippingName", $"{Lines}: denominazione ufficiale di trasporto", "9"),
        new("includedConsignmentItem/transportDangerousGoods/hazardClassificationID", $"{Lines}: classe ADR", "9"),
        new("includedConsignmentItem/transportDangerousGoods/packagingDangerLevelCode", $"{Lines}: gruppo di imballaggio", "9"),
        new("includedConsignmentItem/transportDangerousGoods/grossWeight", $"{Lines}: peso della riga ADR", "11"),
        new("includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/itemQuantity", $"{Lines}: colli della riga ADR", "7"),
        new("includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/typeCode", $"{Lines}: tipo di imballo della riga ADR", "8"),

        new("mainCarriageTransportMovement/modeCode", "fisso: strada"),
        new("mainCarriageTransportMovement/dangerousGoodsIndicator", $"{Lines}: almeno una riga ADR"),
        new("mainCarriageTransportMovement/usedTransportMeans/id", $"{Trip}: targa della motrice"),
        new("mainCarriageTransportMovement/usedTransportMeans/registrationCountry/code", $"{Trip}: paese della motrice"),
        new("mainCarriageTransportMovement/usedTransportMeans/typeCode", $"{Trip}: tipo di mezzo (autocarro o trattore)"),
        new("usedTransportEquipment/id", $"{Trip}: targa del rimorchio"),
        new("usedTransportEquipment/categoryCode", "fisso: semirimorchio"),
        new("usedTransportEquipment/registrationCountry/code", $"{Trip}: paese del rimorchio"),
        new("usedTransportEquipment/sequenceNumber", "fisso: 1"),

        new("transportContractDocument/id", $"{Order}: numero della lettera di vettura"),
        new("transportContractDocument/typeCode", "fisso: lettera di vettura stradale"),
        new("applicableServiceCharge/appliedAmount", $"{Order}: nolo", "20"),
        new("applicableServiceCharge/paymentArrangementCode", $"{Order}: pagamento del nolo", "14"),
        new("paymentArrangementCode", $"{Order}: pagamento del nolo", "14"),
        new("cODAmount", $"{Order}: contrassegno", "15"),
        new("declaredValueForCarriageAmount", $"{Order}: valore dichiarato", "19"),
        new("transshipmentPermittedIndicator", $"{Order}: trasbordo ammesso", "19"),
        new("consignorProvidedInformationText", $"{Order}: istruzioni del mittente", "13")
    ];

    private static IEnumerable<CoveredElement> Party(string path, string source, string cmrBox) =>
    [
        new($"{path}/name", $"{source}, nome", cmrBox),
        .. Address($"{path}/postalAddress", source, cmrBox),
        new($"{path}/taxRegistration/id", $"{source}, partita IVA", cmrBox)
    ];

    private static IEnumerable<CoveredElement> Address(string path, string source, string cmrBox) =>
    [
        new($"{path}/streetName", $"{source}, via", cmrBox),
        new($"{path}/postcode", $"{source}, CAP", cmrBox),
        new($"{path}/cityName", $"{source}, città", cmrBox),
        new($"{path}/countryCode", $"{source}, paese", cmrBox)
    ];
}

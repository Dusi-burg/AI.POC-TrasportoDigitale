using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;

namespace Dusiburg.AI.Trasporto.Shared.Demo;

/// <summary>
/// Tabella unica degli scenari demo: ordini del TMS con codice fisso, che l'importazione trasforma in spedizioni. Tutti i dati
/// sono inventati; gli invarianti degli scenari sono verificati dai test. Le date sono relative al giorno del seed.
/// </summary>
public static class DemoCatalog
{
    /// <summary>Il vettore: l'operatore dal cui punto di vista è costruito il POC.</summary>
    public static DemoParty Carrier { get; } =
        new("Dusiburg Trasporti S.r.l.", "IT04719020002", "Via del Lavoro 18", "37135", "Verona", "IT");

    /// <summary>
    /// Committenti demo: sei clienti del POC Order-to-Cash, con la stessa ragione sociale e la stessa partita IVA. È il filo
    /// "ordine → spedizione" del portfolio. Le partite IVA sono fittizie ma formalmente corrette (ufficio <c>000</c>, cifra di
    /// controllo valida), perché qui la validazione eFTI le controlla; il catalogo di Order-to-Cash usa gli stessi valori.
    /// </summary>
    public static IReadOnlyList<DemoCustomer> Customers { get; } =
    [
        new("CUS-001", "Officine Meccaniche Brambilla S.r.l.", "IT01000010007", "Via dell'Industria 12", "20054", "Segrate", "IT", "acquisti@brambilla-om.it"),
        new("CUS-002", "Cartiera del Brenta S.p.A.", "IT01000020006", "Via Riviera 3", "35010", "Vigonza", "IT", "approvvigionamenti@cartierabrenta.it"),
        new("CUS-003", "Automazioni Nord-Est S.r.l.", "IT01000030005", "Viale Venezia 88", "31100", "Treviso", "IT", "purchasing@automazioni-ne.it"),
        new("CUS-004", "Imballaggi Tirreno S.r.l.", "IT01000040004", "Via Aurelia 150", "57121", "Livorno", "IT", "ordini@imballaggitirreno.it"),
        new("CUS-005", "Tessitura Valle Seriana S.p.A.", "IT01000050003", "Via Provinciale 45", "24021", "Albino", "IT", "ordini@tessituravs.it"),
        new("CUS-006", "Meccanica di Precisione Torinese S.p.A.", "IT01000060002", "Corso Francia 300", "10146", "Torino", "IT", "supply@mptorinese.it"),
    ];

    public static IReadOnlyList<DemoTransportOrder> Orders { get; } =
    [
        new("TO-1001", [1], "Spedizione internazionale completa", "CUS-001",
            new("Maschinenbau Neckartal GmbH", "DE999000011", "Industriestraße 40", "70565", "Stuttgart", "DE"),
            TransportOrderStatus.InTransit, PickupOffsetDays: -1, DeliveryOffsetDays: 1, DeliveredOffsetDays: null,
            [
                new("Cuscinetti e supporti industriali", 6, PackageType.Pallet, 1_840m, 7.2m),
                new("Motori elettrici trifase", 2, PackageType.Crate, 620m, 1.8m),
            ],
            DeclaredGrossWeightKg: null, "CMR-1001001", FreightAmount: 1_450m, FreightPayment.Prepaid, CodAmount: null, DeclaredValue: null,
            SenderInstructions: null,
            new("TR-1001", "ZZ101AA", "XZ101AA", "DRV-01")),

        new("TO-1002", [2], "Campi mancanti e ADR incoerente", "CUS-002",
            new("Papeteries du Rhône SAS", VatNumber: null, "Rue de Gerland 112", PostalCode: null, "Lyon", "FR"),
            TransportOrderStatus.Planned, PickupOffsetDays: 1, DeliveryOffsetDays: 3, DeliveredOffsetDays: null,
            [
                new("Additivo sbiancante per cellulosa", 12, PackageType.Drum, 2_640m, 3.6m,
                    new DemoDangerousGoods(UnNumber: null, ProperShippingName: null, AdrClass: "5.1", PackingGroup: null)),
                new("Carta in bobine", 10, PackageType.Roll, 6_200m, 14.0m),
            ],
            DeclaredGrossWeightKg: 8_480m, CmrNumber: null, FreightAmount: 1_980m, FreightPayment.Prepaid, CodAmount: null, DeclaredValue: null,
            SenderInstructions: "Merce ADR: UN 2014 PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA, classe 5.1 (8), GI II. " +
                "Consegna presso Papeteries du Rhône, 69007 Lyon, TVA FR99999000022. Peso lordo complessivo 8.840 kg.",
            new("TR-1002", "ZZ102AA", "XZ102AA", "DRV-02")),

        new("TO-1003", [3], "CMR in PDF con caselle ambigue", "CUS-003",
            new("Elektromontaža Sava d.o.o.", "SI99900033", "Letališka cesta 27", "1000", "Ljubljana", "SI"),
            TransportOrderStatus.InTransit, PickupOffsetDays: 0, DeliveryOffsetDays: 1, DeliveredOffsetDays: null,
            [
                new("Materiale elettrico", 0, PackageType.Pallet, 0m, VolumeM3: null),
            ],
            DeclaredGrossWeightKg: null, "CMR-1003001", FreightAmount: 690m, FreightPayment.Collect, CodAmount: null, DeclaredValue: null,
            SenderInstructions: "Dettaglio colli e pesi come da lettera di vettura allegata.",
            new("TR-1003", "ZZ103AA", "XZ103AA", "DRV-03")),

        new("TO-1004", [4], "Controllo su strada a partire dalla targa", "CUS-004",
            new("Química Levante S.L.", "ESB99900044", "Carrer dels Fusters 9", "46014", "València", "ES"),
            TransportOrderStatus.InTransit, PickupOffsetDays: -1, DeliveryOffsetDays: 2, DeliveredOffsetDays: null,
            [
                new("Vernici industriali a solvente", 20, PackageType.Drum, 4_200m, 5.4m,
                    new DemoDangerousGoods("1263", "PITTURE", "3", "II")),
                new("Imballaggi in cartone ondulato", 8, PackageType.Pallet, 1_100m, 12.8m),
            ],
            DeclaredGrossWeightKg: null, "CMR-1004001", FreightAmount: 2_350m, FreightPayment.Prepaid, CodAmount: null, DeclaredValue: null,
            SenderInstructions: null,
            new("TR-1004", "ZZ104AA", "XZ104AA", "DRV-04")),

        new("TO-1005", [5, 7], "Consegna con riserva per danno e colli mancanti", "CUS-005",
            new("Textilhaus Donau GmbH", "ATU99900055", "Laxenburger Straße 151", "1100", "Wien", "AT"),
            // Consegnata da due giorni: ancora raggiungibile dal gate, che tiene una spedizione per cinque giorni dopo la consegna.
            TransportOrderStatus.Delivered, PickupOffsetDays: -4, DeliveryOffsetDays: -2, DeliveredOffsetDays: -2,
            [
                new("Tessuti in pezza", 24, PackageType.Carton, 3_600m, 16.5m),
            ],
            DeclaredGrossWeightKg: null, "CMR-1005001", FreightAmount: 1_280m, FreightPayment.Prepaid, CodAmount: null, DeclaredValue: 48_000m,
            SenderInstructions: null,
            new("TR-1005", "ZZ105AA", "XZ105AA", "DRV-05")),

        new("TO-1006", [6], "Reclamo per ritardo con riserva fuori termine", "CUS-006",
            new("Précision Alpine SARL", "FR99999000066", "Rue des Martyrs 31", "38000", "Grenoble", "FR"),
            // Consegnata da nove giorni: sul gate è scaduta, un'autorità non la trova più.
            TransportOrderStatus.Delivered, PickupOffsetDays: -14, DeliveryOffsetDays: -12, DeliveredOffsetDays: -9,
            [
                new("Componenti meccanici di precisione", 5, PackageType.Crate, 1_450m, 4.0m),
            ],
            DeclaredGrossWeightKg: null, "CMR-1006001", FreightAmount: 960m, FreightPayment.Prepaid, CodAmount: null, DeclaredValue: null,
            SenderInstructions: "Consegna tassativa entro la data pattuita: fermo linea del destinatario.",
            new("TR-1006", "ZZ106AA", "XZ106AA", "DRV-06")),
    ];

    public static DemoCustomer CustomerOf(DemoTransportOrder order) => Customers.Single(c => c.Code == order.CustomerCode);

    /// <summary>Negli ordini demo il mittente è sempre il committente.</summary>
    public static DemoParty ConsignorOf(DemoTransportOrder order)
    {
        DemoCustomer customer = CustomerOf(order);

        return new DemoParty(customer.Name, customer.VatNumber, customer.Street, customer.PostalCode, customer.City, customer.CountryCode);
    }
}

public sealed record DemoParty(string Name, string? VatNumber, string? Street, string? PostalCode, string? City, string? CountryCode);

public sealed record DemoCustomer(
    string Code, string Name, string VatNumber, string Street, string PostalCode, string City, string CountryCode, string Email);

/// <summary>Dati ADR di una riga: i valori <c>null</c> sono lacune volute dello scenario.</summary>
public sealed record DemoDangerousGoods(string? UnNumber, string? ProperShippingName, string? AdrClass, string? PackingGroup);

public sealed record DemoGoodsLine(
    string? Description, int PackageCount, PackageType PackageType, decimal GrossWeightKg, decimal? VolumeM3, DemoDangerousGoods? DangerousGoods = null);

/// <summary>Viaggio dell'ordine demo: targhe con prefisso <c>ZZ</c>/<c>XZ</c>, serie non assegnate.</summary>
public sealed record DemoTrip(string Code, string TractorPlate, string TrailerPlate, string DriverCode);

/// <param name="Scenarios">Scenari del piano (§7) che usano l'ordine.</param>
/// <param name="PickupOffsetDays">Data di presa in carico, in giorni rispetto al giorno del seed.</param>
/// <param name="DeliveryOffsetDays">Data di consegna pattuita.</param>
/// <param name="DeliveredOffsetDays">Consegna effettiva; <c>null</c> se non ancora consegnato.</param>
/// <param name="DeclaredGrossWeightKg">Peso di testata; <c>null</c> = somma delle righe.</param>
public sealed record DemoTransportOrder(
    string Code,
    IReadOnlyList<int> Scenarios,
    string Title,
    string CustomerCode,
    DemoParty Consignee,
    TransportOrderStatus Status,
    int PickupOffsetDays,
    int DeliveryOffsetDays,
    int? DeliveredOffsetDays,
    IReadOnlyList<DemoGoodsLine> Lines,
    decimal? DeclaredGrossWeightKg,
    string? CmrNumber,
    decimal FreightAmount,
    FreightPayment FreightPayment,
    decimal? CodAmount,
    decimal? DeclaredValue,
    string? SenderInstructions,
    DemoTrip Trip)
{
    public decimal LinesGrossWeightKg => Lines.Sum(l => l.GrossWeightKg);

    public int LinesPackageCount => Lines.Sum(l => l.PackageCount);
}

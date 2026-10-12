using System.Globalization;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Demo;

namespace Dusiburg.AI.Trasporto.Eval.Cmr;

/// <summary>
/// Un ordine, modificabile prima di diventare il contenuto stampato di un CMR: è ciò che il documento dice di vero, anche dove
/// il TMS ha una lacuna o un dato sbagliato. Da qui nascono sia le caselle del PDF sia la verità di riferimento.
/// </summary>
public sealed class CmrDraft
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    private readonly DemoTransportOrder _order;
    private readonly DateOnly _referenceDay;

    /// <param name="consignor">Il mittente dell'ordine: negli ordini demo è il committente.</param>
    /// <param name="referenceDay">Giorno a cui sono relative le date stampate.</param>
    public CmrDraft(DemoTransportOrder order, DemoParty consignor, DateOnly referenceDay)
    {
        _order = order;
        _referenceDay = referenceDay;

        Sender = ToBlock(consignor);
        Consignee = ToBlock(order.Consignee);
        TakingOver = ToBlock(consignor) with { Name = null, VatNumber = null };
        Delivery = ToBlock(order.Consignee) with { Name = null, VatNumber = null };
        Lines = [.. order.Lines];
        DangerousGoods = [.. order.Lines.Select(l => l.DangerousGoods)];
        PackagesText = [.. order.Lines.Select(l => (string?)null)];
        GrossWeightText = [.. order.Lines.Select(l => (string?)null)];
        VolumeText = [.. order.Lines.Select(l => (string?)null)];
        SenderInstructions = order.SenderInstructions ?? string.Empty;
        TrailerPlate = order.Trip.TrailerPlate;
    }

    public CmrPartyBlock Sender { get; set; }

    public CmrPartyBlock Consignee { get; set; }

    public CmrPartyBlock TakingOver { get; set; }

    public CmrPartyBlock Delivery { get; set; }

    public List<DemoGoodsLine> Lines { get; }

    public List<DemoDangerousGoods?> DangerousGoods { get; }

    /// <summary>Testo stampato al posto del numero di colli della riga, quando il documento è ambiguo.</summary>
    public List<string?> PackagesText { get; }

    public List<string?> GrossWeightText { get; }

    public List<string?> VolumeText { get; }

    public string? TotalPackagesText { get; set; }

    public string? TotalGrossWeightText { get; set; }

    /// <summary>Peso lordo totale che il documento dichiara; senza, la somma delle righe.</summary>
    public decimal? TotalGrossWeightKg { get; set; }

    public string SenderInstructions { get; set; }

    public string TrailerPlate { get; set; }

    public List<CmrHandwrittenNote> Handwritten { get; } = [];

    public List<string> Ambiguous { get; } = [];

    /// <summary>Valori che un'iniezione nel testo cerca di far uscire, per campo: non devono mai essere estratti.</summary>
    public Dictionary<string, string> Injected { get; } = [];

    public int TotalPackages => Lines.Sum(l => l.PackageCount);

    public decimal TotalWeight => TotalGrossWeightKg ?? Lines.Sum(l => l.GrossWeightKg);

    public CmrContent ToContent(string code, CmrLayout layout, string title, string category)
    {
        DemoParty carrier = DemoCatalog.Carrier;
        DateOnly takingOver = _referenceDay.AddDays(_order.PickupOffsetDays);

        return new CmrContent
        {
            Code = code,
            OrderCode = _order.Code,
            Title = title,
            Category = category,
            Layout = layout,
            CmrNumber = CmrNumber,
            Sender = Sender,
            Consignee = Consignee,
            Carrier = ToBlock(carrier),
            Delivery = Delivery,
            TakingOver = TakingOver,
            TakingOverDate = takingOver.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            Goods = [.. Lines.Select((line, index) => ToRow(line, index))],
            TotalPackages = TotalPackagesText ?? TotalPackages.ToString(CultureInfo.InvariantCulture),
            TotalGrossWeight = TotalGrossWeightText ?? $"{Number(TotalWeight)} kg",
            SenderInstructions = SenderInstructions,
            Payment = _order.FreightPayment == FreightPayment.Prepaid ? "Porto franco / Carriage paid" : "Porto assegnato / Carriage forward",
            CashOnDelivery = _order.CodAmount is { } cod ? $"{Number(cod)} EUR" : string.Empty,
            VehiclePlate = _order.Trip.TractorPlate,
            TrailerPlate = TrailerPlate,
            EstablishedAt = $"{Sender.City}, {takingOver.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
            Handwritten = [.. Handwritten],
            Expected = Expected(),
            Ambiguous = [.. Ambiguous],
            Injected = new Dictionary<string, string>(Injected)
        };
    }

    /// <summary>Numero come lo scrive un CMR italiano: punto per le migliaia, virgola per i decimali.</summary>
    public static string Number(decimal value) => value.ToString(value == decimal.Truncate(value) ? "#,##0" : "#,##0.##", Italian);

    private string CmrNumber => _order.CmrNumber ?? $"CMR-{_order.Code["TO-".Length..]}001";

    private CmrGoodsRow ToRow(DemoGoodsLine line, int index)
    {
        DemoDangerousGoods? dangerous = DangerousGoods[index];

        // Come su un documento ADR: numero ONU, denominazione, classe, gruppo di imballaggio.
        string description = dangerous is null
            ? line.Description ?? string.Empty
            : $"{line.Description} — UN {dangerous.UnNumber} {dangerous.ProperShippingName}, {dangerous.AdrClass}" +
              (dangerous.PackingGroup is null ? string.Empty : $", {dangerous.PackingGroup}");

        return new CmrGoodsRow(
            Marks: $"{_order.Code}/{index + 1}",
            Packages: PackagesText[index] ?? line.PackageCount.ToString(CultureInfo.InvariantCulture),
            Packing: PackingLabel(line.PackageType),
            Description: description,
            GrossWeight: GrossWeightText[index] ?? Number(line.GrossWeightKg),
            Volume: VolumeText[index] ?? (line.VolumeM3 is { } volume ? Number(volume) : string.Empty));
    }

    private Dictionary<string, string> Expected()
    {
        Dictionary<string, string> expected = new()
        {
            ["transportContractDocument/id"] = CmrNumber,
            ["numberOfPackages"] = TotalPackages.ToString(CultureInfo.InvariantCulture),
            ["grossWeight"] = Plain(TotalWeight),
            ["mainCarriageTransportMovement[0]/usedTransportMeans/id"] = _order.Trip.TractorPlate
        };

        AddParty(expected, "consignor", Sender);
        AddParty(expected, "consignee", Consignee);
        AddPlace(expected, "carrierAcceptanceLocation", TakingOver);
        AddPlace(expected, "consigneeReceiptLocation", Delivery);
        Add(expected, "usedTransportEquipment[0]/id", TrailerPlate);

        for (int index = 0; index < Lines.Count; index++)
        {
            expected[$"transportPackage[{index}]/itemQuantity"] = Lines[index].PackageCount.ToString(CultureInfo.InvariantCulture);

            if (DangerousGoods[index] is { } dangerous)
            {
                string prefix = $"includedConsignmentItem[{index}]/transportDangerousGoods[0]";

                Add(expected, $"{prefix}/uNDGID", dangerous.UnNumber);
                Add(expected, $"{prefix}/properShippingName", dangerous.ProperShippingName);
                Add(expected, $"{prefix}/hazardClassificationID", dangerous.AdrClass);
                Add(expected, $"{prefix}/packagingDangerLevelCode", dangerous.PackingGroup);
            }
        }

        return expected;
    }

    private static string Plain(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string PackingLabel(PackageType type) => type switch
    {
        PackageType.Pallet => "pallet",
        PackageType.Carton => "cartoni",
        PackageType.Drum => "fusti",
        PackageType.Crate => "casse",
        PackageType.Bag => "sacchi",
        PackageType.Bundle => "fasci",
        PackageType.Roll => "bobine",
        _ => type.ToString()
    };

    private static void AddParty(Dictionary<string, string> expected, string element, CmrPartyBlock party)
    {
        Add(expected, $"{element}/name", party.Name);
        Add(expected, $"{element}/taxRegistration/id", party.VatNumber);
        AddPlace(expected, element, party);
    }

    private static void AddPlace(Dictionary<string, string> expected, string element, CmrPartyBlock place)
    {
        Add(expected, $"{element}/postalAddress/streetName", place.Street);
        Add(expected, $"{element}/postalAddress/postcode", place.PostalCode);
        Add(expected, $"{element}/postalAddress/cityName", place.City);
        Add(expected, $"{element}/postalAddress/countryCode", place.CountryCode);
    }

    private static void Add(Dictionary<string, string> expected, string path, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            expected[path] = value;
        }
    }

    private static CmrPartyBlock ToBlock(DemoParty party) =>
        new(party.Name, party.Street, party.PostalCode, party.City, party.CountryCode, party.VatNumber);
}

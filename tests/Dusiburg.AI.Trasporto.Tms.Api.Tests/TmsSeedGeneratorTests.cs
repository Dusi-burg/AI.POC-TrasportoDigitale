using System.Globalization;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Tms.Data.Entities;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Tms.Api.Tests;

/// <summary>Dati sintetici del TMS: ripetibilità, forma del dataset e difetti voluti. Nessun database.</summary>
public class TmsSeedGeneratorTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    private static readonly TmsSeedData Data = TmsSeedGenerator.Generate(TmsSeedGenerator.DefaultRandomSeed, Today);

    private static IEnumerable<TransportOrder> Generated => Data.Orders.Skip(DemoCatalog.Orders.Count);

    [Test]
    public void Generate_SameSeedAndDate_ProducesIdenticalData()
    {
        //SUT
        TmsSeedData again = TmsSeedGenerator.Generate(TmsSeedGenerator.DefaultRandomSeed, Today);
        TmsSeedData otherSeed = TmsSeedGenerator.Generate(TmsSeedGenerator.DefaultRandomSeed + 1, Today);

        Assert.That(Snapshot(again), Is.EqualTo(Snapshot(Data)));
        Assert.That(Snapshot(otherSeed), Is.Not.EqualTo(Snapshot(Data)));
    }

    [Test]
    public void Generate_OtherDate_ShiftsDatesButKeepsCodesAndDefects()
    {
        //SUT
        TmsSeedData later = TmsSeedGenerator.Generate(TmsSeedGenerator.DefaultRandomSeed, Today.AddDays(30));

        Assert.That(later.Orders.Select(o => o.Code), Is.EqualTo(Data.Orders.Select(o => o.Code)));
        Assert.That(later.Orders.Select(o => o.PickupDate), Is.EqualTo(Data.Orders.Select(o => o.PickupDate!.Value.AddDays(30))));
        Assert.That(later.DefectsByOrderCode.Keys, Is.EquivalentTo(Data.DefectsByOrderCode.Keys));
    }

    [Test]
    public void Generate_Default_HasTheSizesOfThePlan()
    {
        //SUT
        TmsSeedData data = Data;

        Assert.That(data.Customers, Has.Count.EqualTo(TmsSeedGenerator.Customers));
        Assert.That(data.Orders, Has.Count.EqualTo(DemoCatalog.Orders.Count + TmsSeedGenerator.GeneratedOrders));
        Assert.That(data.Vehicles.Count(v => v.Type != VehicleType.SemiTrailer), Is.EqualTo(TmsSeedGenerator.TractiveUnits));
        Assert.That(data.Vehicles.Count(v => v.Type == VehicleType.SemiTrailer), Is.EqualTo(TmsSeedGenerator.Trailers));
        Assert.That(data.Drivers, Has.Count.EqualTo(TmsSeedGenerator.Drivers));
        Assert.That(data.Orders.Take(DemoCatalog.Orders.Count).Select(o => o.Code), Is.EqualTo(DemoCatalog.Orders.Select(o => o.Code)));
    }

    [Test]
    public void Generate_Default_BusinessKeysAreUnique()
    {
        //SUT
        TmsSeedData data = Data;

        Assert.That(data.Orders.Select(o => o.Code), Is.Unique);
        Assert.That(data.Trips.Select(t => t.Code), Is.Unique);
        Assert.That(data.Customers.Select(c => c.Code), Is.Unique);
        Assert.That(data.Customers.Select(c => c.VatNumber), Is.Unique);
        Assert.That(data.Customers.Select(c => c.Name), Is.Unique);
        Assert.That(data.Vehicles.Select(v => v.PlateNumber), Is.Unique);
        Assert.That(data.Drivers.Select(d => d.Code), Is.Unique);
    }

    [Test]
    public void Generate_Default_UsesOnlyFictitiousIdentifiers()
    {
        //SETUP
        IEnumerable<Customer> generatedCustomers = Data.Customers.Skip(DemoCatalog.Customers.Count);
        string?[] foreignVatNumbers = [.. Generated
            .SelectMany(o => new[] { o.Consignor, o.Consignee })
            .Where(p => p.CountryCode != "IT" && p.VatNumber is not null && !p.VatNumber.StartsWith("IT", StringComparison.Ordinal))
            .Select(p => p.VatNumber)];

        //SUT
        TmsSeedData data = Data;

        Assert.That(data.Vehicles.Select(v => v.PlateNumber), Is.All.Matches<string>(p => p.StartsWith("ZZ", StringComparison.Ordinal) || p.StartsWith("XZ", StringComparison.Ordinal)));
        Assert.That(generatedCustomers.Select(c => c.VatNumber), Is.All.Matches<string>(ItalianVat.IsValid));
        Assert.That(generatedCustomers.Select(c => c.VatNumber!.Substring(9, 3)), Is.All.EqualTo(ItalianVat.FictitiousOfficeCode));
        Assert.That(generatedCustomers.Select(c => c.Email), Is.All.EndsWith(".example"));
        Assert.That(foreignVatNumbers, Is.Not.Empty.And.All.Contains("999"));
    }

    [Test]
    public void Generate_Default_MixesNationalAndInternationalAndEveryStatus()
    {
        //SETUP
        TransportOrder[] clean = [.. Generated.Where(o => !Data.DefectsByOrderCode.ContainsKey(o.Code))];

        //SUT
        double internationalShare = (double)clean.Count(o => o.Consignor.CountryCode != o.Consignee.CountryCode) / clean.Length;

        Assert.That(internationalShare, Is.InRange(0.30, 0.60));
        Assert.That(Generated.Select(o => o.Status).Distinct(), Is.EquivalentTo(Enum.GetValues<TransportOrderStatus>()));
        Assert.That(Generated.Count(o => o.Lines.Any(l => l.IsDangerousGoods)), Is.GreaterThanOrEqualTo(10), "ordini con merci pericolose");
    }

    [Test]
    public void Generate_Default_StatusDatesAndTripsAreCoherent()
    {
        //SUT
        TransportOrder[] generated = [.. Generated];

        Assert.That(generated.Where(o => o.Status == TransportOrderStatus.Delivered),
            Is.All.Matches<TransportOrder>(o => o.PickedUpAt is not null && o.DeliveredAt is not null && o.Trip is not null && o.PickupDate < Today));
        Assert.That(generated.Where(o => o.Status == TransportOrderStatus.InTransit),
            Is.All.Matches<TransportOrder>(o => o.PickedUpAt is not null && o.DeliveredAt is null && o.Trip is not null && o.PickupDate <= Today));
        Assert.That(generated.Where(o => o.Status == TransportOrderStatus.Planned),
            Is.All.Matches<TransportOrder>(o => o.PickedUpAt is null && o.DeliveredAt is null && o.PickupDate > Today));
        Assert.That(generated.Where(o => o.Status == TransportOrderStatus.Cancelled),
            Is.All.Matches<TransportOrder>(o => o.PickedUpAt is null && o.Trip is null));
        Assert.That(Data.Trips, Is.All.Matches<Trip>(t => (t.Vehicle.Type == VehicleType.Tractor) == (t.Trailer is not null)), "rimorchio solo con il trattore");
    }

    [Test]
    public void Generate_Default_AboutOneThirdOfTheOrdersHasDefects_AndEveryDefectOccurs()
    {
        //SUT
        double share = (double)Data.DefectsByOrderCode.Count / TmsSeedGenerator.GeneratedOrders;
        IEnumerable<SeedDefect> assigned = Data.DefectsByOrderCode.Values.SelectMany(d => d);

        Assert.That(share, Is.InRange(0.25, 0.45));
        Assert.That(assigned.Distinct(), Is.EquivalentTo(Enum.GetValues<SeedDefect>()));
        Assert.That(Data.DefectsByOrderCode.Values, Is.All.Matches<IReadOnlyList<SeedDefect>>(d => d.Count is 1 or 2 && d.Distinct().Count() == d.Count));
        Assert.That(Data.DefectsByOrderCode.Keys, Is.SubsetOf(Generated.Select(o => o.Code)), "gli ordini demo non ricevono difetti casuali");
    }

    [Test]
    public void Generate_Default_EveryAssignedDefectIsVisibleInTheData_AndNoOtherIs()
    {
        //SUT
        foreach (TransportOrder order in Generated)
        {
            IReadOnlyList<SeedDefect> expected = Data.DefectsByOrderCode.GetValueOrDefault(order.Code) ?? [];
            SeedDefect[] visible = [.. Enum.GetValues<SeedDefect>().Where(defect => IsVisible(order, defect))];

            Assert.That(visible, Is.EquivalentTo(expected), order.Code);
        }
    }

    /// <summary>Come si riconosce ciascun difetto guardando solo il dato: è ciò che la validazione eFTI dovrà saper fare.</summary>
    private static bool IsVisible(TransportOrder order, SeedDefect defect)
    {
        decimal linesWeight = order.Lines.Sum(l => l.GrossWeightKg);

        return defect switch
        {
            SeedDefect.MissingConsigneeAddress => order.Consignee.PostalCode is null && order.Consignee.City is null
                && order.Delivery.PostalCode is null && order.Delivery.City is null,
            SeedDefect.MissingGoodsDescription => order.Lines.Any(l => l.Description is null),
            SeedDefect.MissingPackageCount => order.Lines.Any(l => l.PackageCount == 0),
            SeedDefect.WeightMismatch => Math.Abs(order.TotalGrossWeightKg - linesWeight) > linesWeight * 0.05m,
            SeedDefect.AdrWithoutUnNumber => order.Lines.Any(l => l.IsDangerousGoods && l.UnNumber is null),
            SeedDefect.InvalidCountryCode => order.Consignee.CountryCode is "XX" or "ITA" or "UK" or "D",
            SeedDefect.DeliveryBeforePickup => order.DeliveryDate < order.PickupDate,
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
    }

    /// <summary>Tutto il contenuto generato in una stringa: due dataset uguali hanno la stessa istantanea.</summary>
    private static string Snapshot(TmsSeedData data)
    {
        IEnumerable<string> customers = data.Customers.Select(c => $"{c.Code}|{c.Name}|{c.VatNumber}|{c.Street}|{c.City}|{c.Email}");
        IEnumerable<string> vehicles = data.Vehicles.Select(v => $"{v.PlateNumber}|{v.Type}");
        IEnumerable<string> drivers = data.Drivers.Select(d => $"{d.Code}|{d.FirstName}|{d.LastName}");
        IEnumerable<string> trips = data.Trips.Select(t => $"{t.Code}|{t.Vehicle.PlateNumber}|{t.Trailer?.PlateNumber}|{t.Driver.Code}|{t.DepartureDate:O}|{t.SubcontractorName}");
        IEnumerable<string> orders = data.Orders.Select(o => string.Create(CultureInfo.InvariantCulture,
            $"{o.Code}|{o.Customer.Code}|{o.Status}|{o.OrderDate:O}|{o.Consignor.Name}|{o.Consignee.Name}|{o.Consignee.VatNumber}|{o.Consignee.City}|{o.Consignee.CountryCode}|{o.PickupDate:O}|{o.DeliveryDate:O}|{o.PickedUpAt:O}|{o.DeliveredAt:O}|{o.TotalPackages}|{o.TotalGrossWeightKg}|{o.CmrNumber}|{o.FreightAmount}|{o.FreightPayment}|{o.CodAmount}|{o.DeclaredValue}|{o.SenderInstructions}|{o.Trip?.Code}|") +
            string.Join(";", o.Lines.Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.LineNumber},{l.Description},{l.PackageCount},{l.PackageType},{l.GrossWeightKg},{l.VolumeM3},{l.UnNumber},{l.AdrClass}"))));
        IEnumerable<string> defects = data.DefectsByOrderCode.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => $"{d.Key}:{string.Join(",", d.Value)}");

        return string.Join("\n", [.. customers, .. vehicles, .. drivers, .. trips, .. orders, .. defects]);
    }
}

using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Tms.Api.Tests;

/// <summary>
/// Invarianti degli scenari demo: se il catalogo cambia, ciò che ogni scenario deve mostrare (PLAN §7) deve restare vero.
/// </summary>
public class DemoCatalogTests
{
    [Test]
    public void Catalog_HasExpectedShape()
    {
        //SUT
        IReadOnlyList<DemoTransportOrder> orders = DemoCatalog.Orders;

        Assert.That(orders.Select(o => o.Code), Is.Unique);
        Assert.That(orders.Select(o => o.Trip.Code), Is.Unique);
        Assert.That(orders.SelectMany(o => new[] { o.Trip.TractorPlate, o.Trip.TrailerPlate }), Is.Unique);
        Assert.That(DemoCatalog.Customers.Select(c => c.Code), Is.Unique);
        Assert.That(DemoCatalog.Customers.Select(c => c.VatNumber), Is.Unique);
        Assert.That(orders.Select(o => o.CustomerCode), Is.SubsetOf(DemoCatalog.Customers.Select(c => c.Code)));
        Assert.That(orders.SelectMany(o => o.Scenarios).Distinct(), Is.EquivalentTo(new[] { 1, 2, 3, 4, 5, 6, 7 }));
    }

    [Test]
    public void Catalog_UsesOnlyFictitiousIdentifiers()
    {
        //SUT
        IReadOnlyList<DemoTransportOrder> orders = DemoCatalog.Orders;

        Assert.That(ItalianVat.IsValid(DemoCatalog.Carrier.VatNumber), Is.True, "partita IVA del vettore");
        Assert.That(DemoCatalog.Carrier.VatNumber!.Substring(9, 3), Is.EqualTo(ItalianVat.FictitiousOfficeCode));
        Assert.That(orders.Select(o => o.Trip.TractorPlate), Is.All.StartsWith("ZZ"));
        Assert.That(orders.Select(o => o.Trip.TrailerPlate), Is.All.StartsWith("XZ"));
        Assert.That(orders.Select(o => o.Consignee.VatNumber).OfType<string>(), Is.All.Contains("999"));
    }

    [Test]
    public void Scenario1_InternationalShipment_IsComplete()
    {
        //SUT
        DemoTransportOrder order = Order("TO-1001");
        DemoParty consignor = DemoCatalog.ConsignorOf(order);

        Assert.That(order.Status, Is.EqualTo(TransportOrderStatus.InTransit));
        Assert.That(consignor.CountryCode, Is.Not.EqualTo(order.Consignee.CountryCode), "internazionale");
        Assert.That(
            new[] { order.Consignee.Name, order.Consignee.VatNumber, order.Consignee.Street, order.Consignee.PostalCode, order.Consignee.City, order.CmrNumber },
            Is.All.Not.Null);
        Assert.That(order.Lines, Is.All.Matches<DemoGoodsLine>(l => l.Description is not null && l.PackageCount > 0 && l.GrossWeightKg > 0));
        Assert.That(order.Lines, Has.None.Matches<DemoGoodsLine>(l => l.DangerousGoods is not null));
        Assert.That(order.DeclaredGrossWeightKg, Is.Null, "peso di testata = somma delle righe");
    }

    [Test]
    public void Scenario2_HasGapsThatTheSenderInstructionsCanFill()
    {
        //SUT
        DemoTransportOrder order = Order("TO-1002");
        DemoGoodsLine dangerous = order.Lines.Single(l => l.DangerousGoods is not null);

        Assert.That(order.Consignee.PostalCode, Is.Null);
        Assert.That(order.Consignee.VatNumber, Is.Null);
        Assert.That(dangerous.DangerousGoods!.UnNumber, Is.Null, "ADR senza numero ONU");
        Assert.That(order.DeclaredGrossWeightKg, Is.Not.EqualTo(order.LinesGrossWeightKg), "peso incoerente");

        // L'agente di mappatura deve poter ricondurre ogni proposta a una fonte: i dati mancanti sono nelle istruzioni del mittente.
        Assert.That(order.SenderInstructions, Does.Contain("UN 2014").And.Contain("69007").And.Contain("FR99999000022").And.Contain("8.840"));
    }

    [Test]
    public void Scenario3_OrderLacksTheDetailCarriedByTheCmr()
    {
        //SUT
        DemoTransportOrder order = Order("TO-1003");

        Assert.That(order.Lines, Is.All.Matches<DemoGoodsLine>(l => l.PackageCount == 0 && l.GrossWeightKg == 0));
        Assert.That(order.CmrNumber, Is.Not.Null);
    }

    [Test]
    public void Scenario4_DangerousGoodsInTransit_AreFullyDescribed()
    {
        //SUT
        DemoTransportOrder order = Order("TO-1004");
        DemoDangerousGoods dangerous = order.Lines.Single(l => l.DangerousGoods is not null).DangerousGoods!;

        Assert.That(order.Status, Is.EqualTo(TransportOrderStatus.InTransit));
        Assert.That((dangerous.UnNumber, dangerous.AdrClass, dangerous.PackingGroup), Is.EqualTo(("1263", "3", "II")));
        Assert.That(dangerous.ProperShippingName, Is.Not.Null);
    }

    [Test]
    public void Release2Scenarios_AreDelivered_OneOfThemLate()
    {
        //SUT
        DemoTransportOrder damaged = Order("TO-1005");
        DemoTransportOrder late = Order("TO-1006");

        Assert.That((damaged.Status, late.Status), Is.EqualTo((TransportOrderStatus.Delivered, TransportOrderStatus.Delivered)));
        Assert.That(damaged.DeliveredOffsetDays, Is.EqualTo(damaged.DeliveryOffsetDays), "consegnata nel giorno pattuito");
        Assert.That(damaged.DeclaredValue, Is.GreaterThan(0));
        Assert.That(late.DeliveredOffsetDays, Is.GreaterThan(late.DeliveryOffsetDays), "consegnata dopo la data pattuita");
    }

    private static DemoTransportOrder Order(string code) => DemoCatalog.Orders.Single(o => o.Code == code);
}

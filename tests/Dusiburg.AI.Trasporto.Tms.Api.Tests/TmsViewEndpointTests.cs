using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Tms.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Tms.Api.Tests;

/// <summary>Viste del TMS in sola lettura e reset della demo, sul database creato da <c>DbInit</c>.</summary>
public class TmsViewEndpointTests : TmsApiTestBase
{
    [Test]
    public async Task ListOrders_NoFilter_ReturnsTheWholeSeedNewestFirst()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        //SUT
        List<TransportOrderSummaryView> orders = await GetOrdersAsync(client, "/api/views/orders");

        Assert.That(orders.Select(o => o.Code), Is.EquivalentTo(Seed.Orders.Select(o => o.Code)));
        Assert.That(orders.Select(o => o.OrderDate), Is.Ordered.Descending);
    }

    [Test]
    public async Task ListOrders_ByStatusAndCustomer_FiltersTheSeed()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        string[] expectedInTransit = [.. Seed.Orders.Where(o => o.Status == TransportOrderStatus.InTransit).Select(o => o.Code)];
        string[] expectedOfCustomer = [.. Seed.Orders.Where(o => o.Customer.Code == "CUS-001").Select(o => o.Code)];

        //SUT
        List<TransportOrderSummaryView> inTransit = await GetOrdersAsync(client, "/api/views/orders?status=intransit");
        List<TransportOrderSummaryView> ofCustomer = await GetOrdersAsync(client, "/api/views/orders?customerCode=CUS-001");
        List<TransportOrderSummaryView> both = await GetOrdersAsync(client, "/api/views/orders?status=InTransit&customerCode=CUS-001");

        Assert.That(inTransit.Select(o => o.Code), Is.EquivalentTo(expectedInTransit).And.Contains("TO-1001"));
        Assert.That(ofCustomer.Select(o => o.Code), Is.EquivalentTo(expectedOfCustomer).And.Contains("TO-1001"));
        Assert.That(both.Select(o => o.Code), Is.EquivalentTo(expectedInTransit.Intersect(expectedOfCustomer)));
    }

    [TestCase("status=Shipped")]
    [TestCase("status=2")]
    public async Task ListOrders_InvalidStatus_ReturnsValidationProblem(string query)
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync($"/api/views/orders?{query}", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ErrorCodes.ValidationError));
    }

    [Test]
    public async Task GetOrder_CompleteDemoOrder_ReturnsPartiesTripAndLines()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        DemoTransportOrder demo = DemoCatalog.Orders.Single(o => o.Code == "TO-1001");

        //SUT
        TransportOrderDetailView? order = await client.GetFromJsonAsync<TransportOrderDetailView>("/api/views/orders/TO-1001", TrasportoJson.Options, CancellationToken);

        Assert.That(order, Is.Not.Null);
        Assert.That((order!.Order.CustomerCode, order.Order.Status, order.Order.HasDangerousGoods), Is.EqualTo(("CUS-001", TransportOrderStatus.InTransit, false)));
        Assert.That((order.Consignor.Name, order.Consignor.CountryCode, order.Pickup.City), Is.EqualTo(("Officine Meccaniche Brambilla S.r.l.", "IT", "Segrate")));
        Assert.That((order.Consignee.Name, order.Consignee.VatNumber, order.Delivery.City, order.Delivery.CountryCode),
            Is.EqualTo((demo.Consignee.Name, demo.Consignee.VatNumber, "Stuttgart", "DE")));
        Assert.That((order.Order.PickupDate, order.Order.DeliveryDate), Is.EqualTo((SeedDate.AddDays(-1), SeedDate.AddDays(1))));
        Assert.That(order.PickedUpAt, Is.Not.Null);
        Assert.That(order.DeliveredAt, Is.Null);
        Assert.That((order.Trip!.Code, order.Trip.Vehicle.PlateNumber, order.Trip.Trailer!.PlateNumber, order.Trip.DriverCode),
            Is.EqualTo(("TR-1001", "ZZ101AA", "XZ101AA", "DRV-01")));
        Assert.That((order.Trip.Vehicle.Type, order.Trip.Trailer.Type), Is.EqualTo((VehicleType.Tractor, VehicleType.SemiTrailer)));
        Assert.That(order.Lines.Select(l => (l.LineNumber, l.Description, l.PackageCount, l.PackageType, l.GrossWeightKg)),
            Is.EqualTo(new[] { (1, (string?)"Cuscinetti e supporti industriali", 6, PackageType.Pallet, 1_840m), (2, "Motori elettrici trifase", 2, PackageType.Crate, 620m) }));
        Assert.That((order.Order.TotalPackages, order.Order.TotalGrossWeightKg, order.CmrNumber, order.FreightPayment, order.CurrencyCode),
            Is.EqualTo((8, 2_460m, "CMR-1001001", FreightPayment.Prepaid, "EUR")));
    }

    [Test]
    public async Task GetOrder_DefectiveDemoOrder_ExposesTheGapsAsTheyAre()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        //SUT
        TransportOrderDetailView? order = await client.GetFromJsonAsync<TransportOrderDetailView>("/api/views/orders/TO-1002", TrasportoJson.Options, CancellationToken);

        TransportOrderLineView dangerous = order!.Lines.Single(l => l.IsDangerousGoods);
        Assert.That((order.Consignee.PostalCode, order.Consignee.VatNumber, order.Delivery.PostalCode), Is.EqualTo(((string?)null, (string?)null, (string?)null)));
        Assert.That((dangerous.UnNumber, dangerous.AdrClass), Is.EqualTo(((string?)null, "5.1")));
        Assert.That(order.Order.TotalGrossWeightKg, Is.EqualTo(8_480m).And.Not.EqualTo(order.Lines.Sum(l => l.GrossWeightKg)));
        Assert.That(order.Order.HasDangerousGoods, Is.True);
        Assert.That(order.SenderInstructions, Does.Contain("UN 2014"));
        Assert.That(order.PickedUpAt, Is.Null, "pianificato");
    }

    [Test]
    public async Task GetOrder_Unknown_ReturnsNotFound()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();

        //SUT
        using HttpResponseMessage response = await client.GetAsync("/api/views/orders/TO-9999", CancellationToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await ReadProblemCodeAsync(response), Is.EqualTo(ErrorCodes.NotFound));
    }

    [Test]
    public async Task Database_Lookups_HoldExactlyTheEnumMembers()
    {
        //SUT
        string[] statuses = await WithDbAsync(db => db.TransportOrderStatuses.OrderBy(s => s.Id).Select(s => s.Name).ToArrayAsync(CancellationToken));
        string[] vehicleTypes = await WithDbAsync(db => db.VehicleTypes.OrderBy(s => s.Id).Select(s => s.Name).ToArrayAsync(CancellationToken));
        string[] packageTypes = await WithDbAsync(db => db.PackageTypes.OrderBy(s => s.Id).Select(s => s.Name).ToArrayAsync(CancellationToken));
        string[] freightPayments = await WithDbAsync(db => db.FreightPayments.OrderBy(s => s.Id).Select(s => s.Name).ToArrayAsync(CancellationToken));

        Assert.That(statuses, Is.EqualTo(Enum.GetNames<TransportOrderStatus>()));
        Assert.That(vehicleTypes, Is.EqualTo(Enum.GetNames<VehicleType>()));
        Assert.That(packageTypes, Is.EqualTo(Enum.GetNames<PackageType>()));
        Assert.That(freightPayments, Is.EqualTo(Enum.GetNames<FreightPayment>()));
    }

    [Test]
    public async Task DevReset_AfterChanges_RestoresTheSeed()
    {
        //SETUP
        using HttpClient client = Factory.CreateClient();
        await WithDbAsync(async db =>
        {
            TransportOrder order = await db.TransportOrders.SingleAsync(o => o.Code == "TO-1001", CancellationToken);
            order.Consignee.Name = "Modificato dal test";
            db.TransportOrders.RemoveRange(await db.TransportOrders.Where(o => o.Code == "TO-2001" || o.Code == "TO-2002").ToListAsync(CancellationToken));

            return await db.SaveChangesAsync(CancellationToken);
        });

        //SUT
        using HttpResponseMessage first = await client.PostAsync("/dev/reset", content: null, CancellationToken);
        using HttpResponseMessage second = await client.PostAsync("/dev/reset", content: null, CancellationToken);

        List<TransportOrderSummaryView> orders = await GetOrdersAsync(client, "/api/views/orders");
        Assert.That((first.StatusCode, second.StatusCode), Is.EqualTo((HttpStatusCode.NoContent, HttpStatusCode.NoContent)));
        Assert.That(orders.Select(o => o.Code), Is.EquivalentTo(Seed.Orders.Select(o => o.Code)));
        Assert.That(orders.Single(o => o.Code == "TO-1001").ConsigneeName, Is.EqualTo("Maschinenbau Neckartal GmbH"));
        Assert.That(await WithDbAsync(db => db.Customers.CountAsync(CancellationToken)), Is.EqualTo(Seed.Customers.Count));
        Assert.That(await WithDbAsync(db => db.Trips.CountAsync(CancellationToken)), Is.EqualTo(Seed.Trips.Count));
    }

    private static async Task<List<TransportOrderSummaryView>> GetOrdersAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<TransportOrderSummaryView>>(path, TrasportoJson.Options, CancellationToken))!;
}

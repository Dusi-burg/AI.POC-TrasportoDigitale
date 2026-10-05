using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Demo;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>Ordine del TMS → spedizione → dataset eFTI, sugli ordini demo.</summary>
public class EftiDatasetMapperTests
{
    [Test]
    public void ShipmentFactory_DemoOrder_CopiesTheOrderAndAddsTheCarrier()
    {
        //SUT
        Shipment shipment = SeedShipments.Create("TO-1001");

        Assert.That((shipment.Code, shipment.TmsOrderCode), Is.EqualTo(("SH-1001", "TO-1001")));
        Assert.That(shipment.Parties.Select(p => (p.Role, p.Party.Name)), Is.EqualTo(new[]
        {
            (PartyRole.Consignor, (string?)"Officine Meccaniche Brambilla S.r.l."),
            (PartyRole.Consignee, "Maschinenbau Neckartal GmbH"),
            (PartyRole.Carrier, DemoCatalog.Carrier.Name)
        }));
        Assert.That((shipment.Pickup.City, shipment.Delivery.City, shipment.Delivery.CountryCode), Is.EqualTo(("Segrate", "Stuttgart", "DE")));
        Assert.That((shipment.TotalPackages, shipment.TotalGrossWeightKg, shipment.TotalVolumeM3), Is.EqualTo((8, 2_460m, 9.0m)));
        Assert.That((shipment.VehiclePlateNumber, shipment.TrailerPlateNumber, shipment.HasDangerousGoods), Is.EqualTo(("ZZ101AA", "XZ101AA", false)));
        Assert.That(shipment.Items.Select(i => (i.SequenceNumber, i.PackageCount, i.GrossWeightKg)), Is.EqualTo(new[] { (1, (int?)6, (decimal?)1_840m), (2, 2, 620m) }));
        Assert.That(shipment.ImportedAt, Is.EqualTo(SeedShipments.ImportedAt));
    }

    [Test]
    public void ShipmentFactory_DefectiveOrder_KeepsTheGapsAsTheyAre()
    {
        //SUT
        Shipment shipment = SeedShipments.Create("TO-1002");

        Party consignee = shipment.Parties.Single(p => p.Role == PartyRole.Consignee).Party;
        Assert.That((consignee.PostalCode, consignee.VatNumber), Is.EqualTo(((string?)null, (string?)null)));
        Assert.That(shipment.Items.Single(i => i.IsDangerousGoods).UnNumber, Is.Null);
        Assert.That(shipment.TotalGrossWeightKg, Is.EqualTo(8_480m), "il peso sbagliato di testata non viene corretto");
        Assert.That(shipment.PickedUpAt, Is.Null);
        Assert.That(shipment.PickupDate, Is.EqualTo(SeedShipments.Today.AddDays(1)));
    }

    [Test]
    public void Map_CompleteShipment_FillsPartiesPlacesDatesAndGoods()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1001");

        //SUT
        EftiConsignment dataset = EftiDatasetMapper.Map(shipment);

        Assert.That(dataset.Consignor!.Name, Is.EqualTo(new[] { "Officine Meccaniche Brambilla S.r.l." }));
        Assert.That(dataset.Consignor.TaxRegistration!.Single().Id, Is.EqualTo("IT01000010007"));
        EftiTradeAddress consigneeAddress = dataset.Consignee!.PostalAddress!;
        Assert.That((consigneeAddress.StreetName![0], consigneeAddress.Postcode![0], consigneeAddress.CityName![0], consigneeAddress.CountryCode),
            Is.EqualTo(("Industriestraße 40", "70565", "Stuttgart", "DE")));
        Assert.That(dataset.Carrier!.Name![0], Is.EqualTo(DemoCatalog.Carrier.Name));
        Assert.That(dataset.CarrierAcceptanceLocation!.PostalAddress!.CountryCode, Is.EqualTo("IT"));

        // Presa in carico effettiva alle 08:30 +01:00 del giorno prima: formato 205, CCYYMMDDHHMM±HHMM.
        Assert.That(dataset.CarrierAcceptanceDateTime, Is.EqualTo(new EftiDateTime("202610040830+0100", "205")));
        Assert.That(dataset.DeliveryEvent, Is.EqualTo(new EftiTransportEvent { ScheduledOccurrenceDateTime = new EftiDateTime("20261006", "102") }));

        Assert.That(dataset.NatureIdentificationCargo!.IdentificationText, Is.EqualTo(new[] { "Cuscinetti e supporti industriali", "Motori elettrici trifase" }));
        Assert.That(dataset.NumberOfPackages, Is.EqualTo(8));
        Assert.That(dataset.TransportPackage!.Select(p => (p.ItemQuantity, p.TypeCode![0])), Is.EqualTo(new[] { ((int?)6, "PX"), (2, "CR") }));
        Assert.That(dataset.GrossWeight, Is.EqualTo(new[] { new EftiMeasure(2_460m, "KGM") }));
        Assert.That(dataset.GrossVolume, Is.EqualTo(new[] { new EftiMeasure(9.0m, "MTQ") }));
        Assert.That(dataset.IncludedConsignmentItem, Has.Count.EqualTo(2));
    }

    [Test]
    public void Map_CompleteShipment_FillsVehicleContractAndCharges()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1001");

        //SUT
        EftiConsignment dataset = EftiDatasetMapper.Map(shipment);

        EftiTransportMovement movement = dataset.MainCarriageTransportMovement!.Single();
        EftiTransportEquipment trailer = dataset.UsedTransportEquipment!.Single();
        Assert.That((movement.ModeCode, movement.DangerousGoodsIndicator, movement.UsedTransportMeans!.Id, movement.UsedTransportMeans.RegistrationCountry!.Code),
            Is.EqualTo(("3", (bool?)false, "ZZ101AA", "IT")));
        Assert.That((trailer.Id, trailer.CategoryCode, trailer.RegistrationCountry!.Code, trailer.SequenceNumber), Is.EqualTo(("XZ101AA", "SM", "IT", (int?)1)));
        Assert.That(dataset.TransportContractDocument, Is.EqualTo(new EftiReferencedDocument { Id = "CMR-1001001", TypeCode = "730" }));
        Assert.That(dataset.ApplicableServiceCharge!.Single().AppliedAmount, Is.EqualTo(new[] { new EftiAmount(1_450m, "EUR") }));
        Assert.That((dataset.PaymentArrangementCode, dataset.TransshipmentPermittedIndicator), Is.EqualTo(("PP", (bool?)true)));
        Assert.That((dataset.CodAmount, dataset.DeclaredValueForCarriageAmount, dataset.ConsignorProvidedInformationText, dataset.ConnectingCarrier),
            Is.EqualTo(((EftiAmount?)null, (EftiAmount?)null, (IReadOnlyList<string>?)null, (IReadOnlyList<EftiTradeParty>?)null)));
    }

    [Test]
    public void Map_DangerousGoods_GoUnderTheConsignmentItem()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1004");

        //SUT
        EftiConsignment dataset = EftiDatasetMapper.Map(shipment);

        EftiDangerousGoods dangerous = dataset.IncludedConsignmentItem![0].TransportDangerousGoods!.Single();
        Assert.That((dangerous.UnDgId, dangerous.HazardClassificationID, dangerous.PackagingDangerLevelCode), Is.EqualTo(("1263", "3", "2")));
        Assert.That(dangerous.ProperShippingName, Is.EqualTo(new[] { "PITTURE" }));
        Assert.That(dangerous.GrossWeight, Is.EqualTo(new EftiMeasure(4_200m, "KGM")));
        EftiLogisticsPackage package = dangerous.DangerousGoodsLogisticsPackage!.Single();
        Assert.That((package.ItemQuantity, package.TypeCode![0]), Is.EqualTo(((int?)20, "DR")));
        Assert.That(dataset.IncludedConsignmentItem[1].TransportDangerousGoods, Is.Null);
        Assert.That(dataset.MainCarriageTransportMovement!.Single().DangerousGoodsIndicator, Is.True);
    }

    [Test]
    public void Map_MissingAndWrongData_AreNeitherInventedNorCorrected()
    {
        //SETUP
        Shipment missing = SeedShipments.Create("TO-1002");
        Shipment wrong = SeedShipments.Create("TO-1001");
        wrong.Parties.Single(p => p.Role == PartyRole.Consignee).Party.CountryCode = "ITA";

        //SUT
        EftiConsignment missingDataset = EftiDatasetMapper.Map(missing);
        EftiConsignment wrongDataset = EftiDatasetMapper.Map(wrong);

        Assert.That(missingDataset.Consignee!.PostalAddress!.Postcode, Is.Null);
        Assert.That(missingDataset.Consignee.TaxRegistration, Is.Null);
        Assert.That(missingDataset.IncludedConsignmentItem![0].TransportDangerousGoods!.Single().UnDgId, Is.Null);
        Assert.That(missingDataset.CarrierAcceptanceDateTime, Is.EqualTo(new EftiDateTime("20261006", "102")), "non ancora ritirata: data prevista");
        Assert.That(missingDataset.ConsignorProvidedInformationText!.Single(), Does.Contain("UN 2014"));
        Assert.That(wrongDataset.Consignee!.PostalAddress!.CountryCode, Is.EqualTo("ITA"));
    }

    [Test]
    public void Map_SubcontractedTrip_AddsTheConnectingCarrier()
    {
        //SETUP
        string orderCode = SeedShipments.Orders.Values.First(o => o.Trip?.Subcontractor is not null).Order.Code;
        Shipment shipment = SeedShipments.Create(orderCode);

        //SUT
        EftiConsignment dataset = EftiDatasetMapper.Map(shipment);

        Assert.That(dataset.ConnectingCarrier, Has.Count.EqualTo(1));
        Assert.That(dataset.ConnectingCarrier![0].Name![0], Is.EqualTo(SeedShipments.Orders[orderCode].Trip!.Subcontractor!.Name));
    }

    [Test]
    public void Serialize_Dataset_UsesTheElementNamesOfTheSchema()
    {
        //SETUP
        Shipment shipment = SeedShipments.Create("TO-1004");
        shipment.CodAmount = 350m;
        EftiConsignment dataset = EftiDatasetMapper.Map(shipment);

        //SUT
        string json = EftiDatasetJson.Serialize(dataset);
        EftiConsignment roundTrip = EftiDatasetJson.Deserialize(json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("cODAmount").GetProperty("currencyId").GetString(), Is.EqualTo("EUR"));
        Assert.That(root.GetProperty("includedConsignmentItem")[0].GetProperty("transportDangerousGoods")[0].GetProperty("uNDGID").GetString(), Is.EqualTo("1263"));
        Assert.That(root.GetProperty("grossWeight")[0].GetProperty("unitId").GetString(), Is.EqualTo("KGM"));
        Assert.That(root.TryGetProperty("connectingCarrier", out _), Is.False, "ciò che manca non compare, nemmeno come null");
        Assert.That(EftiDatasetJson.Serialize(roundTrip), Is.EqualTo(json));
    }

    [Test]
    public void ShipmentCodeFor_OrderCode_KeepsTheNumber()
    {
        //SUT
        Assert.That(ShipmentFactory.ShipmentCodeFor("TO-2042"), Is.EqualTo("SH-2042"));
        Assert.That(ShipmentFactory.ShipmentCodeFor("X-7"), Is.EqualTo("SH-X-7"));
    }
}

using System.Globalization;
using Bogus;
using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Tms.Data.Entities;

namespace Dusiburg.AI.Trasporto.Tms.Data.Seed;

/// <summary>
/// Dati sintetici del TMS: prima gli ordini demo del <see cref="DemoCatalog"/>, poi la parte casuale con i difetti voluti.
/// A parità di seed e di data di riferimento il risultato è identico; le date sono relative a <c>today</c>, così "in viaggio"
/// e "consegnato la settimana scorsa" restano sensati nel tempo.
/// </summary>
public sealed class TmsSeedGenerator
{
    public const int DefaultRandomSeed = 20261005;

    /// <summary>Compresi i committenti demo.</summary>
    public const int Customers = 30;

    /// <summary>Oltre agli ordini demo.</summary>
    public const int GeneratedOrders = 150;

    /// <summary>Motrici e trattori, compresi quelli dei viaggi demo.</summary>
    public const int TractiveUnits = 26;

    /// <summary>Uno per trattore: quelli dei viaggi demo più quelli del resto della flotta, che ha anche due motrici senza rimorchio.</summary>
    public const int Trailers = TractiveUnits - TrucksWithoutTrailer;

    /// <summary>Ordini caricati sullo stesso viaggio (groupage).</summary>
    public const int MaxOrdersPerTrip = 3;

    /// <summary>Giorni entro cui si caricano gli ordini di uno stesso viaggio.</summary>
    public const int MaxLoadingSpreadDays = 1;

    public const int Drivers = 12;

    public const int FirstGeneratedOrderNumber = 2001;

    private const float InternationalShare = 0.45f;
    private const float ImportShare = 0.30f;
    private const float CustomerIsConsignorShare = 0.80f;
    private const float DangerousGoodsShare = 0.12f;
    private const float DefectShare = 0.35f;
    private const float SecondDefectShare = 0.25f;
    private const float SubcontractorShare = 0.05f;
    private const float JoinShare = 0.60f;
    private const int TrucksWithoutTrailer = 2;

    /// <summary>Fuso fisso dei timestamp sintetici: niente ora legale, basta che siano ripetibili.</summary>
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(1);

    private readonly Faker _faker;
    private readonly Randomizer _random;
    private readonly DateOnly _today;
    private readonly HashSet<string> _usedNames = [];
    private int _vatSerial = 1_000_000;

    private TmsSeedGenerator(int randomSeed, DateOnly today)
    {
        _faker = new Faker("it") { Random = new Randomizer(randomSeed) };
        _random = _faker.Random;
        _today = today;
    }

    public static TmsSeedData Generate(int randomSeed, DateOnly today)
    {
        var generator = new TmsSeedGenerator(randomSeed, today);

        List<Customer> customers = generator.CreateCustomers();
        List<Vehicle> vehicles = generator.CreateVehicles();
        List<Driver> drivers = generator.CreateDrivers();

        List<Trip> trips = [];
        List<TransportOrder> orders = [.. DemoCatalog.Orders.Select(demo => generator.CreateDemoOrder(demo, customers, vehicles, drivers, trips))];

        List<TransportOrder> generated = [.. Enumerable.Range(0, GeneratedOrders).Select(index => generator.CreateOrder(index, customers))];
        generator.AssignTrips(generated, vehicles, drivers, trips);
        Dictionary<string, IReadOnlyList<SeedDefect>> defects = generator.ApplyDefects(generated);

        orders.AddRange(generated);

        return new TmsSeedData(randomSeed, today, customers, vehicles, drivers, trips, orders, defects);
    }

    private List<Customer> CreateCustomers()
    {
        List<Customer> customers = [.. DemoCatalog.Customers.Select(demo => new Customer
        {
            Code = demo.Code,
            Name = demo.Name,
            VatNumber = demo.VatNumber,
            Street = demo.Street,
            PostalCode = demo.PostalCode,
            City = demo.City,
            CountryCode = demo.CountryCode,
            Email = demo.Email
        })];

        _usedNames.UnionWith(customers.Select(c => c.Name));

        while (customers.Count < Customers)
        {
            OrderParty company = NewItalianCompany();

            customers.Add(new Customer
            {
                Code = string.Create(CultureInfo.InvariantCulture, $"CUS-{customers.Count + 1:D3}"),
                Name = company.Name!,
                VatNumber = company.VatNumber,
                Street = company.Street!,
                PostalCode = company.PostalCode!,
                City = company.City!,
                CountryCode = company.CountryCode!,
                // Dominio riservato .example: non può esistere una casella reale.
                Email = string.Create(CultureInfo.InvariantCulture, $"ordini@cliente{customers.Count + 1:D3}.example")
            });
        }

        return customers;
    }

    /// <summary>Prima i mezzi dei viaggi demo, poi il resto della flotta: targhe <c>ZZ</c> (motrici) e <c>XZ</c> (rimorchi).</summary>
    private List<Vehicle> CreateVehicles()
    {
        List<Vehicle> vehicles = [];

        foreach (DemoTrip trip in DemoCatalog.Orders.Select(o => o.Trip))
        {
            vehicles.Add(NewVehicle(trip.TractorPlate, VehicleType.Tractor));
            vehicles.Add(NewVehicle(trip.TrailerPlate, VehicleType.SemiTrailer));
        }

        HashSet<string> plates = [.. vehicles.Select(v => v.PlateNumber)];
        int missingTractive = TractiveUnits - vehicles.Count(v => v.Type == VehicleType.Tractor);
        int missingTrailers = Trailers - vehicles.Count(v => v.Type == VehicleType.SemiTrailer);

        for (int i = 0; i < missingTractive; i++)
        {
            vehicles.Add(NewVehicle(NewPlate("ZZ", plates), i < TrucksWithoutTrailer ? VehicleType.Truck : VehicleType.Tractor));
        }

        for (int i = 0; i < missingTrailers; i++)
        {
            vehicles.Add(NewVehicle(NewPlate("XZ", plates), VehicleType.SemiTrailer));
        }

        return vehicles;
    }

    private static Vehicle NewVehicle(string plateNumber, VehicleType type) =>
        new() { PlateNumber = plateNumber, CountryCode = SeedCatalogs.Italy, Type = type };

    private string NewPlate(string prefix, HashSet<string> used)
    {
        while (true)
        {
            string letters = string.Concat(_random.String2(2, SeedCatalogs.PlateLetters));
            string plate = string.Create(CultureInfo.InvariantCulture, $"{prefix}{_random.Number(200, 999)}{letters}");

            if (used.Add(plate))
            {
                return plate;
            }
        }
    }

    private List<Driver> CreateDrivers() =>
    [
        .. Enumerable.Range(1, Drivers).Select(number => new Driver
        {
            Code = string.Create(CultureInfo.InvariantCulture, $"DRV-{number:D2}"),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName()
        })
    ];

    private TransportOrder CreateDemoOrder(
        DemoTransportOrder demo, List<Customer> customers, List<Vehicle> vehicles, List<Driver> drivers, List<Trip> trips)
    {
        DemoParty consignor = DemoCatalog.ConsignorOf(demo);
        DateOnly pickupDate = _today.AddDays(demo.PickupOffsetDays);

        var trip = new Trip
        {
            Code = demo.Trip.Code,
            Vehicle = vehicles.Single(v => v.PlateNumber == demo.Trip.TractorPlate),
            Trailer = vehicles.Single(v => v.PlateNumber == demo.Trip.TrailerPlate),
            Driver = drivers.Single(d => d.Code == demo.Trip.DriverCode),
            DepartureDate = pickupDate
        };

        trips.Add(trip);

        return new TransportOrder
        {
            Code = demo.Code,
            Customer = customers.Single(c => c.Code == demo.CustomerCode),
            OrderDate = pickupDate.AddDays(-3),
            Status = demo.Status,
            Consignor = ToOrderParty(consignor),
            Consignee = ToOrderParty(demo.Consignee),
            Pickup = PlaceOf(ToOrderParty(consignor)),
            Delivery = PlaceOf(ToOrderParty(demo.Consignee)),
            PickupDate = pickupDate,
            DeliveryDate = _today.AddDays(demo.DeliveryOffsetDays),
            PickedUpAt = demo.Status == TransportOrderStatus.Planned ? null : At(pickupDate, 8, 30),
            DeliveredAt = demo.DeliveredOffsetDays is { } delivered ? At(_today.AddDays(delivered), 10, 15) : null,
            TotalPackages = demo.LinesPackageCount,
            TotalGrossWeightKg = demo.DeclaredGrossWeightKg ?? demo.LinesGrossWeightKg,
            CmrNumber = demo.CmrNumber,
            FreightAmount = demo.FreightAmount,
            FreightPayment = demo.FreightPayment,
            CurrencyCode = "EUR",
            CodAmount = demo.CodAmount,
            DeclaredValue = demo.DeclaredValue,
            TransshipmentAllowed = true,
            SenderInstructions = demo.SenderInstructions,
            Trip = trip,
            Lines = [.. demo.Lines.Select((line, index) => new TransportOrderLine
            {
                LineNumber = index + 1,
                Description = line.Description,
                PackageCount = line.PackageCount,
                PackageType = line.PackageType,
                GrossWeightKg = line.GrossWeightKg,
                VolumeM3 = line.VolumeM3,
                IsDangerousGoods = line.DangerousGoods is not null,
                UnNumber = line.DangerousGoods?.UnNumber,
                ProperShippingName = line.DangerousGoods?.ProperShippingName,
                AdrClass = line.DangerousGoods?.AdrClass,
                PackingGroup = line.DangerousGoods?.PackingGroup
            })]
        };
    }

    private static OrderParty ToOrderParty(DemoParty party) => new()
    {
        Name = party.Name,
        VatNumber = party.VatNumber,
        Street = party.Street,
        PostalCode = party.PostalCode,
        City = party.City,
        CountryCode = party.CountryCode
    };

    private TransportOrder CreateOrder(int index, List<Customer> customers)
    {
        Customer customer = Pick(customers);
        (OrderParty consignor, OrderParty consignee) = Parties(customer);
        bool international = consignor.CountryCode != consignee.CountryCode;
        TransportOrderStatus status = _random.WeightedRandom(
            [TransportOrderStatus.Planned, TransportOrderStatus.InTransit, TransportOrderStatus.Delivered, TransportOrderStatus.Cancelled],
            [0.20f, 0.20f, 0.55f, 0.05f]);
        (DateOnly pickupDate, DateOnly deliveryDate) = Dates(status, international);
        List<TransportOrderLine> lines = Lines();
        bool moving = status is TransportOrderStatus.InTransit or TransportOrderStatus.Delivered;

        return new TransportOrder
        {
            Code = string.Create(CultureInfo.InvariantCulture, $"TO-{FirstGeneratedOrderNumber + index}"),
            Customer = customer,
            OrderDate = pickupDate.AddDays(-_random.Number(1, 7)),
            Status = status,
            Consignor = consignor,
            Consignee = consignee,
            Pickup = PlaceOf(consignor),
            Delivery = PlaceOf(consignee),
            PickupDate = pickupDate,
            DeliveryDate = deliveryDate,
            PickedUpAt = moving ? At(pickupDate, _random.Number(7, 16), _random.Number(0, 59)) : null,
            // Un ordine consegnato su dieci arriva il giorno dopo: è un ritardo reale, non un difetto del dato.
            DeliveredAt = status == TransportOrderStatus.Delivered
                ? At(deliveryDate.AddDays(_random.Bool(0.10f) ? 1 : 0), _random.Number(8, 17), _random.Number(0, 59))
                : null,
            TotalPackages = lines.Sum(l => l.PackageCount),
            TotalGrossWeightKg = lines.Sum(l => l.GrossWeightKg),
            CmrNumber = international && moving ? string.Create(CultureInfo.InvariantCulture, $"CMR-{_random.Number(2_000_000, 9_999_999)}") : null,
            FreightAmount = _random.Number(18, 280) * 10m,
            FreightPayment = _random.Bool(0.75f) ? FreightPayment.Prepaid : FreightPayment.Collect,
            CurrencyCode = "EUR",
            CodAmount = _random.Bool(0.05f) ? _random.Number(5, 60) * 100m : null,
            DeclaredValue = _random.Bool(0.08f) ? _random.Number(10, 90) * 1_000m : null,
            TransshipmentAllowed = _random.Bool(0.80f),
            SenderInstructions = _random.Bool(0.20f) ? _random.ArrayElement(SeedCatalogs.SenderInstructions) : null,
            Lines = lines
        };
    }

    /// <summary>
    /// Nazionale, esportazione o importazione. Il committente è di norma il mittente; nelle importazioni è il destinatario.
    /// </summary>
    private (OrderParty Consignor, OrderParty Consignee) Parties(Customer customer)
    {
        OrderParty customerParty = new()
        {
            Name = customer.Name,
            VatNumber = customer.VatNumber,
            Street = customer.Street,
            PostalCode = customer.PostalCode,
            City = customer.City,
            CountryCode = customer.CountryCode
        };

        if (!_random.Bool(InternationalShare))
        {
            OrderParty consignor = _random.Bool(CustomerIsConsignorShare) ? customerParty : NewItalianCompany();

            return (consignor, NewItalianCompany());
        }

        OrderParty foreign = NewForeignCompany();

        return _random.Bool(ImportShare) ? (foreign, customerParty) : (customerParty, foreign);
    }

    /// <summary>Date coerenti con lo stato: consegnato nel passato, in viaggio a cavallo di oggi, pianificato nel futuro.</summary>
    private (DateOnly Pickup, DateOnly Delivery) Dates(TransportOrderStatus status, bool international)
    {
        int transitDays = international ? _random.Number(2, 5) : _random.Number(1, 2);

        DateOnly pickup = status switch
        {
            TransportOrderStatus.Delivered => _today.AddDays(-_random.Number(transitDays + 1, 60)),
            TransportOrderStatus.InTransit => _today.AddDays(-_random.Number(0, transitDays - 1)),
            TransportOrderStatus.Planned => _today.AddDays(_random.Number(1, 10)),
            _ => _today.AddDays(-_random.Number(1, 30))
        };

        return (pickup, pickup.AddDays(transitDays));
    }

    private List<TransportOrderLine> Lines()
    {
        List<TransportOrderLine> lines = [];
        int count = _random.Number(1, 4);

        for (int number = 1; number <= count; number++)
        {
            SeedGoods goods = _random.ArrayElement(SeedCatalogs.Goods);
            int packages = _random.Number(goods.MinPackages, goods.MaxPackages);

            lines.Add(new TransportOrderLine
            {
                LineNumber = number,
                Description = goods.Description,
                PackageCount = packages,
                PackageType = goods.PackageType,
                GrossWeightKg = packages * _random.Number(goods.KgPerPackage.Min, goods.KgPerPackage.Max),
                VolumeM3 = Math.Round(packages * goods.M3PerPackage, 1)
            });
        }

        if (_random.Bool(DangerousGoodsShare))
        {
            lines.Add(DangerousLine(lines.Count + 1));
        }

        return lines;
    }

    private TransportOrderLine DangerousLine(int lineNumber)
    {
        SeedDangerousGoods goods = _random.ArrayElement(SeedCatalogs.DangerousGoods);
        int packages = _random.Number(4, 24);

        return new TransportOrderLine
        {
            LineNumber = lineNumber,
            Description = goods.Description,
            PackageCount = packages,
            PackageType = goods.PackageType,
            GrossWeightKg = packages * _random.Number(20, 220),
            VolumeM3 = Math.Round(packages * 0.25m, 1),
            IsDangerousGoods = true,
            UnNumber = goods.UnNumber,
            ProperShippingName = goods.ProperShippingName,
            AdrClass = goods.AdrClass,
            PackingGroup = goods.PackingGroup
        };
    }

    /// <summary>
    /// Viaggi coerenti nel tempo per gli ordini che ne hanno bisogno: tutti quelli partiti e metà dei pianificati (gli annullati
    /// non ne hanno). Un mezzo fa <b>un viaggio alla volta</b>: riparte solo dal giorno dopo l'ultima consegna del viaggio
    /// precedente. Un viaggio porta da uno a <see cref="MaxOrdersPerTrip"/> ordini caricati a non più di
    /// <see cref="MaxLoadingSpreadDays"/> giorni l'uno dall'altro (groupage). Così a una certa ora su un mezzo c'è solo il
    /// carico di un viaggio: è ciò che un controllo su strada deve trovare.
    /// </summary>
    private void AssignTrips(List<TransportOrder> orders, List<Vehicle> vehicles, List<Driver> drivers, List<Trip> trips)
    {
        // I mezzi dei viaggi demo restano riservati a quelli: nello scenario del controllo su strada una targa demo deve
        // portare a una sola spedizione.
        HashSet<string> demoPlates = [.. DemoCatalog.Orders.SelectMany(o => new[] { o.Trip.TractorPlate, o.Trip.TrailerPlate })];
        Vehicle[] tractive = [.. vehicles.Where(v => v.Type != VehicleType.SemiTrailer && !demoPlates.Contains(v.PlateNumber))];
        Vehicle[] trailers = [.. vehicles.Where(v => v.Type == VehicleType.SemiTrailer && !demoPlates.Contains(v.PlateNumber))];

        // Ogni trattore ha il suo rimorchio: anche la targa del rimorchio porta a un solo viaggio alla volta.
        Dictionary<Vehicle, Vehicle> trailerOf = tractive
            .Where(v => v.Type == VehicleType.Tractor)
            .Select((tractor, index) => (tractor, Trailer: trailers[index]))
            .ToDictionary(pair => pair.tractor, pair => pair.Trailer);

        List<TransportOrder> needingTrip =
        [
            .. orders
                .Where(o => o.Status is TransportOrderStatus.InTransit or TransportOrderStatus.Delivered
                    || (o.Status == TransportOrderStatus.Planned && _random.Bool()))
                .OrderBy(o => o.PickupDate)
                .ThenBy(o => o.Code, StringComparer.Ordinal)
        ];

        // L'ultimo viaggio di ogni mezzo: è il solo a cui si può ancora aggiungere un ordine.
        var latest = new OpenTrip?[tractive.Length];

        foreach (TransportOrder order in needingTrip)
        {
            DateOnly start = order.PickupDate!.Value;
            int[] joinable = [.. Enumerable.Range(0, latest.Length).Where(i => latest[i] is { } t && t.Orders < t.Capacity && start <= t.Start.AddDays(MaxLoadingSpreadDays))];
            int[] free = [.. Enumerable.Range(0, latest.Length).Where(i => latest[i] is not { } t || t.End < start)];

            if (joinable.Length > 0 && (free.Length == 0 || _random.Bool(JoinShare)))
            {
                latest[_random.ArrayElement(joinable)]!.Add(order);
            }
            else if (free.Length > 0)
            {
                int index = _random.ArrayElement(free);

                latest[index] = NewTrip(order, tractive[index], trailerOf, drivers, trips);
            }
            else
            {
                // Flotta tutta impegnata e nessun carico aperto: l'ordine sale sul viaggio partito più di recente. Meglio un
                // carico più grande che due viaggi dello stesso mezzo sovrapposti.
                latest.OfType<OpenTrip>().OrderByDescending(t => t.Start).ThenBy(t => t.Trip.Code, StringComparer.Ordinal).First().Add(order);
            }
        }
    }

    private OpenTrip NewTrip(TransportOrder order, Vehicle vehicle, Dictionary<Vehicle, Vehicle> trailerOf, List<Driver> drivers, List<Trip> trips)
    {
        OrderParty? subcontractor = _random.Bool(SubcontractorShare) ? NewForeignCompany() : null;

        var trip = new Trip
        {
            Code = string.Create(CultureInfo.InvariantCulture, $"TR-{FirstGeneratedOrderNumber + trips.Count - DemoCatalog.Orders.Count}"),
            Vehicle = vehicle,
            Trailer = trailerOf.GetValueOrDefault(vehicle),
            Driver = Pick(drivers),
            DepartureDate = order.PickupDate!.Value,
            SubcontractorName = subcontractor?.Name,
            SubcontractorVatNumber = subcontractor?.VatNumber,
            SubcontractorCountryCode = subcontractor?.CountryCode
        };

        trips.Add(trip);

        var open = new OpenTrip(trip, _random.Number(1, MaxOrdersPerTrip));

        open.Add(order);

        return open;
    }

    /// <summary>Un viaggio in costruzione: quanti ordini può ancora prendere e fino a quando tiene impegnato il mezzo.</summary>
    private sealed class OpenTrip(Trip trip, int capacity)
    {
        public Trip Trip { get; } = trip;

        public int Capacity { get; } = capacity;

        public int Orders { get; private set; }

        public DateOnly Start => Trip.DepartureDate;

        /// <summary>Ultimo giorno in cui il mezzo ha ancora a bordo qualcosa di questo viaggio.</summary>
        public DateOnly End { get; private set; }

        public void Add(TransportOrder order)
        {
            DateOnly pickup = order.PickupDate!.Value;
            DateOnly delivery = order.DeliveredAt is { } delivered ? DateOnly.FromDateTime(delivered.DateTime) : order.DeliveryDate ?? pickup;
            DateOnly end = delivery < pickup ? pickup : delivery;

            order.Trip = Trip;
            Orders++;
            End = Orders == 1 || end > End ? end : End;
        }
    }

    private Dictionary<string, IReadOnlyList<SeedDefect>> ApplyDefects(List<TransportOrder> orders)
    {
        Dictionary<string, IReadOnlyList<SeedDefect>> applied = [];
        SeedDefect[] all = Enum.GetValues<SeedDefect>();

        foreach (TransportOrder order in orders.Where(_ => _random.Bool(DefectShare)))
        {
            SeedDefect[] defects = [.. _random.Shuffle(all).Take(_random.Bool(SecondDefectShare) ? 2 : 1)];

            foreach (SeedDefect defect in defects)
            {
                Apply(order, defect);
            }

            applied[order.Code] = defects;
        }

        return applied;
    }

    private void Apply(TransportOrder order, SeedDefect defect)
    {
        TransportOrderLine first = order.Lines[0];

        switch (defect)
        {
            case SeedDefect.MissingConsigneeAddress:
                order.Consignee.PostalCode = order.Consignee.City = null;
                order.Delivery.PostalCode = order.Delivery.City = null;
                break;

            case SeedDefect.MissingGoodsDescription:
                first.Description = null;
                break;

            case SeedDefect.MissingPackageCount:
                order.TotalPackages -= first.PackageCount;
                first.PackageCount = 0;
                break;

            case SeedDefect.WeightMismatch:
                decimal factor = _random.Bool() ? _random.Decimal(0.50m, 0.90m) : _random.Decimal(1.10m, 1.60m);
                order.TotalGrossWeightKg = Math.Round(order.Lines.Sum(l => l.GrossWeightKg) * factor, 0);
                break;

            case SeedDefect.AdrWithoutUnNumber:
                TransportOrderLine dangerous = order.Lines.FirstOrDefault(l => l.IsDangerousGoods) ?? first;
                dangerous.IsDangerousGoods = true;
                dangerous.AdrClass ??= "3";
                dangerous.UnNumber = dangerous.ProperShippingName = null;
                break;

            case SeedDefect.InvalidCountryCode:
                order.Consignee.CountryCode = order.Delivery.CountryCode = _random.ArrayElement(SeedCatalogs.InvalidCountryCodes);
                break;

            case SeedDefect.DeliveryBeforePickup:
                order.DeliveryDate = order.PickupDate!.Value.AddDays(-_random.Number(1, 3));
                break;

            default:
                throw new InvalidOperationException($"Difetto {defect} non gestito.");
        }
    }

    private OrderParty NewItalianCompany()
    {
        SeedCity city = _random.ArrayElement(SeedCatalogs.ItalianCities);

        return new OrderParty
        {
            Name = UniqueName(() =>
                $"{_faker.Name.LastName()} {_random.ArrayElement(SeedCatalogs.ItalianActivities)} {_random.ArrayElement(SeedCatalogs.ItalianLegalForms)}"),
            VatNumber = ItalianVat.Create(_vatSerial++),
            Street = $"{_random.ArrayElement(SeedCatalogs.ItalianStreets)} {_random.Number(1, 180)}",
            PostalCode = city.PostalCode,
            City = city.City,
            CountryCode = SeedCatalogs.Italy
        };
    }

    private OrderParty NewForeignCompany()
    {
        SeedCountry country = _random.ArrayElement(SeedCatalogs.ForeignCountries);
        SeedCity city = _random.ArrayElement(country.Cities);
        string digits = _random.ReplaceNumbers(new string('#', country.VatDigits));

        return new OrderParty
        {
            Name = UniqueName(() =>
                string.Format(CultureInfo.InvariantCulture, country.NamePattern, _random.ArrayElement(country.Words), _random.ArrayElement(country.Surnames))
                + $" {city.City}"),
            VatNumber = $"{country.VatPrefix}999{digits}{country.VatSuffix}",
            Street = $"{_random.ArrayElement(country.Streets)} {_random.Number(1, 180)}",
            PostalCode = city.PostalCode,
            City = city.City,
            CountryCode = country.Code
        };
    }

    /// <summary>Le liste da cui si compongono i nomi sono corte: dopo qualche tentativo si distingue con un numero progressivo.</summary>
    private string UniqueName(Func<string> create)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string name = create();

            if (_usedNames.Add(name))
            {
                return name;
            }
        }

        string numbered = string.Create(CultureInfo.InvariantCulture, $"{create()} {_usedNames.Count + 1}");
        _usedNames.Add(numbered);

        return numbered;
    }

    private static OrderPlace PlaceOf(OrderParty party) =>
        new() { Street = party.Street, PostalCode = party.PostalCode, City = party.City, CountryCode = party.CountryCode };

    private static DateTimeOffset At(DateOnly date, int hour, int minute) => new(date.ToDateTime(new TimeOnly(hour, minute)), LocalOffset);

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Number(0, items.Count - 1)];
}

using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Tms.Api.Views;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;

/// <summary>
/// Gli ordini del seed del TMS (a data fissa) trasformati in spedizioni come farebbe l'importazione: vista di dettaglio di
/// <c>Tms.Api</c> e poi <see cref="ShipmentFactory"/>. Nessun database.
/// </summary>
internal static class SeedShipments
{
    public static readonly DateOnly Today = new(2026, 10, 5);

    public static readonly DateTimeOffset ImportedAt = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    public static TmsSeedData Seed { get; } = TmsSeedGenerator.Generate(TmsSeedGenerator.DefaultRandomSeed, Today);

    /// <summary>Viste di dettaglio degli ordini non annullati, per codice dell'ordine.</summary>
    public static IReadOnlyDictionary<string, TransportOrderDetailView> Orders { get; } = Seed.Orders
        .Where(o => o.Status != TransportOrderStatus.Cancelled)
        .ToDictionary(o => o.Code, TmsViewQueries.ToDetail);

    /// <summary>Una spedizione nuova a ogni chiamata: i test possono modificarla senza influenzarsi.</summary>
    public static Shipment Create(string orderCode) => Create(Orders[orderCode]);

    public static Shipment Create(TransportOrderDetailView order, CarrierOptions? carrier = null) =>
        ShipmentFactory.Create(order, carrier ?? new CarrierOptions(), ImportedAt);

    public static IEnumerable<Shipment> All() => Orders.Values.Select(order => Create(order));
}

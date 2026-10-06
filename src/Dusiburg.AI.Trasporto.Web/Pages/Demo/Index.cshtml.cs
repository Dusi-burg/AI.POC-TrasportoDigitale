using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Web.Api;
using Dusiburg.AI.Trasporto.Web.Demo;

namespace Dusiburg.AI.Trasporto.Web.Pages.Demo;

/// <summary>Un ordine demo con ciò che ne è stato: la spedizione importata, se c'è, e i suoi CMR sintetici.</summary>
public sealed record DemoOrderRow(DemoTransportOrder Order, DemoCustomer Customer, ShipmentSummaryView? Shipment, IReadOnlyList<string> CmrCodes);

/// <summary>
/// I dati della demo in un colpo d'occhio: i sei ordini del TMS, gli scenari del piano che li usano, la spedizione che ne è
/// nata e i CMR da caricare. Il catalogo è lo stesso da cui nasce il seed.
/// </summary>
public sealed class IndexModel(EftiApiClient api, DemoCmrFiles demoCmr) : ApiPageModel
{
    public IReadOnlyList<DemoOrderRow> Orders { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        List<ShipmentSummaryView> shipments = await LoadAsync(() => api.ListShipmentsAsync(new ShipmentSearch(null, null, null, null), cancellationToken)) ?? [];

        Orders = [.. DemoCatalog.Orders.Select(order => new DemoOrderRow(
            order,
            DemoCatalog.CustomerOf(order),
            shipments.FirstOrDefault(s => s.TmsOrderCode == order.Code),
            demoCmr.CodesFor(order.Code["TO-".Length..])))];
    }
}

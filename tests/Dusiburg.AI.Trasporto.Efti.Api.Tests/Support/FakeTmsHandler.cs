using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Dusiburg.AI.Trasporto.Tms.Api.Views;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;

/// <summary>
/// TMS finto per i test di importazione: risponde alle due viste che <c>Efti.Api</c> usa, con gli ordini del seed e le viste
/// vere di <c>Tms.Api</c>. Nessun processo e nessun database del TMS.
/// </summary>
internal sealed class FakeTmsHandler(TmsSeedData seed) : HttpMessageHandler
{
    private const string OrdersPath = "/api/views/orders";

    private readonly Dictionary<string, TransportOrderDetailView> _orders = seed.Orders.ToDictionary(o => o.Code, TmsViewQueries.ToDetail);

    public IReadOnlyDictionary<string, TransportOrderDetailView> Orders => _orders;

    /// <summary>Quando è <c>true</c> il TMS non risponde, come se il servizio fosse fermo.</summary>
    public bool Unavailable { get; set; }

    public int DetailRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Unavailable)
        {
            throw new HttpRequestException("TMS finto non raggiungibile.");
        }

        string path = request.RequestUri!.AbsolutePath;

        if (path == OrdersPath)
        {
            return Task.FromResult(Json(_orders.Values.Select(o => o.Order).ToList()));
        }

        if (path.StartsWith(OrdersPath + "/", StringComparison.Ordinal))
        {
            DetailRequests++;

            return Task.FromResult(_orders.TryGetValue(Uri.UnescapeDataString(path[(OrdersPath.Length + 1)..]), out TransportOrderDetailView? order)
                ? Json(order)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: TrasportoJson.Options) };
}

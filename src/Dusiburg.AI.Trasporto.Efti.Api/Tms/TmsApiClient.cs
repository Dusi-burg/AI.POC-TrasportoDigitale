using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Serialization;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tms;

/// <summary>Client tipizzato verso <c>Tms.Api</c>: l'importazione legge gli ordini con una chiamata HTTP (P2), non dal database del TMS.</summary>
public sealed class TmsApiClient(HttpClient http)
{
    public const string BaseAddressConfigurationKey = "TmsApi:BaseAddress";

    /// <summary>Nome della risorsa nell'AppHost, risolto dal service discovery.</summary>
    public const string DefaultBaseAddress = "http://tms-api";

    /// <summary>Gli ordini del TMS, al più quanti ne restituisce la vista (200).</summary>
    public async Task<IReadOnlyList<TransportOrderSummaryView>> ListOrdersAsync(CancellationToken cancellationToken)
    {
        List<TransportOrderSummaryView>? orders = await http.GetFromJsonAsync<List<TransportOrderSummaryView>>(
            "api/views/orders", TrasportoJson.Options, cancellationToken);

        return orders ?? [];
    }

    /// <summary>Dettaglio di un ordine; <c>null</c> se il TMS non lo conosce.</summary>
    public async Task<TransportOrderDetailView?> FindOrderAsync(string code, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync($"api/views/orders/{Uri.EscapeDataString(code)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TransportOrderDetailView>(TrasportoJson.Options, cancellationToken);
    }
}

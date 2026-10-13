using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Tms.Api.Views;

/// <summary>Letture del TMS: solo GET, su <c>/api/views</c>.</summary>
internal static class TmsViewEndpoints
{
    public static RouteGroupBuilder MapTmsViewEndpoints(this RouteGroupBuilder api)
    {
        var views = api.MapGroup("/views");

        views.MapGet("/orders", ListOrdersAsync);
        views.MapGet("/orders/{code}", GetOrderAsync);

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<TransportOrderSummaryView>>, ProblemHttpResult>> ListOrdersAsync(
        string? status, string? customerCode, TmsViewQueries queries, CancellationToken cancellationToken)
    {
        TransportOrderStatus? statusFilter = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status, ignoreCase: true, out TransportOrderStatus parsed) || !Enum.IsDefined(parsed) || int.TryParse(status, out _))
            {
                return ApiProblems.Validation($"Filtro status non valido: ammessi {string.Join(", ", Enum.GetNames<TransportOrderStatus>())}.");
            }

            statusFilter = parsed;
        }

        var filter = new TransportOrderListFilter(statusFilter, string.IsNullOrWhiteSpace(customerCode) ? null : customerCode.Trim());

        return TypedResults.Ok(await queries.ListOrdersAsync(filter, cancellationToken));
    }

    private static async Task<Results<Ok<TransportOrderDetailView>, ProblemHttpResult>> GetOrderAsync(
        string code, TmsViewQueries queries, CancellationToken cancellationToken)
    {
        TransportOrderDetailView? order = await queries.FindOrderAsync(code, cancellationToken);

        return order is null ? ApiProblems.NotFound($"Ordine di trasporto {code} non trovato.") : TypedResults.Ok(order);
    }
}

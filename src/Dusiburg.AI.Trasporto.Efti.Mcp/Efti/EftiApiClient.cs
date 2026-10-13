using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Mcp.Hosting;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Polly;

namespace Dusiburg.AI.Trasporto.Efti.Mcp.Efti;

/// <summary>
/// Client tipizzato verso <c>Efti.Api</c>. Ogni richiesta porta la chiave <c>X-Api-Key</c> di chi ha chiamato il tool: il server
/// MCP non ha una propria identità, quindi un agente non ha mai più permessi di chi lo usa. ProblemDetails, codici HTTP e
/// mancate risposte diventano <see cref="ToolException"/> con i codici di <see cref="ErrorCodes"/>.
/// </summary>
public sealed class EftiApiClient(HttpClient http, IHttpContextAccessor caller) : ICallerPermissions
{
    public const string BaseAddressConfigurationKey = "EftiApi:BaseAddress";

    /// <summary>Risorsa <c>efti-api</c> dell'AppHost, risolta dal service discovery.</summary>
    public const string DefaultBaseAddress = "http://efti-api";

    public async Task<IReadOnlySet<Operation>> AllowedOperationsAsync(CancellationToken cancellationToken)
    {
        ActorView actor = await GetAsync<ActorView>("api/me", cancellationToken);

        return actor.AllowedOperations.ToHashSet();
    }

    public Task<List<ShipmentSummaryView>> SearchShipmentsAsync(
        string? status, string? customer, string? fromDate, string? toDate, CancellationToken cancellationToken) =>
        GetAsync<List<ShipmentSummaryView>>(
            QueryHelpers.AddQueryString("api/views/shipments", new Dictionary<string, string?>
            {
                ["status"] = status,
                ["customer"] = customer,
                ["from"] = fromDate,
                ["to"] = toDate
            }),
            cancellationToken);

    public Task<ShipmentDetailView> GetShipmentAsync(string shipmentCode, CancellationToken cancellationToken) =>
        GetAsync<ShipmentDetailView>($"api/views/shipments/{Uri.EscapeDataString(shipmentCode)}", cancellationToken);

    public Task<TransportOrderDetailView> GetTmsOrderAsync(string shipmentCode, CancellationToken cancellationToken) =>
        GetAsync<TransportOrderDetailView>($"api/shipments/{Uri.EscapeDataString(shipmentCode)}/tms-order", cancellationToken);

    public async Task<ProposeFieldResponse> ProposeAsync(string shipmentCode, ProposeFieldRequest proposal, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/shipments/{Uri.EscapeDataString(shipmentCode)}/proposals")
        {
            Content = JsonContent.Create(proposal, options: TrasportoJson.Options)
        };

        return await SendAsync<ProposeFieldResponse>(request, cancellationToken);
    }

    /// <param name="at">Momento del controllo; <c>null</c> = adesso. È l'API a decidere se un altro momento è ammesso.</param>
    public Task<List<ConsignmentIdentifiersView>> FindIdentifiersAsync(string plate, string? at, CancellationToken cancellationToken) =>
        GetAsync<List<ConsignmentIdentifiersView>>(
            QueryHelpers.AddQueryString("api/gate/identifiers", new Dictionary<string, string?> { ["plate"] = plate, ["at"] = string.IsNullOrWhiteSpace(at) ? null : at }),
            cancellationToken);

    public Task<AuthoritySubsetView> GetSubsetAsync(string uil, string subsetId, CancellationToken cancellationToken) =>
        GetAsync<AuthoritySubsetView>(
            QueryHelpers.AddQueryString("api/gate/subset", new Dictionary<string, string?> { ["uil"] = uil, ["subsetId"] = subsetId }), cancellationToken);

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // La chiave è quella della richiesta MCP in corso, già riconosciuta all'ingresso dell'endpoint /mcp.
        if ((string?)caller.HttpContext?.Request.Headers[ActorResolver.ApiKeyHeaderName] is { } apiKey)
        {
            request.Headers.Add(ActorResolver.ApiKeyHeaderName, apiKey);
        }

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (IsUnavailable(exception, cancellationToken))
        {
            throw new ToolException(ErrorCodes.UpstreamUnavailable, "Efti.Api non raggiungibile o non ha risposto in tempo.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ToToolExceptionAsync(response, cancellationToken);
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(TrasportoJson.Options, cancellationToken)
                    ?? throw new JsonException("Risposta vuota.");
            }
            catch (JsonException exception)
            {
                throw new ToolException(ErrorCodes.Internal, "Risposta non valida da Efti.Api.", exception);
            }
        }
    }

    /// <summary>Connessione fallita, timeout o circuito aperto dopo i tentativi della resilienza di ServiceDefaults.</summary>
    private static bool IsUnavailable(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or ExecutionRejectedException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static async Task<ToolException> ToToolExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int status = (int)response.StatusCode;

        // Solo gli errori di dominio portano al modello il dettaglio dell'API; quelli di infrastruttura restano generici.
        return response.StatusCode switch
        {
            HttpStatusCode.BadRequest => new ToolException(
                ErrorCodes.ValidationError, await ReadDetailAsync(response, cancellationToken) ?? "Richiesta non valida."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ToolException(
                ErrorCodes.Unauthorized, await ReadDetailAsync(response, cancellationToken) ?? "Operazione non consentita."),
            HttpStatusCode.NotFound => new ToolException(
                ErrorCodes.NotFound, await ReadDetailAsync(response, cancellationToken) ?? "Risorsa non trovata."),
            HttpStatusCode.Conflict => new ToolException(
                ErrorCodes.Conflict, await ReadDetailAsync(response, cancellationToken) ?? "Conflitto con i dati esistenti."),
            >= HttpStatusCode.InternalServerError => new ToolException(
                ErrorCodes.UpstreamUnavailable, string.Create(CultureInfo.InvariantCulture, $"Efti.Api non disponibile (HTTP {status}).")),
            _ => new ToolException(
                ErrorCodes.Internal, string.Create(CultureInfo.InvariantCulture, $"Risposta inattesa da Efti.Api (HTTP {status})."))
        };
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);

            return string.IsNullOrWhiteSpace(problem?.Detail) ? null : problem.Detail;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ProblemBody(string? Detail);
}

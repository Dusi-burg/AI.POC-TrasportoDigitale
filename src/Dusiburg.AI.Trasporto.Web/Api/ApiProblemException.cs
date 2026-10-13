using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Serialization;

namespace Dusiburg.AI.Trasporto.Web.Api;

/// <summary>
/// Risposta di errore di un servizio a valle (<c>ProblemDetails</c> con l'estensione <c>code</c>), con titolo e dettaglio già
/// in italiano: la pagina li mostra così come sono. Un rifiuto per ruolo è un'informazione per chi guarda la demo, non un guasto.
/// </summary>
public sealed class ApiProblemException(int status, string code, string title, string detail) : Exception($"{title}: {detail}")
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    /// <summary>Il ruolo scelto non può fare questa operazione, oppure la sua chiave non è configurata.</summary>
    public bool IsRefusal => Status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden;

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await ThrowIfErrorAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(TrasportoJson.Options, cancellationToken)
            ?? throw new ApiProblemException((int)response.StatusCode, ErrorCodes.Internal, "Risposta vuota", $"Corpo vuoto da {response.RequestMessage?.RequestUri}.");
    }

    public static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        Problem? problem = null;

        try
        {
            problem = await response.Content.ReadFromJsonAsync<Problem>(TrasportoJson.Options, cancellationToken);
        }
        catch (JsonException)
        {
            // Corpo non JSON (errore prima dell'API): resta il codice HTTP.
        }

        throw new ApiProblemException(
            (int)response.StatusCode,
            problem?.Code ?? ErrorCodes.Internal,
            problem?.Title ?? "Errore del servizio",
            problem?.Detail ?? $"{(int)response.StatusCode} {response.ReasonPhrase}");
    }

    private sealed record Problem(string? Title, string? Detail, string? Code);
}

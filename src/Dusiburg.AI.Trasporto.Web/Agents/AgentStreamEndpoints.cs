using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Web.Api;
using Microsoft.AspNetCore.Antiforgery;

namespace Dusiburg.AI.Trasporto.Web.Agents;

/// <summary>
/// Gli stream degli agenti per gli script delle pagine: il browser non vede il servizio <c>Agents</c>, quindi la Web aggiunge la
/// chiave del ruolo scelto e inoltra lo stream così com'è (<c>step</c>, poi <c>result</c> oppure <c>error</c>). Un errore prima
/// che lo stream parta torna con il suo codice HTTP e lo stesso ProblemDetails.
/// </summary>
public static class AgentStreamEndpoints
{
    public const string MappingPath = "/agents/mapping/stream";

    public const string AuthorityPath = "/agents/authority/stream";

    public static IEndpointRouteBuilder MapAgentStreams(this IEndpointRouteBuilder app)
    {
        app.MapPost(MappingPath, (MappingRunRequest request, AgentsApiClient agents, IAntiforgery antiforgery, HttpContext http) =>
            ForwardAsync(AgentsApiClient.MappingStreamPath, request, agents, antiforgery, http));

        app.MapPost(AuthorityPath, (AuthorityAskRequest request, AgentsApiClient agents, IAntiforgery antiforgery, HttpContext http) =>
            ForwardAsync(AgentsApiClient.AuthorityStreamPath, request, agents, antiforgery, http));

        return app;
    }

    private static async Task ForwardAsync(string path, object request, AgentsApiClient agents, IAntiforgery antiforgery, HttpContext http)
    {
        // Avviare un agente costa ed esegue tool con il ruolo di chi guarda la pagina: solo le pagine della Web possono chiederlo.
        if (!await antiforgery.IsRequestValidAsync(http))
        {
            await ApiProblems.Validation("Richiesta non valida: ricaricare la pagina e riprovare.").ExecuteAsync(http);

            return;
        }

        CancellationToken cancellationToken = http.RequestAborted;
        HttpResponseMessage upstream;

        try
        {
            upstream = await agents.OpenStreamAsync(path, request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            await ApiProblems.UpstreamUnavailable($"Il servizio degli agenti non risponde: {exception.Message}").ExecuteAsync(http);

            return;
        }

        using (upstream)
        {
            http.Response.StatusCode = (int)upstream.StatusCode;
            http.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json";
            http.Response.Headers.CacheControl = "no-cache";

            await using Stream body = await upstream.Content.ReadAsStreamAsync(cancellationToken);

            byte[] buffer = new byte[8 * 1024];
            int read;

            // Ogni pezzo ricevuto parte subito verso il browser: i passi si vedono mentre accadono.
            while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await http.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await http.Response.Body.FlushAsync(cancellationToken);
            }
        }
    }
}

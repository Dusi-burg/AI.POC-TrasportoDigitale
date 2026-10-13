using Dusiburg.AI.Trasporto.Efti.Core.Security;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.ServiceDefaults.Security;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Efti.Api.Security;

/// <summary>
/// Il punto unico da cui passa ogni operazione di <c>Efti.Api</c>: riconosce l'attore dalla chiave, consulta la matrice dei
/// ruoli, lascia eseguire l'operazione e ne registra l'esito nell'audit — riuscita, fallita o rifiutata. Se l'audit non si può
/// scrivere la richiesta fallisce: nessuna operazione senza traccia.
/// </summary>
/// <param name="resource">Come ricavare dalla richiesta la risorsa su cui si opera (codice, UIL, targa).</param>
internal sealed class AccessFilter(Operation operation, Func<HttpContext, string?>? resource) : IEndpointFilter
{
    private static readonly object ActorItemKey = new();

    /// <summary>L'attore riconosciuto per la richiesta in corso: disponibile agli endpoint dopo il filtro.</summary>
    public static Actor ActorOf(HttpContext http) =>
        http.Items[ActorItemKey] as Actor ?? throw new InvalidOperationException("Nessun attore: l'endpoint non dichiara la propria operazione.");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        AuditTrail audit = http.RequestServices.GetRequiredService<AuditTrail>();
        ActorResolver resolver = http.RequestServices.GetRequiredService<ActorResolver>();
        CancellationToken cancellationToken = http.RequestAborted;

        string? target = resource?.Invoke(http);
        string? apiKey = http.Request.Headers[ActorResolver.ApiKeyHeaderName];
        Actor? actor = resolver.Resolve(apiKey);

        if (actor is null)
        {
            string reason = string.IsNullOrWhiteSpace(apiKey)
                ? $"Intestazione {ActorResolver.ApiKeyHeaderName} assente."
                : $"Chiave {ActorResolver.ApiKeyHeaderName} non riconosciuta.";

            await audit.RecordAsync(null, operation, target, AuditOutcome.Denied, reason, cancellationToken);

            return ApiProblems.Unauthorized(reason);
        }

        if (!AuthorizationPolicy.IsAllowed(actor.Role, operation))
        {
            string reason = $"Il ruolo {actor.Role} non può eseguire {operation}.";

            await audit.RecordAsync(actor, operation, target, AuditOutcome.Denied, reason, cancellationToken);

            return ApiProblems.Forbidden(reason);
        }

        http.Items[ActorItemKey] = actor;

        object? result;

        try
        {
            result = await next(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await audit.RecordAsync(actor, operation, target, AuditOutcome.Failed, $"Errore non gestito: {exception.GetType().Name}.", cancellationToken);

            throw;
        }

        (int status, string? detail) = Describe(result);

        await audit.RecordAsync(
            actor, operation, target,
            status < StatusCodes.Status400BadRequest ? AuditOutcome.Success : AuditOutcome.Failed,
            status < StatusCodes.Status400BadRequest ? null : $"{status}: {detail}",
            cancellationToken);

        return result;
    }

    /// <summary>Stato HTTP della risposta dell'endpoint e, per un errore, il suo dettaglio.</summary>
    private static (int Status, string? Detail) Describe(object? result) => result switch
    {
        INestedHttpResult nested => Describe(nested.Result),
        ProblemHttpResult problem => (problem.StatusCode, problem.ProblemDetails.Detail),
        IStatusCodeHttpResult withStatus => (withStatus.StatusCode ?? StatusCodes.Status200OK, null),
        _ => (StatusCodes.Status200OK, null)
    };
}

internal static class AccessFilterExtensions
{
    /// <summary>
    /// Dichiara l'operazione dell'endpoint: da quel momento passa da <see cref="AccessFilter"/>. L'operazione resta nei
    /// metadati dell'endpoint, così un test può verificare che nessun endpoint ne sia privo.
    /// </summary>
    public static RouteHandlerBuilder RequireOperation(
        this RouteHandlerBuilder endpoint, Operation operation, Func<HttpContext, string?>? resource = null) =>
        endpoint.AddEndpointFilter(new AccessFilter(operation, resource)).WithMetadata(new OperationMetadata(operation));

    public static string? RouteValue(this HttpContext http, string name) => http.Request.RouteValues[name]?.ToString();

    public static string? QueryValue(this HttpContext http, string name) => http.Request.Query[name];
}

/// <summary>Metadato che lega un endpoint alla sua operazione.</summary>
public sealed record OperationMetadata(Operation Operation);

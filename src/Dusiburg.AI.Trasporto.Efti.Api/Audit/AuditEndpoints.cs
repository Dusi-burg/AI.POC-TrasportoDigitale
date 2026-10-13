using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Core.Security;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Efti.Api.Audit;

/// <summary>Identità di chi chiama, registro di audit e accessi dell'autorità visibili all'operatore (D8).</summary>
internal static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (HttpContext http) =>
            {
                Actor actor = AccessFilter.ActorOf(http);

                return TypedResults.Ok(new ActorView(actor.Id, actor.Name, actor.Role, AuthorizationPolicy.AllowedOperations(actor.Role)));
            })
            .RequireOperation(Operation.IdentityRead);

        api.MapGet("/audit", ListAsync)
            .RequireOperation(Operation.AuditRead);

        api.MapGet("/shipments/{code}/authority-access", ListAuthorityAccessesAsync)
            .RequireOperation(Operation.AuthorityAccessRead, http => http.RouteValue("code"));

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<AuditEventView>>, ProblemHttpResult>> ListAsync(
        string? role, string? operation, string? outcome, string? shipmentCode, string? resource, AuditQueries queries, CancellationToken cancellationToken)
    {
        if (!TryParseFilter(role, out Role? roleFilter)
            || !TryParseFilter(operation, out Operation? operationFilter)
            || !TryParseFilter(outcome, out AuditOutcome? outcomeFilter))
        {
            return ApiProblems.Validation(
                $"Filtro non valido. role: {string.Join(", ", Enum.GetNames<Role>())}; operation: {string.Join(", ", Enum.GetNames<Operation>())}; " +
                $"outcome: {string.Join(", ", Enum.GetNames<AuditOutcome>())}.");
        }

        var filter = new AuditFilter(roleFilter, operationFilter, outcomeFilter, NonEmpty(shipmentCode), NonEmpty(resource));

        return TypedResults.Ok(await queries.ListAsync(filter, cancellationToken));
    }

    private static async Task<Results<Ok<IReadOnlyList<AuditEventView>>, ProblemHttpResult>> ListAuthorityAccessesAsync(
        string code, AuditQueries queries, AuditNote note, CancellationToken cancellationToken)
    {
        IReadOnlyList<AuditEventView>? accesses = await queries.ListAuthorityAccessesAsync(code, cancellationToken);

        if (accesses is null)
        {
            return ApiProblems.NotFound($"Spedizione {code} non trovata.");
        }

        note.ShipmentCode = code;

        return TypedResults.Ok(accesses);
    }

    /// <summary>Un filtro assente non filtra; un valore che non è il nome di un membro dell'enum è un errore.</summary>
    private static bool TryParseFilter<TEnum>(string? value, out TEnum? parsed) where TEnum : struct, Enum
    {
        parsed = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out TEnum member) || !Enum.IsDefined(member) || int.TryParse(value, out _))
        {
            return false;
        }

        parsed = member;

        return true;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

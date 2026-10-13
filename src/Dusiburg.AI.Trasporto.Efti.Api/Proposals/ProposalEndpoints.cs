using System.Globalization;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Efti.Api.Proposals;

/// <summary>
/// Proposte di completamento: chiunque abbia il permesso propone (anche un agente, dal tool MCP), ma il dataset cambia solo
/// con la decisione, che è un endpoint dell'API e non un tool.
/// </summary>
internal static class ProposalEndpoints
{
    public static RouteGroupBuilder MapProposalEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/shipments/{code}/proposals", ListAsync)
            .RequireOperation(Operation.FieldProposalRead, http => http.RouteValue("code"));

        api.MapPost("/shipments/{code}/proposals", ProposeAsync)
            .RequireOperation(Operation.FieldProposalCreate, http => http.RouteValue("code"));

        api.MapPost("/proposals/{id}/decision", DecideAsync)
            .RequireOperation(Operation.FieldProposalDecide, http => http.RouteValue("id"));

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<FieldProposalView>>, ProblemHttpResult>> ListAsync(
        string code, FieldProposalService service, AuditNote note, CancellationToken cancellationToken)
    {
        IReadOnlyList<FieldProposalView>? proposals = await service.ListAsync(code, cancellationToken);

        if (proposals is null)
        {
            return ApiProblems.NotFound($"Spedizione {code} non trovata.");
        }

        note.ShipmentCode = code;

        return TypedResults.Ok(proposals);
    }

    private static async Task<Results<Ok<ProposeFieldResponse>, ProblemHttpResult>> ProposeAsync(
        string code, ProposeFieldRequest request, FieldProposalService service, AuditNote note, HttpContext http, CancellationToken cancellationToken)
    {
        try
        {
            ProposeFieldResponse response = await service.ProposeAsync(code, request, AccessFilter.ActorOf(http), cancellationToken);

            note.ShipmentCode = code;
            note.Detail = Detail($"proposta {response.Proposal.Id} su {response.Proposal.FieldPath}: {(response.Created ? "creata" : "già in attesa")}");

            return TypedResults.Ok(response);
        }
        catch (ProposalException exception)
        {
            return ToProblem(exception);
        }
    }

    private static async Task<Results<Ok<ProposalDecisionResponse>, ProblemHttpResult>> DecideAsync(
        int id, ProposalDecisionRequest request, FieldProposalService service, AuditNote note, HttpContext http, CancellationToken cancellationToken)
    {
        try
        {
            ProposalDecisionResponse response = await service.DecideAsync(id, request, AccessFilter.ActorOf(http), cancellationToken);

            // La risorsa della richiesta è il numero della proposta: qui l'audit impara di quale spedizione si tratta.
            note.ShipmentCode = response.Proposal.ShipmentCode;
            note.Detail = Detail(string.Create(CultureInfo.InvariantCulture,
                $"{response.Proposal.FieldPath}: {response.Proposal.Status}, dataset v{response.DatasetVersion} {response.DatasetStatus}"));

            return TypedResults.Ok(response);
        }
        catch (ProposalException exception)
        {
            return ToProblem(exception);
        }
    }

    private static ProblemHttpResult ToProblem(ProposalException exception) => exception.Failure switch
    {
        ProposalFailure.NotFound => ApiProblems.NotFound(exception.Message),
        ProposalFailure.Conflict => ApiProblems.Conflict(exception.Message),
        _ => ApiProblems.Validation(exception.Message)
    };

    /// <summary>Lunghezza della colonna <c>AuditEvent.Detail</c>.</summary>
    private static string Detail(string text) => text.Length <= 200 ? text : text[..200];
}

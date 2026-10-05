using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Dusiburg.AI.Trasporto.Efti.Api.Gate;

/// <summary>
/// Le due sole porte dell'autorità (D7): cercare una targa nel registro degli identificatori e, con l'UIL ottenuto, leggere
/// un sottoinsieme del dataset. Nessun elenco di spedizioni, nessun accesso per codice.
/// </summary>
internal static class GateEndpoints
{
    public static RouteGroupBuilder MapGateEndpoints(this RouteGroupBuilder api)
    {
        var gate = api.MapGroup("/gate");

        gate.MapGet("/identifiers", FindIdentifiersAsync)
            .RequireOperation(Operation.IdentifierSearch, http => PlateNumbers.Normalize(http.QueryValue("plate")));

        gate.MapGet("/subset", GetSubsetAsync)
            .RequireOperation(Operation.SubsetRead, http => http.QueryValue("uil"));

        return api;
    }

    private static async Task<Results<Ok<IReadOnlyList<ConsignmentIdentifiersView>>, ProblemHttpResult>> FindIdentifiersAsync(
        string? plate, IdentifierRegistry registry, AuditNote note, CancellationToken cancellationToken)
    {
        if (PlateNumbers.Normalize(plate) is not { } normalizedPlate)
        {
            return ApiProblems.Validation("Indicare la targa da cercare (plate).");
        }

        IReadOnlyList<ConsignmentIdentifiersView> found = await registry.FindByPlateAsync(normalizedPlate, cancellationToken);

        note.Detail = found.Count == 1 ? "1 risultato" : $"{found.Count} risultati";

        return TypedResults.Ok(found);
    }

    private static async Task<Results<Ok<AuthoritySubsetView>, ProblemHttpResult>> GetSubsetAsync(
        string? uil, string? subsetId, IdentifierRegistry registry, EftiSubsetProjector projector, AuditNote note, CancellationToken cancellationToken)
    {
        if (!Uil.TryParse(uil, out Uil parsed))
        {
            return ApiProblems.Validation("UIL non valido: atteso gateId:platformId:datasetId.");
        }

        if (!EftiSubsets.IsAuthoritySubset(subsetId))
        {
            return ApiProblems.Validation($"Sottoinsieme non disponibile: ammessi {string.Join(", ", EftiSubsets.Authority)}.");
        }

        RegisteredDataset? dataset = await registry.FindDatasetAsync(parsed, cancellationToken);

        if (dataset is null)
        {
            return ApiProblems.NotFound("Nessuna spedizione raggiungibile con questo UIL: non è registrata oppure è scaduta dopo la consegna.");
        }

        // Da qui l'audit sa quale spedizione è stata toccata, anche se chi chiamava conosceva solo l'UIL.
        note.ShipmentCode = dataset.ShipmentCode;
        note.Detail = subsetId;

        if (dataset.Status == DatasetStatus.Incomplete)
        {
            return ApiProblems.Conflict("Il dataset di questa spedizione non è più valido: non può essere consultato.");
        }

        JsonElement subset = JsonSerializer.SerializeToElement(projector.ProjectSubset(dataset.Json, subsetId!), EftiDatasetJson.Options);

        return TypedResults.Ok(new AuthoritySubsetView(parsed.ToString(), subsetId!, dataset.SchemaVersion, subset));
    }
}

using System.Globalization;
using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Api.Security;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.ServiceDefaults.Problems;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

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

        gate.MapGet("/control-moments", SuggestControlMomentsAsync)
            .RequireOperation(Operation.ControlMomentSuggest, http => PlateNumbers.Normalize(http.QueryValue("plate")));

        return api;
    }

    /// <summary>
    /// Che cosa c'è a bordo di quel mezzo al momento del controllo. Il momento è adesso; <c>at</c> ne indica un altro solo se la
    /// piattaforma lo ammette (demo), e mai fuori dalla finestra in cui il gate conserva le spedizioni.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<ConsignmentIdentifiersView>>, ProblemHttpResult>> FindIdentifiersAsync(
        string? plate, string? at, IdentifierRegistry registry, IOptions<EftiPlatformOptions> platform, TimeProvider timeProvider, AuditNote note,
        CancellationToken cancellationToken)
    {
        if (PlateNumbers.Normalize(plate) is not { } normalizedPlate)
        {
            return ApiProblems.Validation("Indicare la targa da cercare (plate).");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset moment = now;

        if (!string.IsNullOrWhiteSpace(at))
        {
            if (!platform.Value.SimulatedControlTime)
            {
                return ApiProblems.Validation("Il momento del controllo non si può indicare: la ricerca vale per ciò che è a bordo adesso.");
            }

            if (!DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out moment))
            {
                return ApiProblems.Validation("Momento del controllo non valido (at): atteso data e ora ISO 8601, ad esempio 2026-10-05T09:30:00+02:00.");
            }

            if (moment > now || moment < registry.EarliestControlMoment)
            {
                return ApiProblems.Validation(
                    $"Il momento del controllo deve stare fra {registry.EarliestControlMoment:yyyy-MM-ddTHH:mm:ssK} e adesso: prima, le spedizioni di allora non sono più sul gate.");
            }
        }

        IReadOnlyList<ConsignmentIdentifiersView> found = await registry.FindOnBoardAsync(normalizedPlate, moment, cancellationToken);

        note.ControlMoment = moment == now ? null : moment;
        note.Detail = (found.Count == 1 ? "1 risultato" : $"{found.Count} risultati")
            + (moment == now ? string.Empty : $", controllo simulato al {moment.ToUniversalTime():yyyy-MM-dd HH:mm} UTC");

        return TypedResults.Ok(found);
    }

    private static async Task<Results<Ok<ControlMomentsView>, ProblemHttpResult>> SuggestControlMomentsAsync(
        string? plate, IdentifierRegistry registry, IOptions<EftiPlatformOptions> platform, AuditNote note, CancellationToken cancellationToken)
    {
        if (PlateNumbers.Normalize(plate) is not { } normalizedPlate)
        {
            return ApiProblems.Validation("Indicare la targa (plate).");
        }

        IReadOnlyList<ControlMomentView> moments = platform.Value.SimulatedControlTime
            ? await registry.SuggestControlMomentsAsync(normalizedPlate, cancellationToken)
            : [];

        note.Detail = platform.Value.SimulatedControlTime ? $"{moments.Count} momenti" : "momento simulato non ammesso";

        return TypedResults.Ok(new ControlMomentsView(platform.Value.SimulatedControlTime, registry.EarliestControlMoment, moments));
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

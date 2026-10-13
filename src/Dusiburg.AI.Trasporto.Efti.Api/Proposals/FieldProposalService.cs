using Dusiburg.AI.Trasporto.Efti.Api.Ecmr;
using Dusiburg.AI.Trasporto.Efti.Api.Gate;
using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.Efti.Core.Proposals;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Configurations;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Api.Proposals;

/// <summary>Perché una proposta o una decisione non si può eseguire: l'endpoint lo traduce nel ProblemDetails corrispondente.</summary>
public enum ProposalFailure
{
    NotFound,
    Invalid,
    Conflict
}

public sealed class ProposalException(ProposalFailure failure, string message) : Exception(message)
{
    public ProposalFailure Failure { get; } = failure;
}

/// <summary>
/// Proposte di completamento e loro decisione. Proporre non tocca il dataset; accettare scrive il valore sulla spedizione,
/// rimappa, rivalida e crea la versione successiva del dataset, pubblicandola sul gate se è valida. Che un campo esista e che
/// un valore sia accettabile lo decide <see cref="CompletableFields"/>: codice deterministico, non chi propone.
/// </summary>
public sealed class FieldProposalService(
    EftiDbContext db, EftiValidator validator, EcmrValidator ecmrValidator, EftiSchemaCatalog catalog, IdentifierRegistry registry, TimeProvider timeProvider)
{
    public const int MaxRows = 200;

    private const int MaxMessageLength = 500;

    /// <summary>Proposte di una spedizione dalla più recente (indice su <c>ShipmentId</c>); <c>null</c> se la spedizione non esiste.</summary>
    public async Task<IReadOnlyList<FieldProposalView>?> ListAsync(string shipmentCode, CancellationToken cancellationToken)
    {
        int? shipmentId = await db.Shipments
            .Where(s => s.Code == shipmentCode)
            .Select(s => (int?)s.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (shipmentId is null)
        {
            return null;
        }

        List<FieldProposal> proposals = await db.FieldProposals
            .AsNoTracking()
            .Where(p => p.ShipmentId == shipmentId)
            .OrderByDescending(p => p.Id)
            .Take(MaxRows)
            .ToListAsync(cancellationToken);

        return [.. proposals.Select(p => ToView(p, shipmentCode))];
    }

    /// <summary>
    /// Registra una proposta. La stessa proposta ancora in attesa (spedizione, campo, valore) non si duplica: si restituisce
    /// quella esistente.
    /// </summary>
    public async Task<ProposeFieldResponse> ProposeAsync(string shipmentCode, ProposeFieldRequest request, Actor actor, CancellationToken cancellationToken)
    {
        Shipment shipment = await LoadShipmentAsync(shipmentCode, cancellationToken)
            ?? throw new ProposalException(ProposalFailure.NotFound, $"Spedizione {shipmentCode} non trovata.");
        (int datasetVersion, DatasetStatus datasetStatus) = await LatestDatasetAsync(shipment.Id, cancellationToken);

        RefuseIfSigned(shipmentCode, datasetStatus);

        FieldTarget target = CompletableFields.Resolve(shipment, request.FieldPath, out string? fieldError)
            ?? throw new ProposalException(ProposalFailure.Invalid, fieldError!);
        string value = CompletableFields.Normalize(target.Field, request.Value, catalog, out string? valueError)
            ?? throw new ProposalException(ProposalFailure.Invalid, valueError!);

        if (!Enum.TryParse(request.Source, ignoreCase: true, out ProposalSource source) || !Enum.IsDefined(source) || int.TryParse(request.Source, out _))
        {
            throw new ProposalException(ProposalFailure.Invalid, $"Indicare la fonte del valore (source): {string.Join(", ", Enum.GetNames<ProposalSource>())}.");
        }

        string rationale = Required(request.Rationale, "rationale", ColumnSizes.Rationale);
        string? sourceReference = Optional(request.SourceReference, "sourceReference", ColumnSizes.SourceReference);
        string? currentValue = target.Read(shipment);

        if (currentValue == value)
        {
            throw new ProposalException(ProposalFailure.Conflict, $"Il campo {target.CanonicalPath} di {shipmentCode} ha già il valore '{value}'.");
        }

        FieldProposal? pending = await db.FieldProposals
            .AsNoTracking()
            .Where(p => p.ShipmentId == shipment.Id && p.Status == ProposalStatus.Pending && p.FieldPath == target.CanonicalPath)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(p => p.ProposedValue == value, cancellationToken);

        if (pending is not null)
        {
            return new ProposeFieldResponse(ToView(pending, shipmentCode), Created: false);
        }

        var proposal = new FieldProposal
        {
            ShipmentId = shipment.Id,
            FieldPath = target.CanonicalPath,
            ProposedValue = value,
            CurrentValue = currentValue,
            DatasetVersion = datasetVersion,
            Source = source,
            SourceReference = sourceReference,
            Rationale = rationale,
            Status = ProposalStatus.Pending,
            ProposedById = actor.Id,
            ProposedByName = actor.Name,
            ProposedAt = timeProvider.GetUtcNow()
        };

        db.FieldProposals.Add(proposal);
        await db.SaveChangesAsync(cancellationToken);

        return new ProposeFieldResponse(ToView(proposal, shipmentCode), Created: true);
    }

    /// <summary>
    /// Decide una proposta in attesa. Il rifiuto cambia solo la proposta; l'accettazione cambia la spedizione e produce la
    /// versione successiva del dataset. Tutto in un solo salvataggio.
    /// </summary>
    public async Task<ProposalDecisionResponse> DecideAsync(int proposalId, ProposalDecisionRequest request, Actor actor, CancellationToken cancellationToken)
    {
        if (request.Decision is not (ProposalStatus.Accepted or ProposalStatus.Rejected))
        {
            throw new ProposalException(ProposalFailure.Invalid, $"Indicare la decisione (decision): {ProposalStatus.Accepted} oppure {ProposalStatus.Rejected}.");
        }

        string? note = Optional(request.Note, "note", ColumnSizes.Rationale);

        FieldProposal proposal = await db.FieldProposals.SingleOrDefaultAsync(p => p.Id == proposalId, cancellationToken)
            ?? throw new ProposalException(ProposalFailure.NotFound, $"Proposta {proposalId} non trovata.");
        Shipment shipment = await LoadTrackedShipmentAsync(proposal.ShipmentId, cancellationToken);

        if (proposal.Status != ProposalStatus.Pending)
        {
            throw new ProposalException(ProposalFailure.Conflict, $"La proposta {proposalId} è già stata decisa: {proposal.Status}.");
        }

        (int datasetVersion, DatasetStatus datasetStatus) = await LatestDatasetAsync(shipment.Id, cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        FieldTarget? target = null;

        if (request.Decision == ProposalStatus.Accepted)
        {
            RefuseIfSigned(shipment.Code, datasetStatus);

            // Il campo si cerca di nuovo sulla spedizione di adesso: fra la proposta e la decisione può essere cambiata.
            target = CompletableFields.Resolve(shipment, proposal.FieldPath, out string? fieldError)
                ?? throw new ProposalException(ProposalFailure.Conflict, $"La proposta non è più applicabile: {fieldError}");
        }

        proposal.Status = request.Decision.Value;
        proposal.DecidedById = actor.Id;
        proposal.DecidedByName = actor.Name;
        proposal.DecidedAt = now;
        proposal.DecisionNote = note;

        if (request.Decision == ProposalStatus.Rejected)
        {
            await db.SaveChangesAsync(cancellationToken);

            (int errors, int warnings) = await CountIssuesAsync(shipment.Id, datasetVersion, cancellationToken);

            return new ProposalDecisionResponse(
                ToView(proposal, shipment.Code), datasetVersion, datasetStatus, errors, warnings,
                datasetStatus != DatasetStatus.Incomplete && await registry.IsRegisteredAsync(shipment.Id, cancellationToken));
        }

        target!.Write(shipment, proposal.ProposedValue);
        shipment.UpdatedAt = now;

        DatasetEvaluation evaluation = validator.Evaluate(shipment);

        db.Datasets.Add(new EftiDataset
        {
            ShipmentId = shipment.Id,
            Version = datasetVersion + 1,
            Status = evaluation.Status,
            SchemaVersion = validator.SchemaVersion,
            Json = evaluation.Json,
            CreatedAt = now,
            Issues = [.. evaluation.Findings.Select(f => new ValidationIssue
            {
                RuleCode = f.RuleCode,
                Severity = f.Severity,
                FieldPath = f.FieldPath,
                Message = f.Message.Length <= MaxMessageLength ? f.Message : f.Message[..MaxMessageLength]
            })]
        });

        // La stessa decisione rigenera anche il documento eCMR, con lo stesso numero di versione.
        EcmrDocument ecmr = EcmrVersions.Create(ecmrValidator.Evaluate(shipment), ecmrValidator.SchemaVersion, datasetVersion + 1, now);

        ecmr.ShipmentId = shipment.Id;
        db.EcmrDocuments.Add(ecmr);

        // Solo un dataset valido si pubblica sul gate. Se torna incompleto la voce resta: la lettura per UIL lo rifiuta da sé.
        if (evaluation.Status == DatasetStatus.Valid)
        {
            await registry.PublishAsync(shipment, evaluation.Json, now, cancellationToken);
        }

        proposal.ResultingDatasetVersion = datasetVersion + 1;

        // L'indice univoco (ShipmentId, Version) fa fallire una seconda decisione concorrente sulla stessa spedizione.
        await db.SaveChangesAsync(cancellationToken);

        return new ProposalDecisionResponse(
            ToView(proposal, shipment.Code), datasetVersion + 1, evaluation.Status, evaluation.Errors, evaluation.Warnings,
            evaluation.Status == DatasetStatus.Valid);
    }

    /// <summary>La spedizione con ciò che serve a mappare e validare: soggetti e righe (lettura per chiave univoca).</summary>
    private async Task<Shipment?> LoadShipmentAsync(string code, CancellationToken cancellationToken) =>
        await ShipmentGraph().AsNoTracking().SingleOrDefaultAsync(s => s.Code == code, cancellationToken);

    /// <summary>La stessa spedizione, seguita da EF: è quella che una decisione modifica (lettura per chiave primaria).</summary>
    private async Task<Shipment> LoadTrackedShipmentAsync(int id, CancellationToken cancellationToken) =>
        await ShipmentGraph().SingleAsync(s => s.Id == id, cancellationToken);

    private IQueryable<Shipment> ShipmentGraph() =>
        db.Shipments
            .Include(s => s.Parties).ThenInclude(p => p.Party)
            .Include(s => s.Items)
            .Include(s => s.AccompanyingDocuments)
            .AsSplitQuery();

    /// <summary>Versione e stato dell'ultimo dataset (indice univoco <c>ShipmentId</c>, <c>Version</c>).</summary>
    private async Task<(int Version, DatasetStatus Status)> LatestDatasetAsync(int shipmentId, CancellationToken cancellationToken)
    {
        var latest = await db.Datasets
            .AsNoTracking()
            .Where(d => d.ShipmentId == shipmentId)
            .OrderByDescending(d => d.Version)
            .Select(d => new { d.Version, d.Status })
            .FirstAsync(cancellationToken);

        return (latest.Version, latest.Status);
    }

    private async Task<(int Errors, int Warnings)> CountIssuesAsync(int shipmentId, int version, CancellationToken cancellationToken)
    {
        List<IssueSeverity> severities = await db.ValidationIssues
            .AsNoTracking()
            .Where(i => i.EftiDataset.ShipmentId == shipmentId && i.EftiDataset.Version == version)
            .Select(i => i.Severity)
            .ToListAsync(cancellationToken);

        return (severities.Count(s => s == IssueSeverity.Error), severities.Count(s => s == IssueSeverity.Warning));
    }

    private static void RefuseIfSigned(string shipmentCode, DatasetStatus status)
    {
        if (status == DatasetStatus.Signed)
        {
            throw new ProposalException(ProposalFailure.Conflict, $"Il dataset di {shipmentCode} è firmato: non si completa con una proposta.");
        }
    }

    private static string Required(string? value, string name, int maxLength) =>
        Optional(value, name, maxLength) ?? throw new ProposalException(ProposalFailure.Invalid, $"{name} è obbligatorio.");

    private static string? Optional(string? value, string name, int maxLength)
    {
        string? text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        return text is null || text.Length <= maxLength
            ? text
            : throw new ProposalException(ProposalFailure.Invalid, $"{name}: al massimo {maxLength} caratteri.");
    }

    private static FieldProposalView ToView(FieldProposal proposal, string shipmentCode) => new(
        proposal.Id, shipmentCode, proposal.FieldPath, proposal.ProposedValue, proposal.CurrentValue, proposal.DatasetVersion,
        proposal.Source, proposal.SourceReference, proposal.Rationale, proposal.Status,
        proposal.ProposedById, proposal.ProposedByName, proposal.ProposedAt,
        proposal.DecidedById, proposal.DecidedByName, proposal.DecidedAt, proposal.DecisionNote, proposal.ResultingDatasetVersion);
}

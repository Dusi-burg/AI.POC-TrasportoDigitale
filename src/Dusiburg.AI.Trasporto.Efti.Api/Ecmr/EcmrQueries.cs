using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.Efti.Api.Ecmr;

/// <summary>Il JSON salvato dell'ultima versione dell'eCMR, per l'esportazione XML.</summary>
public sealed record StoredEcmr(string Json, EcmrStatus Status, int Version);

/// <summary>Letture del documento eCMR di una spedizione: sempre l'ultima versione.</summary>
public sealed class EcmrQueries(EftiDbContext db)
{
    /// <summary>
    /// Ultima versione dell'eCMR con i suoi problemi (indice univoco <c>ShipmentId</c>, <c>Version</c>, dopo la lettura della
    /// spedizione per chiave univoca); <c>null</c> se la spedizione non esiste.
    /// </summary>
    public async Task<EcmrDocumentView?> FindAsync(string shipmentCode, CancellationToken cancellationToken)
    {
        EcmrDocument? document = await db.EcmrDocuments
            .AsNoTracking()
            .Include(d => d.Issues)
            .Where(d => d.Shipment.Code == shipmentCode)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null)
        {
            return null;
        }

        using JsonDocument json = JsonDocument.Parse(document.Json);

        return new EcmrDocumentView(
            shipmentCode,
            document.Version,
            document.Status,
            document.SchemaVersion,
            document.CreatedAt,
            document.Issues.Count(i => i.Severity == IssueSeverity.Error),
            document.Issues.Count(i => i.Severity == IssueSeverity.Warning),
            json.RootElement.Clone(),
            [.. document.Issues.OrderBy(i => i.Id).Select(i => new ValidationIssueView(i.RuleCode, i.Severity, i.FieldPath, i.Message))]);
    }

    /// <summary>Il solo JSON dell'ultima versione; <c>null</c> se la spedizione non esiste.</summary>
    public async Task<StoredEcmr?> FindStoredAsync(string shipmentCode, CancellationToken cancellationToken) =>
        await db.EcmrDocuments
            .AsNoTracking()
            .Where(d => d.Shipment.Code == shipmentCode)
            .OrderByDescending(d => d.Version)
            .Select(d => new StoredEcmr(d.Json, d.Status, d.Version))
            .FirstOrDefaultAsync(cancellationToken);
}

using Dusiburg.AI.Trasporto.Efti.Api.Gate;
using Dusiburg.AI.Trasporto.Efti.Api.Tms;
using Dusiburg.AI.Trasporto.Efti.Core.Import;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Tms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dusiburg.AI.Trasporto.Efti.Api.Shipments;

/// <summary>L'ordine richiesto non esiste nel TMS.</summary>
public sealed class TransportOrderNotFoundException(string orderCode) : Exception($"Ordine di trasporto {orderCode} non trovato nel TMS.");

/// <summary>L'ordine esiste ma non può diventare una spedizione.</summary>
public sealed class TransportOrderNotImportableException(string message) : Exception(message);

/// <summary>
/// Importazione dal TMS: legge l'ordine, lo copia in una spedizione, costruisce il dataset eFTI e lo valida. Mappatura e regole
/// sono in <c>Efti.Core</c>; qui si orchestra e si salva. Idempotente sul codice dell'ordine: una spedizione già importata non
/// si tocca, perché da quel momento il dataset cambia solo con una decisione umana.
/// </summary>
public sealed class ShipmentImportService(
    EftiDbContext db,
    TmsApiClient tms,
    EftiValidator validator,
    IdentifierRegistry registry,
    IOptions<CarrierOptions> carrier,
    TimeProvider timeProvider,
    ILogger<ShipmentImportService> logger)
{
    private Party? _carrierParty;

    public async Task<ImportShipmentsResponse> ImportOneAsync(string orderCode, CancellationToken cancellationToken)
    {
        if (await db.Shipments.AnyAsync(s => s.TmsOrderCode == orderCode, cancellationToken))
        {
            return new ImportShipmentsResponse([], [orderCode], []);
        }

        TransportOrderDetailView order = await tms.FindOrderAsync(orderCode, cancellationToken)
            ?? throw new TransportOrderNotFoundException(orderCode);

        if (order.Order.Status == TransportOrderStatus.Cancelled)
        {
            throw new TransportOrderNotImportableException($"L'ordine {orderCode} è annullato: non diventa una spedizione.");
        }

        return new ImportShipmentsResponse([await ImportAsync(order, cancellationToken)], [], []);
    }

    public async Task<ImportShipmentsResponse> ImportAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TransportOrderSummaryView> orders = await tms.ListOrdersAsync(cancellationToken);
        HashSet<string> existing = [.. await db.Shipments.Select(s => s.TmsOrderCode).ToListAsync(cancellationToken)];

        List<ImportedShipment> imported = [];
        List<string> alreadyImported = [];
        List<string> skipped = [];

        // Ordine stabile per codice: due importazioni dello stesso TMS creano le spedizioni nella stessa sequenza.
        foreach (TransportOrderSummaryView summary in orders.OrderBy(o => o.Code, StringComparer.Ordinal))
        {
            if (existing.Contains(summary.Code))
            {
                alreadyImported.Add(summary.Code);
            }
            else if (summary.Status == TransportOrderStatus.Cancelled)
            {
                skipped.Add(summary.Code);
            }
            else if (await tms.FindOrderAsync(summary.Code, cancellationToken) is { } order)
            {
                imported.Add(await ImportAsync(order, cancellationToken));
            }
        }

        logger.LogInformation(
            "Importazione dal TMS: {Imported} spedizioni create, {AlreadyImported} già presenti, {Skipped} ordini annullati",
            imported.Count, alreadyImported.Count, skipped.Count);

        return new ImportShipmentsResponse(imported, alreadyImported, skipped);
    }

    private async Task<ImportedShipment> ImportAsync(TransportOrderDetailView order, CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Shipment shipment = ShipmentFactory.Create(order, carrier.Value, now);

        // L'identificativo pubblico si genera qui e non nel database: serve all'UIL prima del salvataggio.
        shipment.PublicId = Guid.CreateVersion7(now);

        await ReuseCarrierPartyAsync(shipment, cancellationToken);

        DatasetEvaluation evaluation = validator.Evaluate(shipment);

        // Solo un dataset valido si pubblica sul gate: è ciò che rende la spedizione raggiungibile da un'autorità (D7).
        if (evaluation.Status == DatasetStatus.Valid)
        {
            db.IdentifierRegistry.Add(registry.CreateEntry(shipment, evaluation.Json, now));
        }

        shipment.Datasets.Add(new EftiDataset
        {
            Version = 1,
            Status = evaluation.Status,
            SchemaVersion = validator.SchemaVersion,
            Json = evaluation.Json,
            CreatedAt = now,
            Issues = [.. evaluation.Findings.Select(f => new ValidationIssue
            {
                RuleCode = f.RuleCode,
                Severity = f.Severity,
                FieldPath = f.FieldPath,
                Message = Truncate(f.Message, MaxMessageLength)
            })]
        });

        db.Shipments.Add(shipment);
        await db.SaveChangesAsync(cancellationToken);

        return new ImportedShipment(order.Order.Code, shipment.Code, evaluation.Status, evaluation.Errors, evaluation.Warnings);
    }

    /// <summary>
    /// Il vettore è lo stesso per tutte le spedizioni: una sola riga in <c>efti.Party</c>, cercata per partita IVA e nome.
    /// Gli altri soggetti restano righe distinte per spedizione, perché nel TMS sono testo dell'ordine e non anagrafiche.
    /// </summary>
    private async Task ReuseCarrierPartyAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        ShipmentParty carrierRole = shipment.Parties.Single(p => p.Role == PartyRole.Carrier);
        Party candidate = carrierRole.Party;

        _carrierParty ??= await db.Parties
            .Where(p => p.VatNumber == candidate.VatNumber && p.Name == candidate.Name
                && db.ShipmentParties.Any(sp => sp.PartyId == p.Id && sp.Role == PartyRole.Carrier))
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (_carrierParty is not null)
        {
            carrierRole.Party = _carrierParty;
        }
        else
        {
            _carrierParty = candidate;
        }
    }

    /// <summary>Lunghezza della colonna <c>ValidationIssue.Message</c>: i messaggi dell'XSD possono superarla.</summary>
    private const int MaxMessageLength = 500;

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}

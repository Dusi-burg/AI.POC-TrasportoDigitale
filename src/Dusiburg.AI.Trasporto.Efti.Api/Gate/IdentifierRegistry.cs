using System.Text.Json;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dusiburg.AI.Trasporto.Efti.Api.Gate;

/// <summary>Il dataset a cui porta un UIL, con la spedizione a cui appartiene.</summary>
public sealed record RegisteredDataset(string ShipmentCode, string Json, DatasetStatus Status, string SchemaVersion);

/// <summary>
/// Registro degli identificatori del gate simulato (D7). La piattaforma vi pubblica le spedizioni con dataset valido;
/// l'autorità lo interroga per targa e ne ricava l'UIL. È l'unica strada da una targa a una spedizione.
/// </summary>
public sealed class IdentifierRegistry(
    EftiDbContext db, EftiSubsetProjector projector, IOptions<EftiPlatformOptions> platform, TimeProvider timeProvider)
{
    public const int MaxRows = 50;

    /// <summary>
    /// Le spedizioni consegnate prima di questo istante sono scadute: non sono più raggiungibili dal gate. La voce resta nel
    /// registro (la scadenza è un'impostazione e può cambiare), ma ricerca e lettura non la restituiscono.
    /// </summary>
    private DateTimeOffset DeliveredNoEarlierThan => timeProvider.GetUtcNow().AddMinutes(-platform.Value.GateRetentionMinutes);

    /// <summary>
    /// La voce da pubblicare per una spedizione: UIL, targhe normalizzate e sottoinsieme degli identificatori ricavato dal
    /// dataset. Non la salva: la aggiunge chi sta salvando la spedizione, nella stessa transazione.
    /// </summary>
    public IdentifierRegistryEntry CreateEntry(Shipment shipment, string datasetJson, DateTimeOffset registeredAt) => new()
    {
        Shipment = shipment,
        Uil = Uil.For(platform.Value, shipment.PublicId).ToString(),
        VehiclePlateNumber = PlateNumbers.Normalize(shipment.VehiclePlateNumber),
        TrailerPlateNumber = PlateNumbers.Normalize(shipment.TrailerPlateNumber),
        IdentifiersJson = projector.ProjectIdentifiers(datasetJson).ToJsonString(EftiDatasetJson.Options),
        RegisteredAt = registeredAt
    };

    /// <summary>
    /// Pubblica o aggiorna la voce di una spedizione il cui dataset è appena diventato (o rimasto) valido dopo una decisione
    /// umana: targhe e identificatori seguono la nuova versione, UIL e data di pubblicazione restano quelli della prima volta.
    /// Non salva: lo fa chi sta salvando la nuova versione del dataset, nella stessa transazione.
    /// </summary>
    public async Task PublishAsync(Shipment shipment, string datasetJson, DateTimeOffset now, CancellationToken cancellationToken)
    {
        IdentifierRegistryEntry fresh = CreateEntry(shipment, datasetJson, now);
        IdentifierRegistryEntry? existing = await db.IdentifierRegistry.SingleOrDefaultAsync(e => e.ShipmentId == shipment.Id, cancellationToken);

        if (existing is null)
        {
            db.IdentifierRegistry.Add(fresh);

            return;
        }

        existing.VehiclePlateNumber = fresh.VehiclePlateNumber;
        existing.TrailerPlateNumber = fresh.TrailerPlateNumber;
        existing.IdentifiersJson = fresh.IdentifiersJson;
    }

    /// <summary>La spedizione ha una voce nel registro (lettura per chiave univoca).</summary>
    public Task<bool> IsRegisteredAsync(int shipmentId, CancellationToken cancellationToken) =>
        db.IdentifierRegistry.AnyAsync(e => e.ShipmentId == shipmentId, cancellationToken);

    /// <summary>Voci non scadute con quella targa, della motrice o del rimorchio (colonne indicizzate), dalla più recente.</summary>
    public async Task<IReadOnlyList<ConsignmentIdentifiersView>> FindByPlateAsync(string normalizedPlate, CancellationToken cancellationToken)
    {
        DateTimeOffset deliveredNoEarlierThan = DeliveredNoEarlierThan;

        var entries = await db.IdentifierRegistry
            .AsNoTracking()
            .Where(e => e.VehiclePlateNumber == normalizedPlate || e.TrailerPlateNumber == normalizedPlate)
            .Where(e => e.Shipment.DeliveredAt == null || e.Shipment.DeliveredAt > deliveredNoEarlierThan)
            .OrderByDescending(e => e.Id)
            .Take(MaxRows)
            .Select(e => new { e.Uil, e.IdentifiersJson })
            .ToListAsync(cancellationToken);

        return [.. entries.Select(e => new ConsignmentIdentifiersView(e.Uil, ParseJson(e.IdentifiersJson)))];
    }

    /// <summary>
    /// Ultima versione del dataset della spedizione registrata con quell'UIL (lettura per chiave univoca); <c>null</c> se l'UIL
    /// non è nel registro o se la spedizione è scaduta. Per chi chiama i due casi sono indistinguibili.
    /// </summary>
    public async Task<RegisteredDataset?> FindDatasetAsync(Uil uil, CancellationToken cancellationToken)
    {
        string key = uil.ToString();
        DateTimeOffset deliveredNoEarlierThan = DeliveredNoEarlierThan;

        return await db.IdentifierRegistry
            .AsNoTracking()
            .Where(e => e.Uil == key)
            .Where(e => e.Shipment.DeliveredAt == null || e.Shipment.DeliveredAt > deliveredNoEarlierThan)
            .Select(e => e.Shipment.Datasets
                .OrderByDescending(d => d.Version)
                .Select(d => new RegisteredDataset(e.Shipment.Code, d.Json, d.Status, d.SchemaVersion))
                .First())
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}

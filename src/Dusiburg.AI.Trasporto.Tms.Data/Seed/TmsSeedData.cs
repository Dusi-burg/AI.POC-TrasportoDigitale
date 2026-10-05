using Dusiburg.AI.Trasporto.Tms.Data.Entities;

namespace Dusiburg.AI.Trasporto.Tms.Data.Seed;

/// <summary>
/// Difetti voluti del dato sintetico: ciascuno è un caso che la validazione eFTI (Fase 3) deve riconoscere.
/// Non è una tabella: resta nel risultato del generatore, per il riepilogo di <c>DbInit</c> e per i test.
/// </summary>
public enum SeedDefect
{
    /// <summary>CAP e città del destinatario e del luogo di consegna vuoti.</summary>
    MissingConsigneeAddress = 1,

    /// <summary>Descrizione della prima riga vuota.</summary>
    MissingGoodsDescription = 2,

    /// <summary>Colli della prima riga a zero.</summary>
    MissingPackageCount = 3,

    /// <summary>Peso di testata diverso dalla somma delle righe di oltre il 5%.</summary>
    WeightMismatch = 4,

    /// <summary>Riga marcata ADR senza numero ONU.</summary>
    AdrWithoutUnNumber = 5,

    /// <summary>Paese del destinatario non ISO 3166-1 a due lettere.</summary>
    InvalidCountryCode = 6,

    /// <summary>Data di consegna pattuita precedente alla presa in carico.</summary>
    DeliveryBeforePickup = 7
}

/// <summary>
/// Dati del TMS pronti da inserire: grafo di entità collegate dalle navigazioni, ordini demo prima di quelli generati.
/// </summary>
/// <param name="DefectsByOrderCode">Solo gli ordini generati con almeno un difetto.</param>
public sealed record TmsSeedData(
    int RandomSeed,
    DateOnly Today,
    IReadOnlyList<Customer> Customers,
    IReadOnlyList<Vehicle> Vehicles,
    IReadOnlyList<Driver> Drivers,
    IReadOnlyList<Trip> Trips,
    IReadOnlyList<TransportOrder> Orders,
    IReadOnlyDictionary<string, IReadOnlyList<SeedDefect>> DefectsByOrderCode);

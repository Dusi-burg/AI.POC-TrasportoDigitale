namespace Dusiburg.AI.Trasporto.Shared.Contracts.Tms;

// Valori espliciti: sono le PK delle tabelle di lookup dello schema tms (tinyint).

public enum TransportOrderStatus : byte
{
    /// <summary>Preso in carico dall'ufficio traffico, merce non ancora ritirata.</summary>
    Planned = 1,

    InTransit = 2,

    Delivered = 3,

    Cancelled = 4
}

public enum VehicleType : byte
{
    /// <summary>Trattore stradale.</summary>
    Tractor = 1,

    /// <summary>Motrice con cassone proprio.</summary>
    Truck = 2,

    SemiTrailer = 3
}

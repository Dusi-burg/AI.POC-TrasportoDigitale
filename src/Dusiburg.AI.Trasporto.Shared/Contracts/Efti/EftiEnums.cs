namespace Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

// Valori espliciti: sono le PK delle tabelle di lookup dello schema efti (tinyint).

/// <summary>Ruolo di un soggetto nella spedizione.</summary>
public enum PartyRole : byte
{
    Consignor = 1,

    Consignee = 2,

    Carrier = 3,

    /// <summary>Vettore successivo.</summary>
    ConnectingCarrier = 4
}

/// <summary>Stato di una versione del dataset eFTI.</summary>
public enum DatasetStatus : byte
{
    /// <summary>Ha almeno un problema di validazione bloccante.</summary>
    Incomplete = 1,

    Valid = 2,

    /// <summary>Firmato: nel POC la firma è simulata.</summary>
    Signed = 3
}

public enum IssueSeverity : byte
{
    Error = 1,

    Warning = 2
}

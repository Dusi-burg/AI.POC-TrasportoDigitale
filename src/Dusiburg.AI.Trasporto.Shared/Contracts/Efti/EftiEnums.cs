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

/// <summary>Stato di una proposta di completamento: la decide una persona, mai un tool.</summary>
public enum ProposalStatus : byte
{
    Pending = 1,

    Accepted = 2,

    Rejected = 3
}

/// <summary>Che cosa è successo a un campo estratto da un documento: lo decide il codice, dalla confidenza.</summary>
public enum ExtractionFieldOutcome : byte
{
    /// <summary>Coincide con il valore che la spedizione ha già: nulla da fare.</summary>
    Matches = 1,

    /// <summary>Sopra soglia: è diventato una proposta di completamento.</summary>
    Proposed = 2,

    /// <summary>Sotto soglia o con un formato non valido: è nella coda di revisione.</summary>
    InReview = 3,

    /// <summary>Il campo non esiste sulla spedizione (riga in più, riga non ADR).</summary>
    NotApplicable = 4
}

/// <summary>Stato di una voce della coda di revisione.</summary>
public enum ReviewStatus : byte
{
    Pending = 1,

    /// <summary>Confermata da una persona, eventualmente con un valore corretto: il dataset ha una nuova versione.</summary>
    Confirmed = 2,

    Rejected = 3
}

/// <summary>Da dove viene il valore proposto.</summary>
public enum ProposalSource : byte
{
    /// <summary>Un dato dell'ordine nel TMS.</summary>
    TmsOrder = 1,

    /// <summary>Un documento caricato (Fase 7).</summary>
    Document = 2,

    /// <summary>Dedotto da altri dati della spedizione, senza una fonte che lo riporti alla lettera.</summary>
    Inference = 3
}

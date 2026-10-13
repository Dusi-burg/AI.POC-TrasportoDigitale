namespace Dusiburg.AI.Trasporto.Shared.Contracts;

// Enum usati sia dal TMS sia dal nucleo eFTI. Valori espliciti: sono le PK delle tabelle di lookup (tinyint), una per schema.

/// <summary>Tipo di imballo di una riga di merce.</summary>
public enum PackageType : byte
{
    Pallet = 1,
    Carton = 2,
    Drum = 3,
    Crate = 4,
    Bag = 5,
    Bundle = 6,
    Roll = 7
}

/// <summary>Chi paga il nolo.</summary>
public enum FreightPayment : byte
{
    /// <summary>Porto franco: paga il mittente.</summary>
    Prepaid = 1,

    /// <summary>Porto assegnato: paga il destinatario.</summary>
    Collect = 2
}

namespace Dusiburg.AI.Trasporto.Eval.Cmr;

/// <summary>Impaginazione di un CMR sintetico.</summary>
public enum CmrLayout
{
    /// <summary>Griglia di caselle numerate, etichette in italiano e inglese.</summary>
    BoxesItalian,

    /// <summary>La stessa griglia con etichette in francese e inglese.</summary>
    BoxesFrench,

    /// <summary>Elenco etichetta–valore senza numeri di casella, in un altro ordine.</summary>
    LabelList
}

/// <summary>Che cosa un CMR sintetico mette alla prova: è la chiave con cui la valutazione raggruppa i risultati.</summary>
public static class CmrCategories
{
    /// <summary>Tutto coincide con il TMS.</summary>
    public const string Clean = "clean";

    /// <summary>Il documento porta dati che al TMS mancano.</summary>
    public const string TmsGap = "tms-gap";

    /// <summary>Un valore del documento è diverso da quello del TMS.</summary>
    public const string Conflict = "conflict";

    /// <summary>Caselle con più numeri candidati, o una nota a mano che contraddice lo stampato.</summary>
    public const string Ambiguous = "ambiguous";

    /// <summary>Note a mano che non cambiano i valori: non devono disturbare.</summary>
    public const string Annotation = "annotation";

    /// <summary>Caselle vuote: non c'è nulla da estrarre.</summary>
    public const string Empty = "empty";

    /// <summary>Istruzioni nel testo del documento, rivolte a chi lo legge in automatico.</summary>
    public const string Injection = "injection";

    public static readonly IReadOnlyList<string> All = [Clean, TmsGap, Conflict, Ambiguous, Annotation, Empty, Injection];
}

/// <summary>Un soggetto come è stampato in una casella.</summary>
public sealed record CmrPartyBlock(string? Name, string? Street, string? PostalCode, string? City, string? CountryCode, string? VatNumber);

/// <summary>Una riga di merce come è stampata nelle caselle 6–12.</summary>
/// <param name="Packages">Testo della casella 7: può essere ambiguo, per esempio "14 (12 pallet + 2 casse)".</param>
/// <param name="GrossWeight">Testo della casella 11.</param>
public sealed record CmrGoodsRow(string Marks, string Packages, string Packing, string Description, string GrossWeight, string Volume);

/// <summary>Un'annotazione "a mano" (resa in corsivo blu): contraddice o completa lo stampato.</summary>
public sealed record CmrHandwrittenNote(int Box, string Text);

/// <summary>
/// Ciò che un CMR sintetico stampa, casella per casella, e la sua verità di riferimento: i valori che un'estrazione corretta
/// deve restituire, per percorso del campo nel dataset, e i campi che il documento rende ambigui.
/// </summary>
public sealed class CmrContent
{
    public required string Code { get; init; }

    public required string OrderCode { get; init; }

    public required string Title { get; init; }

    /// <summary>Una delle <see cref="CmrCategories"/>.</summary>
    public required string Category { get; init; }

    public required CmrLayout Layout { get; init; }

    public required string CmrNumber { get; init; }

    public required CmrPartyBlock Sender { get; init; }

    public required CmrPartyBlock Consignee { get; init; }

    public required CmrPartyBlock Carrier { get; init; }

    public required CmrPartyBlock Delivery { get; init; }

    public required CmrPartyBlock TakingOver { get; init; }

    public required string TakingOverDate { get; init; }

    public required IReadOnlyList<CmrGoodsRow> Goods { get; init; }

    public required string TotalPackages { get; init; }

    public required string TotalGrossWeight { get; init; }

    public required string SenderInstructions { get; init; }

    public required string Payment { get; init; }

    public required string CashOnDelivery { get; init; }

    public required string VehiclePlate { get; init; }

    public required string TrailerPlate { get; init; }

    public required string EstablishedAt { get; init; }

    public IReadOnlyList<CmrHandwrittenNote> Handwritten { get; init; } = [];

    /// <summary>Valore atteso per percorso del campo nel dataset eFTI (gli stessi percorsi di <c>CompletableFields</c>).</summary>
    public required IReadOnlyDictionary<string, string> Expected { get; init; }

    /// <summary>Campi che il documento rende ambigui: ci si aspetta che finiscano in revisione.</summary>
    public IReadOnlyList<string> Ambiguous { get; init; } = [];

    /// <summary>Valori che un'iniezione nel testo cerca di far uscire, per campo: un'estrazione corretta non li restituisce mai.</summary>
    public IReadOnlyDictionary<string, string> Injected { get; init; } = new Dictionary<string, string>();
}

/// <summary>La verità di riferimento salvata accanto al PDF (<c>&lt;codice&gt;.expected.json</c>).</summary>
public sealed record CmrExpected(
    string Code, string OrderCode, string ShipmentCode, string Title, string Layout, IReadOnlyDictionary<string, string> Fields, IReadOnlyList<string> Ambiguous);

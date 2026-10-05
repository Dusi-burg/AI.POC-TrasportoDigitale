using Dusiburg.AI.Trasporto.Shared.Contracts;

namespace Dusiburg.AI.Trasporto.Efti.Core.Dataset;

/// <summary>
/// Codici scritti nel dataset. Quelli segnati "da riconfermare" sono campi <c>token</c> nello schema: lo schema non ne elenca
/// i valori, quindi la conformità all'XSD non li verifica (fase-3.md §4).
/// </summary>
public static class EftiCodes
{
    /// <summary>UN/ECE Rec. 19, verificato sulla lista dello schema: <c>3</c> = trasporto su strada.</summary>
    public const string RoadTransportMode = "3";

    /// <summary>UN/EDIFACT 8053, verificato sulla lista dello schema: <c>SM</c> = semirimorchio.</summary>
    public const string SemiTrailerCategory = "SM";

    /// <summary>UN/EDIFACT 1001, da riconfermare: <c>730</c> = lettera di vettura stradale.</summary>
    public const string RoadConsignmentNote = "730";

    public const string Kilogram = "KGM";

    public const string CubicMetre = "MTQ";

    /// <summary>UN/EDIFACT 2379: data senza ora.</summary>
    public const string DateFormat = "102";

    /// <summary>UN/EDIFACT 2379: data, ora e scostamento da UTC.</summary>
    public const string DateTimeWithOffsetFormat = "205";

    /// <summary>UN/ECE Rec. 21, da riconfermare.</summary>
    public static string PackageTypeCode(PackageType type) => type switch
    {
        PackageType.Pallet => "PX",
        PackageType.Carton => "CT",
        PackageType.Drum => "DR",
        PackageType.Crate => "CR",
        PackageType.Bag => "BG",
        PackageType.Bundle => "BE",
        PackageType.Roll => "RO",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo di imballo senza codice eFTI.")
    };

    /// <summary>UN/EDIFACT 4237, da riconfermare: <c>PP</c> = porto franco, <c>CC</c> = porto assegnato.</summary>
    public static string PaymentArrangementCode(FreightPayment payment) => payment switch
    {
        FreightPayment.Prepaid => "PP",
        FreightPayment.Collect => "CC",
        _ => throw new ArgumentOutOfRangeException(nameof(payment), payment, "Pagamento del nolo senza codice eFTI.")
    };

    /// <summary>UN/EDIFACT 8339, da riconfermare: gruppo di imballaggio I, II, III → 1, 2, 3. Un valore non riconosciuto resta fuori dal dataset.</summary>
    public static string? PackagingDangerLevelCode(string? packingGroup) => packingGroup?.Trim().ToUpperInvariant() switch
    {
        "I" => "1",
        "II" => "2",
        "III" => "3",
        _ => null
    };
}

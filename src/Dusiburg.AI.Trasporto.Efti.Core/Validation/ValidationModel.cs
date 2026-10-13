using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Validation;

public enum RuleCategory
{
    /// <summary>Un dato che deve esserci.</summary>
    Mandatory,

    /// <summary>Un dato presente ma scritto male.</summary>
    Format,

    /// <summary>Due dati che non vanno d'accordo fra loro.</summary>
    Coherence,

    /// <summary>Segnalazione che non blocca il dataset.</summary>
    Advisory,

    /// <summary>Conformità dell'XML allo schema XSD.</summary>
    Schema
}

/// <summary>Un caso in cui una regola non è rispettata: quale campo e perché.</summary>
/// <param name="FieldPath">Percorso dell'elemento nel dataset eFTI, con l'indice per gli elementi ripetuti.</param>
public sealed record RuleViolation(string FieldPath, string Message);

/// <summary>
/// Una regola di validazione, dichiarata con ciò che serve a spiegarla: da questo elenco si genera
/// <c>docs/regole-di-validazione.md</c>.
/// </summary>
/// <param name="Field">Elemento eFTI a cui la regola si riferisce, come compare nel documento.</param>
public sealed record ValidationRule(
    string Code,
    RuleCategory Category,
    IssueSeverity Severity,
    string Field,
    string Explanation,
    Func<ValidationContext, IEnumerable<RuleViolation>> Check);

/// <summary>Esito dell'applicazione di una regola a una spedizione.</summary>
public sealed record ValidationFinding(string RuleCode, IssueSeverity Severity, string FieldPath, string Message);

/// <summary>La spedizione vista dalle regole: soggetti per ruolo, righe in ordine, liste di codici dello schema.</summary>
public sealed class ValidationContext(Shipment shipment, EftiSchemaCatalog catalog)
{
    public Shipment Shipment { get; } = shipment;

    public Party? Consignor { get; } = PartyOf(shipment, PartyRole.Consignor);

    public Party? Consignee { get; } = PartyOf(shipment, PartyRole.Consignee);

    public Party? Carrier { get; } = PartyOf(shipment, PartyRole.Carrier);

    public IReadOnlyList<ConsignmentItem> Items { get; } = [.. shipment.Items.OrderBy(i => i.SequenceNumber)];

    /// <summary>Giorno di presa in carico: quello effettivo se la merce è stata ritirata, altrimenti quello previsto.</summary>
    public DateOnly? PickupDay =>
        Shipment.PickedUpAt is { } pickedUpAt ? DateOnly.FromDateTime(pickedUpAt.DateTime) : Shipment.PickupDate;

    public bool IsKnownCountry(string? code) => code is not null && catalog.CountryCodes.Contains(code);

    /// <summary>Soggetti con il prefisso del loro elemento eFTI.</summary>
    public IEnumerable<(string Path, Party Party)> Parties()
    {
        foreach (IGrouping<PartyRole, ShipmentParty> role in Shipment.Parties.GroupBy(p => p.Role))
        {
            ShipmentParty[] parties = [.. role.OrderBy(p => p.Sequence)];

            for (int index = 0; index < parties.Length; index++)
            {
                yield return (PartyPath(role.Key, index), parties[index].Party);
            }
        }
    }

    private static string PartyPath(PartyRole role, int index) => role switch
    {
        PartyRole.Consignor => "consignor",
        PartyRole.Consignee => "consignee",
        PartyRole.Carrier => "carrier",
        _ => $"connectingCarrier[{index}]"
    };

    private static Party? PartyOf(Shipment shipment, PartyRole role) =>
        shipment.Parties.Where(p => p.Role == role).OrderBy(p => p.Sequence).Select(p => p.Party).FirstOrDefault();
}

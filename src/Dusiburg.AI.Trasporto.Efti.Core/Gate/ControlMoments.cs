using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Gate;

/// <summary>Una spedizione che un mezzo ha avuto a bordo: da quando a quando.</summary>
/// <param name="DeliveredAt"><c>null</c> se è ancora a bordo.</param>
public sealed record CarriedConsignment(string Uil, DateTimeOffset PickedUpAt, DateTimeOffset? DeliveredAt)
{
    public bool IsOnBoardAt(DateTimeOffset moment) => PickedUpAt <= moment && (DeliveredAt is null || DeliveredAt > moment);
}

/// <summary>
/// I momenti da proporre a chi simula un controllo su strada nel passato recente (aiuto per la demo): uno per ogni carico
/// diverso che il mezzo ha avuto a bordo, escluso quello di adesso. Nella realtà un controllo avviene sempre "adesso".
/// </summary>
public static class ControlMoments
{
    /// <summary>Quanto prima della consegna si colloca il controllo simulato: il mezzo è ancora carico, sta arrivando.</summary>
    public static readonly TimeSpan BeforeDelivery = TimeSpan.FromHours(1);

    public const int MaxSuggestions = 5;

    /// <param name="earliest">Prima di questo istante un controllo simulato non è ammesso.</param>
    public static IReadOnlyList<ControlMomentView> Suggest(IReadOnlyCollection<CarriedConsignment> carried, DateTimeOffset earliest, DateTimeOffset now)
    {
        string onBoardNow = Load(carried, now);
        var seen = new HashSet<string>(StringComparer.Ordinal) { onBoardNow, string.Empty };
        List<ControlMomentView> moments = [];

        // Poco prima di ogni consegna il carico è al completo: da lì, andando indietro, si incontrano i carichi precedenti.
        foreach (DateTimeOffset delivered in carried.Select(c => c.DeliveredAt).OfType<DateTimeOffset>().Where(d => d <= now).Distinct().OrderDescending())
        {
            DateTimeOffset moment = delivered - BeforeDelivery;

            if (moment < earliest || !seen.Add(Load(carried, moment)))
            {
                continue;
            }

            moments.Add(new ControlMomentView(moment, carried.Count(c => c.IsOnBoardAt(moment))));

            if (moments.Count == MaxSuggestions)
            {
                break;
            }
        }

        return moments;
    }

    /// <summary>Il carico a un certo momento, come chiave: gli UIL a bordo.</summary>
    private static string Load(IEnumerable<CarriedConsignment> carried, DateTimeOffset moment) =>
        string.Join('|', carried.Where(c => c.IsOnBoardAt(moment)).Select(c => c.Uil).Order(StringComparer.Ordinal));
}

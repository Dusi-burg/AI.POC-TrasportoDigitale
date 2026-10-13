using Dusiburg.AI.Trasporto.Efti.Core.Gate;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>I momenti proposti per simulare un controllo su strada (aiuto per la demo): uno per ogni carico diverso da quello di adesso.</summary>
public class ControlMomentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Earliest = Now.AddDays(-5);

    [Test]
    public void Suggest_OnePerPastLoad_NewestFirst_WithoutTheLoadOfNow()
    {
        //SETUP
        CarriedConsignment[] carried =
        [
            // Viaggio di oggi: due spedizioni ancora a bordo.
            new("uil-today-1", At(0, 7), null),
            new("uil-today-2", At(0, 8), null),
            // Viaggio di tre giorni fa: due spedizioni consegnate a un giorno l'una dall'altra.
            new("uil-past-1", At(-4, 8), At(-3, 10)),
            new("uil-past-2", At(-4, 9), At(-2, 16)),
            // Viaggio di sette giorni fa: fuori dalla finestra del gate.
            new("uil-old", At(-8, 8), At(-7, 10))
        ];

        //SUT
        IReadOnlyList<ControlMomentView> moments = ControlMoments.Suggest(carried, Earliest, Now);

        Assert.That(moments, Is.EqualTo(new[]
        {
            new ControlMomentView(At(-2, 15), 1),
            new ControlMomentView(At(-3, 9), 2)
        }), "un'ora prima di ogni consegna, finché il carico cambia; mai prima della finestra del gate");
    }

    [Test]
    public void Suggest_OnlyTheLoadOfNow_HasNothingToSuggest()
    {
        //SETUP
        CarriedConsignment[] carried = [new("uil-1", At(-1, 8), null)];

        //SUT
        IReadOnlyList<ControlMomentView> moments = ControlMoments.Suggest(carried, Earliest, Now);

        Assert.That(moments, Is.Empty);
        Assert.That(ControlMoments.Suggest([], Earliest, Now), Is.Empty);
    }

    [Test]
    public void Suggest_SameLoadDeliveredTogether_IsSuggestedOnce_AndNeverMoreThanTheMaximum()
    {
        //SETUP
        CarriedConsignment[] together = [new("a", At(-3, 8), At(-2, 10)), new("b", At(-3, 8), At(-2, 10))];
        CarriedConsignment[] many = [.. Enumerable.Range(1, 12).Select(index => new CarriedConsignment($"uil-{index}", Now.AddHours(-8 * index), Now.AddHours(-8 * index + 6)))];

        //SUT
        IReadOnlyList<ControlMomentView> once = ControlMoments.Suggest(together, Earliest, Now);
        IReadOnlyList<ControlMomentView> capped = ControlMoments.Suggest(many, Earliest, Now);

        Assert.That(once, Is.EqualTo(new[] { new ControlMomentView(At(-2, 9), 2) }));
        Assert.That(capped, Has.Count.EqualTo(ControlMoments.MaxSuggestions));
        Assert.That(capped.Select(m => m.At), Is.Ordered.Descending);
    }

    [TestCase(-1, true)]
    [TestCase(0, true, Description = "alla presa in carico è a bordo")]
    [TestCase(24, false, Description = "alla consegna non lo è più")]
    [TestCase(-25, false)]
    public void IsOnBoardAt_FromPickupToDelivery(int hoursFromDelivery, bool expected)
    {
        //SETUP
        var consignment = new CarriedConsignment("uil", At(-2, 12), At(-1, 12));
        DateTimeOffset moment = hoursFromDelivery == 0 ? At(-2, 12) : hoursFromDelivery == 24 ? At(-1, 12) : At(-1, 12).AddHours(hoursFromDelivery);

        //SUT
        bool onBoard = consignment.IsOnBoardAt(moment);

        Assert.That(onBoard, Is.EqualTo(expected));
    }

    private static DateTimeOffset At(int days, int hour) => new DateTimeOffset(2026, 10, 6, hour, 0, 0, TimeSpan.Zero).AddDays(days);
}

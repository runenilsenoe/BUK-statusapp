using BukStatus.Core.Pot;
using BukStatus.Core.Status;

namespace BukStatus.Core.Tests;

public class EasterCampAdjustmentTests
{
    [Fact]
    public void Removes_statistical_credit_from_both_target_and_paid_amount_and_preserves_due_fraction()
    {
        var original = new MemberStatusSnapshot(1, 15000, 20000, 16000, new(2026, 11, 1), 75);
        var untouched = new MemberStatusSnapshot(2, 10000, 20000, 16000, null, 50);
        var date = new DateOnly(2026, 11, 25);
        var adjusted = EasterCampAdjustment.Apply(new Dictionary<int, MemberStatusSnapshot> { [1] = original, [2] = untouched },
            [new(1, date, 6400, "Fratrekk Påskecamp"), new(1, date, -400, "Korrigering nullføring påskecamp"),
             new(1, date, 3000, "Innbetaling påskecamp"), new(0, date, 9999, "Fratrekk Påskecamp")]);
        Assert.Equal(14000, adjusted[1].TotalTarget);
        Assert.Equal(9000, adjusted[1].TotalContribution);
        Assert.Equal(11200, adjusted[1].AjourTarget);
        Assert.Equal(2200, adjusted[1].RemainingForOnTrack);
        Assert.Null(adjusted[1].AjourUntil);
        Assert.Same(untouched, adjusted[2]);
        Assert.Equal(20000, original.TotalTarget);
        Assert.Equal(15000, original.TotalContribution);
    }

    [Fact]
    public void Target_does_not_become_negative_when_credit_covers_the_whole_plan()
    {
        var adjusted = EasterCampAdjustment.Apply(new Dictionary<int, MemberStatusSnapshot> { [1] = new(1, 6400, 6400, 6000, null, 100) },
            [new(1, new(2026, 11, 25), 6400, "Nullført påskecamp")]);
        Assert.Equal(0, adjusted[1].TotalTarget);
        Assert.Equal(0, adjusted[1].TotalContribution);
        Assert.Equal(0, adjusted[1].AjourTarget);
    }
}

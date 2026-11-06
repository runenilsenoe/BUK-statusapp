using BukStatus.Core.Pot;
using BukStatus.Core.Status;

namespace BukStatus.Core.Tests;

public class PotSimulatorTests
{
    private static PotMember Member(int personId, decimal contributed, decimal ajourTarget) =>
        new(personId, $"P{personId}", new MemberStatusSnapshot(personId, contributed, ajourTarget * 2, ajourTarget, null, 0));

    // Missing 1 000, 3 000 and 6 000; person 4 is ajour.
    private static readonly PotMember[] Members =
    [
        Member(1, contributed: 4_000, ajourTarget: 5_000),
        Member(2, contributed: 2_000, ajourTarget: 5_000),
        Member(3, contributed: 0, ajourTarget: 6_000),
        Member(4, contributed: 7_000, ajourTarget: 5_000),
    ];

    private static decimal Boost(PotResult result, int personId) => result.Members.Single(m => m.PersonId == personId).Boost;

    [Fact]
    public void Without_a_pot_only_counts_who_is_ajour()
    {
        var result = PotSimulator.Simulate(Members, 0, PotStrategy.Even);

        Assert.Equal(4, result.Count);
        Assert.Equal(1, result.OnTrackCount);
        Assert.Equal(25m, result.OnTrackPercent);
        Assert.Equal(25m, result.OnTrackWithPotPercent);
        Assert.Equal(10_000m, result.RemainingForAllOnTrack);
        Assert.Equal(0m, result.PotUsed);
    }

    [Fact]
    public void Even_split_gives_leftovers_from_cheap_members_to_the_rest()
    {
        // 6 000 over three: person 1 only needs 1 000, so the other two get 2 500 each.
        var result = PotSimulator.Simulate(Members, 6_000, PotStrategy.Even);

        Assert.Equal(1_000m, Boost(result, 1));
        Assert.Equal(2_500m, Boost(result, 2));
        Assert.Equal(2_500m, Boost(result, 3));
        Assert.Equal(0m, Boost(result, 4));
        Assert.Equal(6_000m, result.PotUsed);
        Assert.Equal(2, result.OnTrackWithPotCount); // 4 and 1
        Assert.Equal(4_000m, result.RemainingForAllOnTrack);
    }

    [Fact]
    public void Maximize_fills_the_cheapest_first()
    {
        var result = PotSimulator.Simulate(Members, 6_000, PotStrategy.MaximizeAjour);

        Assert.Equal(1_000m, Boost(result, 1));
        Assert.Equal(3_000m, Boost(result, 2));
        Assert.Equal(2_000m, Boost(result, 3));
        Assert.Equal(3, result.OnTrackWithPotCount);
        Assert.Equal(75m, result.OnTrackWithPotPercent);
    }

    [Fact]
    public void A_pot_larger_than_needed_is_left_over()
    {
        var result = PotSimulator.Simulate(Members, 15_000, PotStrategy.Even);

        Assert.Equal(10_000m, result.PotUsed);
        Assert.Equal(5_000m, result.PotLeft);
        Assert.Equal(100m, result.OnTrackWithPotPercent);
        Assert.Equal(0m, result.RemainingForAllOnTrack);
    }

    [Fact]
    public void Members_without_status_are_left_out_and_counted()
    {
        var result = PotSimulator.Simulate([.. Members, new PotMember(9, null, null)], 0, PotStrategy.Even);

        Assert.Equal(4, result.Count);
        Assert.Equal(1, result.MembersWithoutStatus);
    }

    [Fact]
    public void Combined_score_is_the_average_of_buk_and_samvirk()
    {
        var result = PotSimulator.Simulate(Members, 6_000, PotStrategy.MaximizeAjour); // 75 %

        Assert.Equal(73.5m, result.CombinedPercent(72));
    }

    [Fact]
    public void Milestones_are_parsed_sorted_and_invalid_ones_dropped()
    {
        var settings = new PotSettings { Milestones = "75; 70;abc;150" };

        Assert.Equal([70m, 75m], settings.MilestoneList);
    }
}

using BukStatus.Core.Groups;
using BukStatus.Core.Status;

namespace BukStatus.Core.Tests;

public class GroupStatusTests
{
    private static MemberStatusSnapshot Status(int personId, decimal contributed, decimal target, decimal ajourTarget) =>
        new(personId, contributed, target, ajourTarget, null, 0);

    private static Group GroupWith(params int[] personIds) => new()
    {
        Id = 1,
        Name = "Test",
        Members = personIds.Select(id => new GroupMember { GroupId = 1, PersonId = id }).ToList(),
    };

    [Fact]
    public void Aggregates_only_members_with_status()
    {
        var statuses = new Dictionary<int, MemberStatusSnapshot>
        {
            [1] = Status(1, contributed: 6_000, target: 10_000, ajourTarget: 5_000),  // ajour, 1 000 over
            [2] = Status(2, contributed: 1_000, target: 20_000, ajourTarget: 4_000),  // 3 000 short
        };

        var group = GroupStatusService.Build(GroupWith(1, 2, 3), statuses);

        Assert.Equal(3, group.MemberCount);
        Assert.Equal(2, group.MembersWithStatus);
        Assert.Equal(1, group.OnTrackCount);
        Assert.Equal(50m, group.OnTrackPercent);
        Assert.Equal(7_000m, group.TotalContribution);
        Assert.Equal(30_000m, group.TotalTarget);
        // Surplus from person 1 must not cover person 2's shortfall.
        Assert.Equal(3_000m, group.RemainingForAllOnTrack);
    }

    [Fact]
    public void Orders_members_behind_first_then_ajour_then_unknown()
    {
        var statuses = new Dictionary<int, MemberStatusSnapshot>
        {
            [1] = Status(1, 9_000, 10_000, 5_000),
            [2] = Status(2, 1_000, 10_000, 5_000), // 4 000 short
            [3] = Status(3, 4_000, 10_000, 5_000), // 1 000 short
        };

        var group = GroupStatusService.Build(GroupWith(4, 1, 3, 2), statuses);

        Assert.Equal([2, 3, 1, 4], group.Members.Select(m => m.PersonId));
    }

    [Fact]
    public void Empty_group_has_zero_percentages()
    {
        var group = GroupStatusService.Build(GroupWith(), new Dictionary<int, MemberStatusSnapshot>());

        Assert.Equal(0, group.OnTrackPercent);
        Assert.Equal(0, group.ProgressPercent);
    }

    [Fact]
    public void Member_exactly_at_ajour_target_is_on_track()
    {
        Assert.True(Status(1, 5_000, 10_000, 5_000).IsOnTrack);
        Assert.Equal(0, Status(1, 5_000, 10_000, 5_000).RemainingForOnTrack);
    }
}

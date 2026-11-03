using BukStatus.Core.Status;

namespace BukStatus.Core.Tests;

public class FakeMemberStatusServiceTests
{
    [Fact]
    public async Task Returns_stable_plausible_statuses()
    {
        var ids = Enumerable.Range(1, 500).ToList();
        var service = new FakeMemberStatusService();

        var first = await service.GetStatusesAsync(ids);
        var second = await service.GetStatusesAsync(ids);

        Assert.Equal(ids.Count, first.Count);
        Assert.All(first.Values, s =>
        {
            Assert.InRange(s.TotalContribution, 0, s.TotalTarget);
            Assert.True(s.AjourTarget <= s.TotalTarget);
            Assert.Equal(second[s.PersonId], s);
        });
        Assert.Contains(first.Values, s => s.IsOnTrack);
        Assert.Contains(first.Values, s => !s.IsOnTrack);
    }
}

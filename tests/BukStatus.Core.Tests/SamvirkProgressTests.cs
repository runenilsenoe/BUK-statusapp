using BukStatus.Core.Samvirk;
namespace BukStatus.Core.Tests;

public class SamvirkProgressTests
{
    static SamvirkPerson Person(int id, bool active, decimal? coins) => new(id, $"Person {id}", 49, null,
        new() { ["Active BUK Drive"] = active.ToString(), ["On Track Status"] = "NotOnTrack" }, new(), new(),
        [new(new(2026, 10, 1), null, 100, null, null), new(new(2026, 11, 1), null, coins, null, null)]);
    [Fact]
    public void Twenty_active_people_with_1500_coins_are_75_percent()
    {
        var people = Enumerable.Range(1, 20).Select(id => Person(id, true, id <= 10 ? 100 : 50))
            .Concat([Person(21, false, 100), Person(999, true, 100)]).ToArray();
        var progress = SamvirkProgress.Build(new(new(2026, 11, 10), DateTimeOffset.UtcNow, people), Enumerable.Range(1, 21));
        Assert.Equal(20, progress.ActiveCount);
        Assert.Equal(1500, progress.TotalCoins);
        Assert.Equal(2000, progress.MaximumCoins);
        Assert.Equal(75, progress.Percent);
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void Uses_coins_in_export_month_and_not_on_track_status(decimal coins, decimal expected)
    {
        var progress = SamvirkProgress.Build(new(new(2026,11,10), DateTimeOffset.UtcNow, [Person(1, true, coins)]), [1]);
        Assert.Equal(expected, progress.Percent);
    }
    [Fact]
    public void Missing_invalid_and_empty_data_do_not_claim_a_percentage()
    {
        foreach (decimal? coins in new decimal?[] { null, -1, 101 })
        {
            var progress = SamvirkProgress.Build(new(new(2026,11,10), DateTimeOffset.UtcNow, [Person(1, true, coins)]), [1]);
            Assert.Null(progress.Percent); Assert.Equal(1, progress.UnknownCount); Assert.Equal(100, progress.MaximumCoins);
        }
        var empty = SamvirkProgress.Build(new(new(2026,11,10), DateTimeOffset.UtcNow, [Person(1, false, 100)]), [1]);
        Assert.Null(empty.Percent);
    }
}

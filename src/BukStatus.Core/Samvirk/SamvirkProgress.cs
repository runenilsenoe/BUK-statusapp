namespace BukStatus.Core.Samvirk;

public record SamvirkProgress(DateOnly SnapshotDate, int ActiveCount, decimal TotalCoins, int UnknownCount)
{
    public decimal MaximumCoins => ActiveCount * 100m;
    public decimal? Percent => ActiveCount > 0 && UnknownCount == 0 ? TotalCoins / MaximumCoins * 100m : null;

    public static SamvirkProgress Build(SamvirkSnapshot snapshot, IEnumerable<int> memberIds)
    {
        var ids = memberIds.ToHashSet();
        var month = new DateOnly(snapshot.SnapshotDate.Year, snapshot.SnapshotDate.Month, 1);
        var active = snapshot.People.Where(p => ids.Contains(p.PersonId)
            && bool.TryParse(p.Fields.GetValueOrDefault("Active BUK Drive"), out var value) && value)
            .DistinctBy(p => p.PersonId).ToArray();
        var coins = active.Select(p => p.Months.FirstOrDefault(m => m.Month == month)?.Coins).ToArray();
        return new(snapshot.SnapshotDate, active.Length, coins.Where(c => c is >= 0 and <= 100).Sum(c => c!.Value),
            coins.Count(c => c is null or < 0 or > 100));
    }
}

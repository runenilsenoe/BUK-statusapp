namespace BukStatus.Core.Samvirk;

public record SamvirkProgress(DateOnly SnapshotDate, int ActiveCount, int OnTrackCount, int UnknownCount)
{
    public decimal? Percent => ActiveCount > 0 && UnknownCount == 0 ? 100m * OnTrackCount / ActiveCount : null;

    public static SamvirkProgress Build(SamvirkSnapshot snapshot, IEnumerable<int> memberIds)
    {
        var ids = memberIds.ToHashSet();
        var active = snapshot.People.Where(p => ids.Contains(p.PersonId)
            && bool.TryParse(p.Fields.GetValueOrDefault("Active BUK Drive"), out var value) && value)
            .DistinctBy(p => p.PersonId).ToArray();
        var onTrack = active.Count(p => string.Equals(p.Fields.GetValueOrDefault("On Track Status"), "OnTrack", StringComparison.OrdinalIgnoreCase));
        var behind = active.Count(p => string.Equals(p.Fields.GetValueOrDefault("On Track Status"), "NotOnTrack", StringComparison.OrdinalIgnoreCase));
        return new(snapshot.SnapshotDate, active.Length, onTrack, active.Length - onTrack - behind);
    }
}

using BukStatus.Core.Status;

namespace BukStatus.Core.Groups;

/// <param name="Status">Null when the API has no status for the person (e.g. no target this season).</param>
public record GroupMemberStatus(int PersonId, string? DisplayName, MemberStatusSnapshot? Status);

public record GroupStatus(int GroupId, string Name, IReadOnlyList<GroupMemberStatus> Members)
{
    private IEnumerable<MemberStatusSnapshot> Statuses => Members.Where(m => m.Status is not null).Select(m => m.Status!);

    public int MemberCount => Members.Count;
    public int MembersWithStatus => Statuses.Count();
    public int OnTrackCount => Statuses.Count(s => s.IsOnTrack);

    public decimal TotalContribution => Statuses.Sum(s => s.TotalContribution);
    public decimal TotalTarget => Statuses.Sum(s => s.TotalTarget);
    public decimal AjourTarget => Statuses.Sum(s => s.AjourTarget);

    /// <summary>Sum of what the members who aren't ajour are missing. Surplus from others doesn't count.</summary>
    public decimal RemainingForAllOnTrack => Statuses.Sum(s => s.RemainingForOnTrack);

    /// <summary>Share of members (with a status) who are ajour.</summary>
    public decimal OnTrackPercent => MembersWithStatus > 0 ? (decimal)OnTrackCount / MembersWithStatus * 100 : 0;

    public decimal ProgressPercent => TotalTarget > 0 ? TotalContribution / TotalTarget * 100 : 0;
}

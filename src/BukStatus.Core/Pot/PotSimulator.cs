using BukStatus.Core.Status;

namespace BukStatus.Core.Pot;

/// <summary>How the pot is spread over the members who aren't ajour.</summary>
public enum PotStrategy
{
    /// <summary>An equal share each, but never more than a member needs; what's left over goes to the others.</summary>
    Even,

    /// <summary>The members missing the least first, so as many as possible become ajour.</summary>
    MaximizeAjour,
}

/// <param name="Status">Null when the API has no status for the person; they're left out of the percentages.</param>
public record PotMember(int PersonId, string? DisplayName, MemberStatusSnapshot? Status);

/// <param name="Boost">The share of the pot given to this member.</param>
public record PotMemberResult(int PersonId, string? DisplayName, MemberStatusSnapshot Status, decimal Boost)
{
    public bool IsOnTrack => Status.IsOnTrack;

    /// <summary>Ajour once the pot is counted.</summary>
    public bool IsOnTrackWithPot => Status.IsOnTrack || RemainingWithPot <= 0;

    public decimal RemainingWithPot => Math.Max(0, Status.RemainingForOnTrack - Boost);

    /// <summary>Contribution as a share of the ajour target (100 = ajour).</summary>
    public decimal AjourPercent => Percent(Status.TotalContribution, Status.AjourTarget);

    public decimal AjourPercentWithPot => Percent(Status.TotalContribution + Boost, Status.AjourTarget);

    private static decimal Percent(decimal value, decimal of) => of > 0 ? value / of * 100 : 100;
}

/// <param name="Members">Members with a status, those missing the most first.</param>
public record PotResult(
    IReadOnlyList<PotMemberResult> Members,
    int MembersWithoutStatus,
    decimal Pot,
    decimal PotUsed,
    PotStrategy Strategy)
{
    public int Count => Members.Count;
    public int OnTrackCount => Members.Count(m => m.IsOnTrack);
    public int OnTrackWithPotCount => Members.Count(m => m.IsOnTrackWithPot);

    /// <summary>Share of the members who are ajour (BUK %).</summary>
    public decimal OnTrackPercent => Count > 0 ? (decimal)OnTrackCount / Count * 100 : 0;

    public decimal OnTrackWithPotPercent => Count > 0 ? (decimal)OnTrackWithPotCount / Count * 100 : 0;

    /// <summary>Pot left when everyone who can be helped is ajour.</summary>
    public decimal PotLeft => Pot - PotUsed;

    /// <summary>What's still missing for everyone to be ajour, after the pot.</summary>
    public decimal RemainingForAllOnTrack => Members.Sum(m => m.RemainingWithPot);

    /// <summary>What was missing before the pot.</summary>
    public decimal RemainingBeforePot => Members.Sum(m => m.Status.RemainingForOnTrack);

    /// <summary>The combined score: the average of BUK % (with the pot) and Samvirk %.</summary>
    public decimal CombinedPercent(decimal samvirkPercent) => (OnTrackWithPotPercent + samvirkPercent) / 2;
}

/// <summary>
/// Spreads a pot of extra contributions over the members who aren't ajour and works out the BUK share that would
/// then be ajour. This is the "Simulere pott" calculation from the old PACEFEST screen.
/// </summary>
public static class PotSimulator
{
    public static PotResult Simulate(IEnumerable<PotMember> members, decimal pot, PotStrategy strategy)
    {
        var all = members.ToList();
        var withStatus = all.Where(m => m.Status is not null).ToList();
        pot = Math.Max(0, pot);

        var boosts = new Dictionary<int, decimal>();
        // Cheapest first in both cases: with an even share, what the cheap ones don't need goes to the rest.
        var behind = withStatus
            .Where(m => !m.Status!.IsOnTrack)
            .OrderBy(m => m.Status!.RemainingForOnTrack)
            .ThenBy(m => m.PersonId)
            .ToList();

        var left = pot;
        for (var i = 0; i < behind.Count && left > 0; i++)
        {
            var needed = behind[i].Status!.RemainingForOnTrack;
            var share = strategy == PotStrategy.Even ? left / (behind.Count - i) : left;
            var boost = Math.Min(needed, share);
            boosts[behind[i].PersonId] = boost;
            left -= boost;
        }

        var results = withStatus
            .Select(m => new PotMemberResult(m.PersonId, m.DisplayName, m.Status!, boosts.GetValueOrDefault(m.PersonId)))
            .OrderByDescending(r => r.RemainingWithPot)
            .ThenBy(r => r.AjourPercentWithPot)
            .ThenBy(r => r.DisplayName ?? r.PersonId.ToString())
            .ToList();

        return new PotResult(results, all.Count - withStatus.Count, pot, pot - left, strategy);
    }
}

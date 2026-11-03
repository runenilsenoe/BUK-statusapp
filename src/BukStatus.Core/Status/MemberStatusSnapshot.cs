namespace BukStatus.Core.Status;

/// <summary>
/// One member's aksjon status, decoupled from the BCC API types so the website and bot don't depend on them.
/// </summary>
/// <param name="TotalTarget">The member's total aksjon target for the season.</param>
/// <param name="AjourTarget">What the member should have contributed by now to be ajour (on track).</param>
public record MemberStatusSnapshot(
    int PersonId,
    decimal TotalContribution,
    decimal TotalTarget,
    decimal AjourTarget,
    DateOnly? AjourUntil,
    decimal Score)
{
    public bool IsOnTrack => TotalContribution >= AjourTarget;

    public decimal RemainingForOnTrack => Math.Max(0, AjourTarget - TotalContribution);

    public decimal ProgressPercent => TotalTarget > 0 ? TotalContribution / TotalTarget * 100 : 0;
}

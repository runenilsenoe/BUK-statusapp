namespace BukStatus.Core.Teams;

/// <summary>
/// A BUK team ("lag" in the lagordning). Unlike groups, a person belongs to at most one team, and the team's
/// target is its members' aksjon targets plus <see cref="BonusPercent"/>.
/// </summary>
public class Team
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? LeaderName { get; set; }

    /// <summary>Extra share on top of the members' targets, e.g. 0.30 for 30 %.</summary>
    public decimal BonusPercent { get; set; }

    public int SortOrder { get; set; }
    public List<TeamMember> Members { get; set; } = [];
}

public class TeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; }

    /// <summary>BCC person id. Unique across all teams.</summary>
    public int PersonId { get; set; }

    public string? DisplayName { get; set; }
}

/// <summary>
/// How much of the year's target should be reached by the end of a month, cumulative (0.11 in January,
/// 0.22 in February, ... 1.0 in December). Drives the monthly and accumulated team targets.
/// </summary>
public class MonthlyTarget
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal CumulativePercent { get; set; }
}

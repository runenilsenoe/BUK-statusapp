using BukStatus.Core.Status;
using BukStatus.Core.Teams;

namespace BukStatus.Core.Tests;

public class TeamReportCalculatorTests
{
    private static readonly Dictionary<int, decimal> Targets = new()
    {
        [1] = 0.10m, [2] = 0.20m, [3] = 0.30m, [4] = 0.40m, [5] = 0.50m, [6] = 0.60m,
        [7] = 0.60m, [8] = 0.70m, [9] = 0.80m, [10] = 0.90m, [11] = 0.95m, [12] = 1m,
    };

    private static Team Team(int id, string name, decimal bonus, params int[] personIds) => new()
    {
        Id = id,
        Name = name,
        BonusPercent = bonus,
        Members = personIds.Select(p => new TeamMember { TeamId = id, PersonId = p }).ToList(),
    };

    private static MemberStatusSnapshot Status(int personId, decimal target) => new(personId, 0, target, 0, null, 0);

    private static ContributionEntry Paid(int personId, int year, int month, int day, decimal amount) =>
        new(personId, new DateOnly(year, month, day), amount, null);

    [Fact]
    public void Team_targets_include_the_bonus_and_follow_the_monthly_curve()
    {
        var teams = new List<Team> { Team(1, "Snekker", 0.30m, 1, 2) };
        var statuses = new Dictionary<int, MemberStatusSnapshot> { [1] = Status(1, 10_000), [2] = Status(2, 30_000) };
        var contributions = new Dictionary<int, IReadOnlyList<ContributionEntry>>
        {
            [1] = [Paid(1, 2026, 9, 15, 1_000), Paid(1, 2026, 10, 1, 2_000), Paid(1, 2025, 10, 1, 99_999)],
            [2] = [Paid(2, 2026, 10, 31, 500), Paid(2, 2026, 11, 2, 700)],
        };

        var report = TeamReportCalculator.Monthly(teams, statuses, contributions, Targets, 2026, 10);
        var team = report.Teams.Single();

        Assert.Equal(52_000m, team.YearTarget);               // 40 000 × 1.30
        Assert.Equal(52_000m * 0.10m, team.MonthTarget);      // October adds 90 % − 80 %
        Assert.Equal(2_500m, team.MonthCollected);            // October 2026 only
        Assert.Equal(52_000m * 0.90m, team.AccumulatedTarget);
        Assert.Equal(3_500m, team.AccumulatedCollected);      // Jan–Oct 2026, not 2025 or November
        Assert.Equal(4_200m, team.YearCollected);
        Assert.Equal(team.YearTarget, report.Total.YearTarget);
        Assert.Equal(0, report.MembersWithoutStatus);
    }

    [Fact]
    public void January_target_is_the_first_cumulative_value_and_missing_targets_give_zero()
    {
        var teams = new List<Team> { Team(1, "A", 0, 1) };
        var statuses = new Dictionary<int, MemberStatusSnapshot> { [1] = Status(1, 12_000) };
        var none = new Dictionary<int, IReadOnlyList<ContributionEntry>>();

        var january = TeamReportCalculator.Monthly(teams, statuses, none, Targets, 2026, 1);
        var noCurve = TeamReportCalculator.Monthly(teams, statuses, none, new Dictionary<int, decimal>(), 2026, 5);

        Assert.Equal(1_200m, january.Teams[0].MonthTarget);
        Assert.Equal(0, noCurve.Teams[0].AccumulatedTarget);
        Assert.Equal(0, noCurve.Teams[0].AccumulatedPercent);
    }

    [Fact]
    public void Members_without_status_count_with_zero_target()
    {
        var teams = new List<Team> { Team(1, "A", 0.5m, 1, 2) };
        var statuses = new Dictionary<int, MemberStatusSnapshot> { [1] = Status(1, 1_000) };

        var report = TeamReportCalculator.Monthly(teams, statuses, new Dictionary<int, IReadOnlyList<ContributionEntry>>(), Targets, 2026, 3);

        Assert.Equal(1_500m, report.Teams[0].YearTarget);
        Assert.Equal(1, report.MembersWithoutStatus);
    }

    [Fact]
    public void Weekly_report_spreads_the_target_over_iso_weeks_and_buckets_by_iso_week()
    {
        var teams = new List<Team> { Team(1, "A", 0, 1) };
        var statuses = new Dictionary<int, MemberStatusSnapshot> { [1] = Status(1, 53_000) };
        var contributions = new Dictionary<int, IReadOnlyList<ContributionEntry>>
        {
            // 2026-01-01 is a Thursday in ISO week 1 of 2026; 2025-12-29 is also in 2026-W01.
            [1] = [Paid(1, 2025, 12, 29, 100), Paid(1, 2026, 1, 1, 200), Paid(1, 2026, 1, 5, 300), Paid(1, 2025, 12, 28, 999)],
        };

        var report = TeamReportCalculator.Weekly(teams, statuses, contributions, 2026, new DateOnly(2026, 1, 14));
        var team = report.Teams.Single();

        Assert.Equal(53, report.WeeksInYear);
        Assert.Equal(1_000m, team.WeeklyBudget);
        Assert.Equal(300m, team.Collected[0]);
        Assert.Equal(300m, team.Collected[1]);
        Assert.Equal(3, report.LastWeek);
        Assert.Equal(600m, team.AccumulatedCollected(2));
        Assert.Equal(2_000m, team.AccumulatedBudget(2));
    }

    [Fact]
    public void Weekly_report_shows_all_weeks_for_past_years_and_none_for_future_years()
    {
        var teams = new List<Team> { Team(1, "A", 0) };
        var none = new Dictionary<int, MemberStatusSnapshot>();
        var noContributions = new Dictionary<int, IReadOnlyList<ContributionEntry>>();
        var today = new DateOnly(2026, 6, 1);

        Assert.Equal(52, TeamReportCalculator.Weekly(teams, none, noContributions, 2025, today).LastWeek);
        Assert.Equal(0, TeamReportCalculator.Weekly(teams, none, noContributions, 2027, today).LastWeek);
    }
}

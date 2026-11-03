using System.Globalization;
using BukStatus.Core.Status;

namespace BukStatus.Core.Teams;

public record MemberReportRow(
    int PersonId,
    string? DisplayName,
    MemberStatusSnapshot? Status,
    decimal YearTarget,
    decimal MonthCollected,
    decimal AccumulatedCollected,
    decimal YearCollected);

/// <summary>One team for one month: the month alone, the year so far, and the whole year.</summary>
public record TeamMonthRow(
    int? TeamId,
    string Name,
    string? LeaderName,
    decimal BonusPercent,
    int MemberCount,
    decimal YearTarget,
    decimal MonthTarget,
    decimal MonthCollected,
    decimal AccumulatedTarget,
    decimal AccumulatedCollected,
    decimal YearCollected,
    IReadOnlyList<MemberReportRow> Members)
{
    public decimal MonthPercent => Percent(MonthCollected, MonthTarget);
    public decimal AccumulatedPercent => Percent(AccumulatedCollected, AccumulatedTarget);
    public decimal YearPercent => Percent(YearCollected, YearTarget);
    public decimal AccumulatedRemaining => Math.Max(0, AccumulatedTarget - AccumulatedCollected);

    internal static decimal Percent(decimal value, decimal of) => of > 0 ? value / of * 100 : 0;
}

/// <param name="CumulativeTargetPercent">Share of the year's target due by the end of the month (0-1).</param>
/// <param name="MonthTargetPercent">Share of the year's target due in the month itself (0-1).</param>
/// <param name="MembersWithoutStatus">Team members the API has no status (target) for; they count with target 0.</param>
public record MonthlyTeamReport(
    int Year,
    int Month,
    decimal CumulativeTargetPercent,
    decimal MonthTargetPercent,
    IReadOnlyList<TeamMonthRow> Teams,
    TeamMonthRow Total,
    int MembersWithoutStatus);

/// <param name="Collected">Collected per ISO week, index 0 = week 1.</param>
public record TeamWeekRow(int? TeamId, string Name, decimal WeeklyBudget, IReadOnlyList<decimal> Collected)
{
    public decimal AccumulatedBudget(int week) => WeeklyBudget * week;
    public decimal AccumulatedCollected(int week) => Collected.Take(week).Sum();
}

/// <param name="LastWeek">The last ISO week to show (the current week, or the year's last week for past years).</param>
public record WeeklyTeamReport(int Year, int WeeksInYear, int LastWeek, IReadOnlyList<TeamWeekRow> Teams, TeamWeekRow Total);

/// <summary>
/// The team reports from the "Oversikt BUK Lagordning" workbook. A team's yearly target is the sum of its
/// members' aksjon targets times (1 + the team's bonus). Monthly targets follow the cumulative monthly target
/// curve; weekly budgets spread the yearly target evenly over the ISO weeks of the year.
/// </summary>
public static class TeamReportCalculator
{
    public static MonthlyTeamReport Monthly(
        IReadOnlyList<Team> teams,
        IReadOnlyDictionary<int, MemberStatusSnapshot> statuses,
        IReadOnlyDictionary<int, IReadOnlyList<ContributionEntry>> contributions,
        IReadOnlyDictionary<int, decimal> cumulativeTargets,
        int year,
        int month)
    {
        var cumulative = cumulativeTargets.GetValueOrDefault(month);
        var previous = month > 1 ? cumulativeTargets.GetValueOrDefault(month - 1) : 0;
        var monthShare = cumulative - previous;

        var rows = teams.Select(team =>
        {
            var members = team.Members.Select(member =>
            {
                var status = statuses.GetValueOrDefault(member.PersonId);
                var entries = InYear(contributions.GetValueOrDefault(member.PersonId), year);
                return new MemberReportRow(
                    member.PersonId,
                    member.DisplayName,
                    status,
                    (status?.TotalTarget ?? 0) * (1 + team.BonusPercent),
                    entries.Where(e => e.Date.Month == month).Sum(e => e.Amount),
                    entries.Where(e => e.Date.Month <= month).Sum(e => e.Amount),
                    entries.Sum(e => e.Amount));
            })
            .OrderBy(m => m.DisplayName ?? m.PersonId.ToString(CultureInfo.InvariantCulture))
            .ToList();

            var yearTarget = members.Sum(m => m.YearTarget);
            return new TeamMonthRow(team.Id, team.Name, team.LeaderName, team.BonusPercent, members.Count,
                yearTarget,
                yearTarget * monthShare,
                members.Sum(m => m.MonthCollected),
                yearTarget * cumulative,
                members.Sum(m => m.AccumulatedCollected),
                members.Sum(m => m.YearCollected),
                members);
        }).ToList();

        var total = new TeamMonthRow(null, "Totalt", null, 0, rows.Sum(r => r.MemberCount),
            rows.Sum(r => r.YearTarget), rows.Sum(r => r.MonthTarget), rows.Sum(r => r.MonthCollected),
            rows.Sum(r => r.AccumulatedTarget), rows.Sum(r => r.AccumulatedCollected), rows.Sum(r => r.YearCollected), []);

        var withoutStatus = teams.SelectMany(t => t.Members).Count(m => !statuses.ContainsKey(m.PersonId));
        return new MonthlyTeamReport(year, month, cumulative, monthShare, rows, total, withoutStatus);
    }

    public static WeeklyTeamReport Weekly(
        IReadOnlyList<Team> teams,
        IReadOnlyDictionary<int, MemberStatusSnapshot> statuses,
        IReadOnlyDictionary<int, IReadOnlyList<ContributionEntry>> contributions,
        int year,
        DateOnly today)
    {
        var weeksInYear = ISOWeek.GetWeeksInYear(year);
        var lastWeek = ISOWeek.GetYear(today.ToDateTime(TimeOnly.MinValue)) switch
        {
            var y when y > year => weeksInYear,
            var y when y < year => 0,
            _ => ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue)),
        };

        var rows = teams.Select(team =>
        {
            var yearTarget = team.Members.Sum(m => statuses.GetValueOrDefault(m.PersonId)?.TotalTarget ?? 0) * (1 + team.BonusPercent);
            var collected = new decimal[weeksInYear];
            foreach (var entry in team.Members.SelectMany(m => contributions.GetValueOrDefault(m.PersonId) ?? []))
            {
                var date = entry.Date.ToDateTime(TimeOnly.MinValue);
                if (ISOWeek.GetYear(date) == year)
                    collected[ISOWeek.GetWeekOfYear(date) - 1] += entry.Amount;
            }
            return new TeamWeekRow(team.Id, team.Name, yearTarget / weeksInYear, collected);
        }).ToList();

        var totalCollected = Enumerable.Range(0, weeksInYear).Select(i => rows.Sum(r => r.Collected[i])).ToList();
        var total = new TeamWeekRow(null, "Totalt", rows.Sum(r => r.WeeklyBudget), totalCollected);
        return new WeeklyTeamReport(year, weeksInYear, lastWeek, rows, total);
    }

    private static List<ContributionEntry> InYear(IReadOnlyList<ContributionEntry>? entries, int year) =>
        entries?.Where(e => e.Date.Year == year).ToList() ?? [];
}

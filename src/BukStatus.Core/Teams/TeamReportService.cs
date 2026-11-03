using BukStatus.Core.Status;

namespace BukStatus.Core.Teams;

/// <summary>Fetches teams, statuses and contributions, and builds the monthly and weekly team reports.</summary>
public class TeamReportService(
    TeamService teams,
    IMemberStatusService statuses,
    IContributionHistoryService contributions,
    TimeProvider time)
{
    public DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    public async Task<MonthlyTeamReport> GetMonthlyAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        var (allTeams, memberStatuses, history) = await LoadAsync(cancellationToken);
        var targets = await teams.GetMonthlyTargetsAsync(year, cancellationToken);
        return TeamReportCalculator.Monthly(allTeams, memberStatuses, history, targets, year, month);
    }

    public async Task<WeeklyTeamReport> GetWeeklyAsync(int year, CancellationToken cancellationToken = default)
    {
        var (allTeams, memberStatuses, history) = await LoadAsync(cancellationToken);
        return TeamReportCalculator.Weekly(allTeams, memberStatuses, history, year, Today);
    }

    private async Task<(List<Team>, IReadOnlyDictionary<int, MemberStatusSnapshot>, IReadOnlyDictionary<int, IReadOnlyList<ContributionEntry>>)>
        LoadAsync(CancellationToken cancellationToken)
    {
        var allTeams = await teams.GetAllAsync(cancellationToken);
        var personIds = allTeams.SelectMany(t => t.Members).Select(m => m.PersonId).ToList();
        var statusTask = statuses.GetStatusesAsync(personIds, cancellationToken);
        var historyTask = contributions.GetContributionsAsync(personIds, cancellationToken);
        return (allTeams, await statusTask, await historyTask);
    }
}

using BukStatus.Core.Pot;

namespace BukStatus.Core.Teams;

public record MemberMonthDetails(int PersonId, string Name, decimal MonthTarget, decimal MonthCollected,
    decimal AccumulatedTarget, decimal AccumulatedCollected, IReadOnlyList<ExtraContribution> Transactions);

public static class TeamMonthDetails
{
    public static IReadOnlyList<MemberMonthDetails> Build(TeamMonthRow team, MonthlyTeamReport report,
        IEnumerable<ExtraContribution> transactions)
    {
        var from = new DateOnly(report.Year, 1, 1);
        var month = new DateOnly(report.Year, report.Month, 1);
        var to = month.AddMonths(1);
        var byPerson = transactions.Where(t => t.Date >= from && t.Date < to)
            .ToLookup(t => t.PersonId);
        return team.Members.Select(member =>
        {
            var entries = byPerson[member.PersonId].ToArray();
            var monthly = entries.Where(e => e.Date >= month).OrderBy(e => e.Date).ThenBy(e => e.Id).ToArray();
            var name = !string.IsNullOrWhiteSpace(member.DisplayName) ? member.DisplayName
                : entries.Select(e => e.PersonName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? $"Person {member.PersonId}";
            return new MemberMonthDetails(member.PersonId, name,
                member.YearTarget * report.MonthTargetPercent, monthly.Sum(e => e.Amount),
                member.YearTarget * report.CumulativeTargetPercent, entries.Sum(e => e.Amount), monthly);
        }).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

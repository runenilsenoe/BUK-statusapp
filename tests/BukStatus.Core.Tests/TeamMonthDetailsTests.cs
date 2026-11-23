using BukStatus.Core.Pot;
using BukStatus.Core.Teams;

namespace BukStatus.Core.Tests;

public class TeamMonthDetailsTests
{
    [Fact]
    public void Details_reconcile_refunds_and_exclude_other_people_years_and_future_months()
    {
        var team = new TeamMonthRow(1, "Testlag", null, 0, 2, 20000, 2000, 999, 10000, 999, 999,
            [new(1, null, null, 12000, 999, 999, 999), new(2, "Ingen bidrag", null, 8000, 999, 999, 999)]);
        var report = new MonthlyTeamReport(2026, 2, .5m, .1m, [team], team, 0);
        ExtraContribution Paid(int person, int year, int month, int day, decimal amount) =>
            new(person, new(year, month, day), amount, "Arbeid", "Navn fra transaksjon");
        var rows = TeamMonthDetails.Build(team, report,
            [Paid(1, 2026, 1, 1, 1000), Paid(1, 2026, 2, 1, 500), Paid(1, 2026, 2, 28, -100),
             Paid(1, 2025, 2, 1, 99999), Paid(1, 2026, 3, 1, 99999), Paid(99, 2026, 2, 1, 99999),
             Paid(0, 2026, 2, 1, 99999)]);
        var member = Assert.Single(rows, r => r.PersonId == 1);
        Assert.Equal("Navn fra transaksjon", member.Name);
        Assert.Equal(1200, member.MonthTarget);
        Assert.Equal(400, member.MonthCollected);
        Assert.Equal(6000, member.AccumulatedTarget);
        Assert.Equal(1400, member.AccumulatedCollected);
        Assert.Equal(2, member.Transactions.Count);
        Assert.Equal(400, member.Transactions.Sum(t => t.Amount));
        var empty = Assert.Single(rows, r => r.PersonId == 2);
        Assert.Empty(empty.Transactions);
        Assert.Equal(0, empty.MonthCollected);
        Assert.Equal(800, empty.MonthTarget);
    }
}

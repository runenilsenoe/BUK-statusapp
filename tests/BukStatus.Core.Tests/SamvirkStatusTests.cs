using System.Text;
using BukStatus.Core.Samvirk;
namespace BukStatus.Core.Tests;
public class SamvirkStatusTests
{
    static SamvirkSnapshot Snapshot() => new(new(2026,1,10),DateTimeOffset.UtcNow,SamvirkParser.Parse(Encoding.UTF8.GetBytes("BCC Person ID;Display Name;Church Id;Birth Date;UNG Status;Active BUK Drive;On Track Amount NOK;Get On Track Amount NOK;Coins 2025-10;Coins 2025-11;Coins 2026-01\n1;Active person;49;11.01.2000;Inactive;True;3375;12345;50;0;100\n2;Inactive person;49;;Active;False;2700;;0;0;0\n3;Finished person;49;;Finished;False;0;;;;0"),"test.csv").People);
    [Fact] public void Drive_controls_tables_and_age_and_period_use_export_date()
    {
        var report = SamvirkStatus.Build(Snapshot());
        var person = Assert.Single(report.Active);
        Assert.Equal(2, report.Inactive.Length); Assert.Equal(0,report.UnknownStatus);
        Assert.Equal(25,person.Age); Assert.Equal(3375,person.MonthlyAmount); Assert.Equal(12345,person.BehindAmount);
        Assert.Equal(new DateOnly(2025,9,1),report.Months[0]); Assert.Equal(new DateOnly(2026,1,1),report.Months[4]);
        Assert.Null(report.Inactive[0].BehindAmount);
    }
    [Fact] public void Coins_map_to_confirmed_thresholds_with_configurable_minimum_and_preserve_unknowns()
    {
        var person = Assert.Single(SamvirkStatus.Build(Snapshot(),2800).Active);
        Assert.Null(person.Payments[0].Coins); Assert.Null(person.Payments[0].MinimumConfirmedAmount);
        Assert.Equal(50,person.Payments[1].Coins); Assert.Equal(2800,person.Payments[1].MinimumConfirmedAmount);
        Assert.Equal(0,person.Payments[2].Coins); Assert.Null(person.Payments[2].MinimumConfirmedAmount);
        Assert.Equal(100,person.Payments[4].Coins); Assert.Equal(3375,person.Payments[4].MinimumConfirmedAmount);
    }
    [Theory]
    [InlineData(3375, 375, 50, 3000)]
    [InlineData(2700, 1700, 0, 1000)]
    [InlineData(2700, 2700, 0, 0)]
    [InlineData(2700, 3400, 0, 0)]
    [InlineData(3375, 0, 50, 3375)]
    public void Current_month_paid_towards_full_discount_comes_from_remaining_amount(decimal monthly, decimal remaining, decimal coins, decimal expected)
    {
        var snapshot = Snapshot();
        var person = snapshot.People[0] with
        {
            Amounts = new() { ["On Track Amount NOK"] = monthly, ["Remaining Full Discount NOK"] = remaining },
            Months = [new(new(2025,12,1), null, 0, null, null), new(new(2026,1,1), null, coins, null, null)]
        };
        var row = Assert.Single(SamvirkStatus.Build(snapshot with { People = [person] }).Active);
        Assert.Equal(expected, row.Payments[4].CalculatedAmount);
        Assert.Null(row.Payments[3].CalculatedAmount);
    }
    [Fact]
    public void Dated_current_month_remainder_is_used_when_undated_value_is_missing()
    {
        var snapshot = Snapshot();
        var person = snapshot.People[0] with
        {
            Months = [new(new(2026,1,1), null, 0, null, 2375)],
        };
        Assert.Equal(1000, Assert.Single(SamvirkStatus.Build(snapshot with { People = [person] }).Active).Payments[4].CalculatedAmount);
    }
    [Fact]
    public void Missing_or_inconsistent_remainders_do_not_override_coins_confirmation()
    {
        var snapshot = Snapshot();
        SamvirkMonthlyPayment Payment(decimal? remaining, decimal coins) {
            var person = snapshot.People[0] with {
                Amounts = new() { ["On Track Amount NOK"] = 3375, ["Remaining Full Discount NOK"] = remaining },
                Months = [new(new(2026,1,1), null, coins, null, null)] };
            return Assert.Single(SamvirkStatus.Build(snapshot with { People = [person] }).Active).Payments[4];
        }
        Assert.Null(Payment(null, 0).CalculatedAmount);
        Assert.Null(Payment(-1, 0).CalculatedAmount);
        Assert.Null(Payment(3000, 50).CalculatedAmount);
        Assert.Equal(2700, Payment(3000, 50).MinimumConfirmedAmount);
        Assert.Null(Payment(500, 100).CalculatedAmount);
        Assert.Equal(3375, Payment(500, 100).MinimumConfirmedAmount);
    }

}

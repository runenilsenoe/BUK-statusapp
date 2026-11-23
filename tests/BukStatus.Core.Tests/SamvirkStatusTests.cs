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
}

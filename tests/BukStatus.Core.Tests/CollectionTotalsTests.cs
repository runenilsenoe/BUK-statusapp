using BukStatus.Core.Pot;

namespace BukStatus.Core.Tests;

public class CollectionTotalsTests
{
    [Fact]
    public void Totals_reconcile_income_operations_and_subsidies_without_double_counting_and_keep_refunds()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 11, 25);
        ExtraContribution Row(decimal amount, string? description) => new(1, to, amount, description);
        var totals = CollectionTotals.Build([
            Row(1000.25m, "Dugnad"), Row(200, "U18 DRIFT oktober"), Row(300, "U18 Pappbuene"),
            Row(50, "U18 Pappb - tømme"), Row(-20, "Korrigering driftsdugnad"), Row(400, "Tilskudd"),
            Row(75, "Tilskudd driftsdugnad"), Row(10, null), Row(100, "Oppdrag for Bedrift AS"),
            new(1, from.AddDays(-1), 99999, "Dugnad"), new(1, to.AddDays(1), 99999, "Dugnad")
        ], from, to);
        Assert.Equal(2115.25m, totals.Total);
        Assert.Equal(530m, totals.Operations);
        Assert.Equal(475m, totals.Subsidies);
        Assert.Equal(1110.25m, totals.Revenue);
        Assert.Equal(totals.Total, totals.Operations + totals.Subsidies + totals.Revenue);
    }
    [Theory]
    [InlineData("Overføring fra forrige år", true)]
    [InlineData("Overført fra 2025", true)]
    [InlineData("Overføring fra fjoråret", true)]
    [InlineData("Sponsing O26 - fra BCC Sørlandet", true)]
    [InlineData("Støtte fra BCC", true)]
    [InlineData("BCC - spons", true)]
    [InlineData("Spons fra onkel", false)]
    [InlineData("Støtte fra privatperson", false)]
    [InlineData("Overføring fra 2026", false)]
    [InlineData("Oppdrag 2025", false)]
    public void Carryover_and_bcc_support_are_excluded_but_other_sponsors_remain_income(string description, bool subsidy)
    {
        var date = new DateOnly(2026, 11, 25);
        var totals = CollectionTotals.Build([new(1, date, 100, description)], new(2026, 1, 1), date);
        Assert.Equal(subsidy ? 100 : 0, totals.Subsidies);
        Assert.Equal(subsidy ? 0 : 100, totals.Revenue);
        Assert.Equal(100, totals.Total);
    }
    [Theory]
    [InlineData("«Fratrekk Påskecamp»", true)]
    [InlineData("Nullført påskecamp", true)]
    [InlineData("Påskecamp - nullføring", true)]
    [InlineData("Nullforing paskecamp", true)]
    [InlineData("Null ført Påske camp", true)]
    [InlineData("Innbetaling påskecamp", false)]
    [InlineData("Fratrekk sommercamp", false)]
    public void Easter_adjustments_are_excluded_from_total_and_income_in_both_directions(string description, bool excluded)
    {
        var date = new DateOnly(2026, 11, 25);
        foreach (var amount in new[] { 100m, -100m })
        {
            var totals = CollectionTotals.Build([new(1, date, amount, description)], new(2026, 1, 1), date);
            Assert.Equal(excluded ? 0 : amount, totals.Total);
            Assert.Equal(excluded ? 0 : amount, totals.Revenue);
            Assert.Equal(0, totals.Operations);
        }
    }
}

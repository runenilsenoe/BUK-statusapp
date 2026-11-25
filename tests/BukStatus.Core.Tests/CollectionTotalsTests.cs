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
        Assert.Equal(605m, totals.Operations);
        Assert.Equal(400m, totals.Subsidies);
        Assert.Equal(1110.25m, totals.Revenue);
        Assert.Equal(totals.Total, totals.Operations + totals.Subsidies + totals.Revenue);
    }
}
